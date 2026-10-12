using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Principal;
using System.Text;

namespace KidGuard {
    [DataContract] public class Installation {
        [DataMember(IsRequired=true)] public string userName;
        [DataMember(IsRequired=true)] public string userSid;
        [DataMember(IsRequired=true)] public bool originallyEnabled;
        // "local", "microsoft", "entra" or "domain". Absent means local.
        [DataMember(EmitDefaultValue=false)] public string kind;
        // Deny-sign-in rights the account already had before installation; never removed by KidGuard.
        [DataMember(EmitDefaultValue=false)] public string[] deniedBefore;

        public static Installation Read(string admin) {
            // Text first: Windows PowerShell 5.1 writes UTF-8 with a byte-order mark, which the JSON reader rejects.
            using(var ms=new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(admin,"installation.json")))))
                return Parse(ms);
        }
        public static Installation Parse(Stream s) { return (Installation)new DataContractJsonSerializer(typeof(Installation)).ReadObject(s); }
        // Local and Microsoft accounts are local SAM accounts underneath, with an account-disabled flag.
        // Entra ID and domain accounts have no local account object; sign-in is blocked with deny-logon rights.
        public bool UsesAccountFlag { get { return kind==null || kind=="local" || kind=="microsoft"; } }
        public void VerifyIdentity() {
            var sid=new SecurityIdentifier(userSid);
            if(userSid.EndsWith("-500") || sid.IsWellKnown(WellKnownSidType.AccountAdministratorSid) || sid.IsWellKnown(WellKnownSidType.LocalSystemSid))
                throw new Exception("Target account is protected.");
            if(!UsesAccountFlag) return;
            var byName=(SecurityIdentifier)new NTAccount(Environment.MachineName,userName).Translate(typeof(SecurityIdentifier));
            if(byName.Value!=userSid) throw new Exception("Target local account identity changed.");
        }
    }

    // "Deny log on locally" and "Deny log on through Remote Desktop" for one SID, via the LSA policy API.
    public static class LogonRights {
        public static readonly string[] Deny = { "SeDenyInteractiveLogonRight", "SeDenyRemoteInteractiveLogonRight" };
        [StructLayout(LayoutKind.Sequential)] struct LsaString { public ushort Length, MaximumLength; public IntPtr Buffer; }
        [StructLayout(LayoutKind.Sequential)] struct LsaAttributes { public int Length; public IntPtr RootDirectory, ObjectName; public uint Attributes; public IntPtr SecurityDescriptor, SecurityQualityOfService; }
        [DllImport("advapi32.dll")] static extern uint LsaOpenPolicy(IntPtr system, ref LsaAttributes attributes, uint access, out IntPtr policy);
        [DllImport("advapi32.dll")] static extern uint LsaAddAccountRights(IntPtr policy, byte[] sid, LsaString[] rights, uint count);
        [DllImport("advapi32.dll")] static extern uint LsaRemoveAccountRights(IntPtr policy, byte[] sid, bool all, LsaString[] rights, uint count);
        [DllImport("advapi32.dll")] static extern uint LsaEnumerateAccountRights(IntPtr policy, byte[] sid, out IntPtr rights, out uint count);
        [DllImport("advapi32.dll")] static extern uint LsaFreeMemory(IntPtr p);
        [DllImport("advapi32.dll")] static extern uint LsaClose(IntPtr policy);
        [DllImport("advapi32.dll")] static extern int LsaNtStatusToWinError(uint status);
        const uint PolicyAllAccess = 0x000F0FFF, ObjectNameNotFound = 0xC0000034;
        static void Check(uint status) { if(status!=0) throw new Win32Exception(LsaNtStatusToWinError(status)); }
        static byte[] Sid(string sid) { var s=new SecurityIdentifier(sid); var b=new byte[s.BinaryLength]; s.GetBinaryForm(b,0); return b; }
        static T WithPolicy<T>(Func<IntPtr,T> action) {
            var attributes=new LsaAttributes(); IntPtr policy;
            Check(LsaOpenPolicy(IntPtr.Zero,ref attributes,PolicyAllAccess,out policy));
            try { return action(policy); } finally { LsaClose(policy); }
        }
        // The deny rights this SID currently holds.
        public static List<string> Denied(string sid) {
            return WithPolicy(policy => {
                IntPtr rights; uint count;
                uint status=LsaEnumerateAccountRights(policy,Sid(sid),out rights,out count);
                var result=new List<string>();
                if(status==ObjectNameNotFound) return result;
                Check(status);
                try {
                    int size=Marshal.SizeOf(typeof(LsaString));
                    for(int i=0;i<count;i++) {
                        var s=(LsaString)Marshal.PtrToStructure(IntPtr.Add(rights,i*size),typeof(LsaString));
                        string name=Marshal.PtrToStringUni(s.Buffer,s.Length/2);
                        if(Deny.Contains(name)) result.Add(name);
                    }
                } finally { LsaFreeMemory(rights); }
                return result;
            });
        }
        static void Change(string sid,List<string> names,bool add) {
            if(names.Count==0) return;
            var strings=names.Select(n => new LsaString { Length=(ushort)(n.Length*2), MaximumLength=(ushort)(n.Length*2+2), Buffer=Marshal.StringToHGlobalUni(n) }).ToArray();
            try {
                WithPolicy(policy => {
                    Check(add ? LsaAddAccountRights(policy,Sid(sid),strings,(uint)strings.Length)
                              : LsaRemoveAccountRights(policy,Sid(sid),false,strings,(uint)strings.Length));
                    return true;
                });
            } finally { foreach(var s in strings) Marshal.FreeHGlobal(s.Buffer); }
        }
        // What to add or remove so the SID is blocked (allowed=false) or no longer blocked by KidGuard.
        // Rights the account had before installation are never removed.
        public static void Plan(bool allowed,ICollection<string> current,ICollection<string> before,List<string> add,List<string> remove) {
            foreach(string r in Deny) {
                if(!allowed && !current.Contains(r)) add.Add(r);
                if(allowed && current.Contains(r) && (before==null || !before.Contains(r))) remove.Add(r);
            }
        }
        // Returns true if anything changed.
        public static bool Apply(string sid,bool allowed,ICollection<string> before) {
            var add=new List<string>(); var remove=new List<string>();
            Plan(allowed,Denied(sid),before,add,remove);
            Change(sid,add,true); Change(sid,remove,false);
            return add.Count+remove.Count>0;
        }
    }

    public static class Account {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct User1 {
            public IntPtr name,password; public uint passwordAge,privilege; public IntPtr home,comment;
            public uint flags; public IntPtr script;
        }
        [StructLayout(LayoutKind.Sequential)] struct Session { public int id; public IntPtr station; public int state; }
        [DllImport("netapi32.dll",CharSet=CharSet.Unicode)] static extern uint NetUserGetInfo(string server,string user,uint level,out IntPtr info);
        [DllImport("netapi32.dll",CharSet=CharSet.Unicode)] static extern uint NetUserSetInfo(string server,string user,uint level,ref uint info,out uint error);
        [DllImport("netapi32.dll")] static extern uint NetApiBufferFree(IntPtr p);
        [DllImport("wtsapi32.dll",SetLastError=true)] static extern bool WTSEnumerateSessions(IntPtr server,int reserved,int version,out IntPtr info,out int count);
        [DllImport("wtsapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool WTSQuerySessionInformation(IntPtr server,int session,int infoClass,out IntPtr buffer,out int bytes);
        [DllImport("wtsapi32.dll",SetLastError=true)] static extern bool WTSQueryUserToken(int session,out IntPtr token);
        [DllImport("wtsapi32.dll",SetLastError=true)] static extern bool WTSLogoffSession(IntPtr server,int session,bool wait);
        [DllImport("wtsapi32.dll")] static extern void WTSFreeMemory(IntPtr p);
        [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr token,int infoClass,out int info,int length,out int returned);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        static void Check(uint r) { if(r!=0) throw new Win32Exception((int)r); }

        static void SetAccountFlag(Installation target,bool enabled) {
            IntPtr p; Check(NetUserGetInfo(null,target.userName,1,out p));
            uint flags;
            try { flags=((User1)Marshal.PtrToStructure(p,typeof(User1))).flags; }
            finally { NetApiBufferFree(p); }
            uint next=enabled ? flags & ~2u : flags | 2u;
            if(next!=flags) { uint error; Check(NetUserSetInfo(null,target.userName,1008,ref next,out error)); Log.Write(enabled ? "Account enabled" : "Account disabled"); }
        }
        // Opens or closes the sign-in gate for the target, using the mechanism its account type supports.
        public static void SetAllowed(Installation target,bool allowed) {
            target.VerifyIdentity();
            if(target.UsesAccountFlag) { SetAccountFlag(target,allowed); return; }
            if(LogonRights.Apply(target.userSid,allowed,target.deniedBefore))
                Log.Write(allowed ? "Sign-in allowed (deny-logon rights removed)" : "Sign-in blocked (deny-logon rights added)");
        }
        // Uninstall: put the account back as it was before installation.
        public static void Restore(Installation target) {
            target.VerifyIdentity();
            if(target.UsesAccountFlag) SetAccountFlag(target,target.originallyEnabled);
            else if(LogonRights.Apply(target.userSid,true,target.deniedBefore)) Log.Write("Deny-logon rights removed (uninstall)");
        }

        static string SessionText(int id,int kind) {
            IntPtr p; int bytes;
            if(!WTSQuerySessionInformation(IntPtr.Zero,id,kind,out p,out bytes)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { return Marshal.PtrToStringUni(p) ?? ""; } finally { WTSFreeMemory(p); }
        }
        // The session's user SID from its logon token (works for every account type; needs SYSTEM).
        static bool TokenUser(int id,out string sid,out bool admin) {
            sid=null; admin=false;
            IntPtr token;
            if(!WTSQueryUserToken(id,out token)) return false;
            try {
                using(var identity=new WindowsIdentity(token)) {
                    sid=identity.User.Value;
                    // 2/3 = full/limited token of a UAC-split administrator; IsInRole covers UAC turned off.
                    int type, length;
                    admin=(GetTokenInformation(token,18,out type,4,out length) && type!=1) ||
                        new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            } finally { CloseHandle(token); }
            return true;
        }
        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath(ref Guid id,uint flags,IntPtr token,out IntPtr path);
        // The session user's real Desktop folder (follows OneDrive/folder redirection). Needs SYSTEM; null if unavailable.
        public static string DesktopOf(int session) {
            IntPtr token;
            if(!WTSQueryUserToken(session,out token)) return null;
            try {
                Guid desktop=new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
                IntPtr path;
                if(SHGetKnownFolderPath(ref desktop,0,token,out path)!=0) return null;
                try { return Marshal.PtrToStringUni(path); } finally { Marshal.FreeCoTaskMem(path); }
            } finally { CloseHandle(token); }
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct StartupInfo {
            public int cb; public string reserved, desktop, title; public int x,y,width,height,columns,rows,fill,flags;
            public short show,reserved2; public IntPtr reserved3,input,output,error;
        }
        [StructLayout(LayoutKind.Sequential)] struct ProcessInformation { public IntPtr process,thread; public int processId,threadId; }
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcessAsUser(IntPtr token,string application,StringBuilder commandLine,IntPtr processAttributes,IntPtr threadAttributes,bool inherit,uint flags,IntPtr environment,string directory,ref StartupInfo startup,out ProcessInformation info);
        [DllImport("userenv.dll",SetLastError=true)] static extern bool CreateEnvironmentBlock(out IntPtr environment,IntPtr token,bool inherit);
        [DllImport("userenv.dll")] static extern bool DestroyEnvironmentBlock(IntPtr environment);
        // Starts a program as the session's signed-in user, on their desktop (needs SYSTEM). Returns the process id.
        public static int StartInSession(int session,string exe,string directory) {
            IntPtr token;
            if(!WTSQueryUserToken(session,out token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            IntPtr environment=IntPtr.Zero;
            try {
                if(!CreateEnvironmentBlock(out environment,token,false)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var startup=new StartupInfo { cb=Marshal.SizeOf(typeof(StartupInfo)), desktop="winsta0\\default" };
                ProcessInformation info;
                const uint CreateUnicodeEnvironment=0x400;
                if(!CreateProcessAsUser(token,exe,new StringBuilder("\""+exe+"\""),IntPtr.Zero,IntPtr.Zero,false,CreateUnicodeEnvironment,environment,directory,ref startup,out info))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                CloseHandle(info.thread); CloseHandle(info.process);
                return info.processId;
            } finally {
                if(environment!=IntPtr.Zero) DestroyEnvironmentBlock(environment);
                CloseHandle(token);
            }
        }
        [DllImport("wtsapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool WTSSendMessage(IntPtr server,int session,string title,int titleBytes,string message,int messageBytes,int style,int timeoutSeconds,out int response,bool wait);
        public static List<int> ChildSessions(Installation target) { bool admin; return ChildSessions(target,out admin); }
        // admin: true if any of the child's sessions has administrator rights (only detectable via tokens, i.e. as SYSTEM).
        public static List<int> ChildSessions(Installation target,out bool admin) {
            var result=new List<int>(); admin=false;
            IntPtr p; int count;
            if(!WTSEnumerateSessions(IntPtr.Zero,0,1,out p,out count)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                int size=Marshal.SizeOf(typeof(Session));
                for(int i=0;i<count;i++) {
                    var s=(Session)Marshal.PtrToStructure(IntPtr.Add(p,i*size),typeof(Session));
                    if(s.id==0) continue;
                    try {
                        string sid; bool sessionAdmin;
                        if(TokenUser(s.id,out sid,out sessionAdmin)) {
                            if(sid==target.userSid) { result.Add(s.id); admin|=sessionAdmin; }
                            continue;
                        }
                        // No token access (not SYSTEM) or no user: fall back to the session's name.
                        string name=SessionText(s.id,5),domain=SessionText(s.id,7);
                        if(name.Length==0) continue;
                        var byName=(SecurityIdentifier)new NTAccount(domain,name).Translate(typeof(SecurityIdentifier));
                        if(byName.Value==target.userSid) result.Add(s.id);
                    } catch(Exception ex) { Log.Write("Session check: " + ex.Message); }
                }
            } finally { WTSFreeMemory(p); }
            return result;
        }
        public static void Logoff(Installation target) {
            foreach(int id in ChildSessions(target)) {
                if(!WTSLogoffSession(IntPtr.Zero,id,false)) Log.Write("Sign-out failed for session "+id+": "+new Win32Exception(Marshal.GetLastWin32Error()).Message);
                else Log.Write("Requested sign-out for target session " + id);
            }
        }
        // Non-blocking message box in each of the child's sessions; it closes itself after timeoutSeconds.
        public static void Notify(Installation target,string title,string message,int timeoutSeconds) {
            const int MB_ICONINFORMATION=0x40, MB_TOPMOST=0x40000;
            foreach(int id in ChildSessions(target)) {
                int response;
                if(!WTSSendMessage(IntPtr.Zero,id,title,title.Length*2,message,message.Length*2,MB_ICONINFORMATION|MB_TOPMOST,timeoutSeconds,out response,false))
                    Log.Write("Warning to session "+id+" failed: "+new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }
        }
        public static void Enforce(Installation target,bool allowed) {
            using(var mutex=new System.Threading.Mutex(false,"Global\\KidGuardAccountGate")) {
                bool held=false;
                try {
                    try { held=mutex.WaitOne(10000); } catch(System.Threading.AbandonedMutexException) { held=true; }
                    if(!held) throw new TimeoutException("Account gate busy.");
                    SetAllowed(target,allowed);
                    if(!allowed) Logoff(target);
                } finally { if(held) mutex.ReleaseMutex(); }
            }
        }
    }
    public static class Log {
        static readonly object Sync=new object();
        public static string DirectoryPath;
        public static void Write(string message) {
            lock(Sync) {
                try {
                    if(DirectoryPath==null) { Console.Error.WriteLine(message); return; }
                    string path=Path.Combine(DirectoryPath,"kidguard.log");
                    if(File.Exists(path) && new FileInfo(path).Length>2*1024*1024) {
                        string old=path+".1"; if(File.Exists(old)) File.Delete(old); File.Move(path,old);
                    }
                    File.AppendAllText(path,DateTime.UtcNow.ToString("o")+" "+message+Environment.NewLine);
                } catch { /* Logging must not prevent enforcement. */ }
            }
        }
    }
}
