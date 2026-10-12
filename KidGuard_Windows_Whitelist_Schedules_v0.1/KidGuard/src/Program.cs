using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace KidGuard {
    // Written by the service every cycle to Admin\status.json for Manage.ps1 and the parent app.
    [DataContract] public class ServiceStatus {
        [DataMember] public string version=Program.Version;
        [DataMember] public string updated;
        [DataMember] public bool enforcing;
        [DataMember(EmitDefaultValue=false)] public string failClosedReason;
        [DataMember(EmitDefaultValue=false)] public string configError;
        [DataMember] public bool accountAllowed;
        [DataMember] public bool internetAllowed;
        [DataMember(EmitDefaultValue=false)] public string nextAccountChange;
        [DataMember(EmitDefaultValue=false)] public string nextInternetChange;
        [DataMember] public bool scheduleForceLock;
        [DataMember(EmitDefaultValue=false)] public Overrides overrides;
        [DataMember] public int childSessions;
        // The child's signed-in session has administrator rights: KidGuard can then be removed by the child.
        [DataMember] public bool childIsAdmin;
        public void Write(string admin) {
            using(var ms=new MemoryStream()) {
                new DataContractJsonSerializer(typeof(ServiceStatus)).WriteObject(ms,this);
                ConfigStore.WriteAtomic(Path.Combine(admin,"status.json"),Encoding.UTF8.GetString(ms.ToArray()));
            }
        }
        static string Time(DateTime? t) { return t.HasValue ? Overrides.Format(t.Value) : null; }
        public static ServiceStatus From(Policy p,DateTime now,string configError) {
            return new ServiceStatus {
                updated=Overrides.Format(now), enforcing=true, configError=configError,
                accountAllowed=p.AccountAllowed(now), internetAllowed=p.InternetAllowed(now),
                nextAccountChange=Time(p.NextChange(now,p.AccountAllowed)), nextInternetChange=Time(p.NextChange(now,p.InternetAllowed)),
                scheduleForceLock=p.Schedule.forceLock, overrides=p.Overrides
            };
        }
    }

    public class GuardService : ServiceBase {
        public static readonly string Admin=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Admin");
        static readonly int[] AccountWarnings={10,1}, InternetWarnings={5};
        readonly ManualResetEvent stop=new ManualResetEvent(false);
        readonly HashSet<string> warned=new HashSet<string>();
        Thread worker; Proxy proxy; Installation target; volatile Policy current; bool warnedAdmin; string lastStartPageError, lastHelperError;
        public GuardService() { ServiceName="KidGuard"; CanStop=true; CanShutdown=true; AutoLog=true; }
        protected override void OnStart(string[] args) {
            Log.DirectoryPath=Admin;
            target=Installation.Read(Admin); target.VerifyIdentity();
            if(!Wfp.Present()) throw new Exception("KidGuard WFP filters are absent.");
            // Explorer and the Start menu must be able to read the Parent Panel program to show its shortcut.
            try { PanelShortcuts.GrantRead(AppDomain.CurrentDomain.BaseDirectory); } catch(Exception ex) { Log.Write("Parent Panel shortcut permissions: "+ex.Message); }
            proxy=new Proxy(()=>current); proxy.Start();
            worker=new Thread(Run) { IsBackground=true }; worker.Start();
        }
        void Run() {
            string lastError=null, lastConfigError=null, savedGood=null;
            while(!stop.WaitOne(0)) {
                DateTime now=DateTime.UtcNow;
                try {
                    if(!Wfp.Present()) throw new Exception("WFP filters are missing.");
                    string configError;
                    // A broken edit keeps the last valid policy instead of signing the child out.
                    Policy next=ConfigStore.Load(Admin,out configError);
                    if(configError!=lastConfigError) {
                        if(configError!=null) Log.Write("Configuration error; keeping last valid policy: "+configError);
                        else if(lastConfigError!=null) Log.Write("Configuration error resolved.");
                        lastConfigError=configError;
                    }
                    bool changed=current==null || current.Revision!=next.Revision;
                    current=next;
                    if(configError==null && savedGood!=next.FullRevision) { ConfigStore.SaveLastGood(Admin,next); savedGood=next.FullRevision; }
                    bool account=next.AccountAllowed(now), internet=next.InternetAllowed(now);
                    if(changed || !internet) proxy.CloseAll();
                    Account.Enforce(target,account);
                    if(changed) Log.Write("Policy loaded.");
                    File.WriteAllText(Path.Combine(Admin,"heartbeat.txt"),now.ToString("o"));
                    lastError=null;
                    // Reporting must never trigger fail-closed enforcement.
                    var status=ServiceStatus.From(next,now,configError);
                    try { Warn(now,status); } catch(Exception ex) { Log.Write("Warning failed: "+ex.Message); }
                    // Preferences: the language of messages to the child, and whether the child's helper runs.
                    Settings settings=Settings.Read(Admin); L.Language=settings.Language;
                    List<int> sessions=new List<int>();
                    try {
                        bool admin; sessions=Account.ChildSessions(target,out admin); status.childSessions=sessions.Count; status.childIsAdmin=admin;
                        if(admin && !warnedAdmin) Log.Write("WARNING: the child account has administrator rights. Make it a standard account; otherwise it can remove KidGuard.");
                        warnedAdmin=admin;
                    } catch { }
                    try { Blocked.Flush(Admin,next.Domains); } catch(Exception ex) { Log.Write("Blocked list: "+ex.Message); }
                    try { StartPage.Maintain(Admin,target,sessions,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"kidguard.ico")); lastStartPageError=null; }
                    catch(Exception ex) { if(ex.Message!=lastStartPageError) { Log.Write("Start page: "+ex.Message); lastStartPageError=ex.Message; } }
                    try { ChildHelper.Cycle(Admin,target,sessions,status,next,now,settings,AppDomain.CurrentDomain.BaseDirectory); lastHelperError=null; }
                    catch(Exception ex) { if(ex.Message!=lastHelperError) { Log.Write("Child helper: "+ex.Message); lastHelperError=ex.Message; } }
                    try { status.Write(Admin); } catch { }
                } catch(Exception ex) {
                    current=null; proxy.CloseAll();
                    if(ex.Message!=lastError) { Log.Write("Fail closed: "+ex.Message); lastError=ex.Message; }
                    try { Account.Enforce(target,false); } catch(Exception gate) { Log.Write("Account gate failure: "+gate.Message); }
                    try { File.Delete(Path.Combine(Admin,"heartbeat.txt")); } catch { }
                    try { new ServiceStatus { updated=Overrides.Format(now), failClosedReason=ex.Message }.Write(Admin); } catch { }
                }
                if(stop.WaitOne(5000)) break;
            }
        }
        // One message per threshold per upcoming cutoff, e.g. 10 and 1 minutes before sign-out.
        void Warn(DateTime now,ServiceStatus s) {
            if(warned.Count>200) warned.Clear();
            DateTime? account=Overrides.Time(s.nextAccountChange), internet=Overrides.Time(s.nextInternetChange);
            if(s.accountAllowed && account.HasValue)
                Warn(now,"account",account.Value,AccountWarnings,"Windows will sign you out in {0}. Save your work now.");
            if(s.internetAllowed && internet.HasValue && (!account.HasValue || internet.Value<account.Value))
                Warn(now,"internet",internet.Value,InternetWarnings,"The internet will turn off in {0}.");
        }
        void Warn(DateTime now,string kind,DateTime at,int[] thresholds,string text) {
            double left=(at-now).TotalMinutes;
            if(left<=0) return;
            int hit=-1;
            foreach(int t in thresholds) if(left<=t && (hit<0 || t<hit)) hit=t;
            if(hit<0 || !warned.Add(kind+at.Ticks+":"+hit)) return;
            foreach(int t in thresholds) if(t>hit) warned.Add(kind+at.Ticks+":"+t);
            int minutes=(int)Math.Ceiling(left);
            Account.Notify(target,"KidGuard",L.T(text,L.Minutes(minutes)),60);
            Log.Write("Warned child: "+kind+" ends in "+minutes+" min.");
        }
        protected override void OnStop() {
            stop.Set(); current=null;
            if(proxy!=null) proxy.Dispose();
            if(worker!=null && !worker.Join(15000)) { Log.Write("Worker did not stop; failing service."); Environment.Exit(3); }
            try { File.Delete(Path.Combine(Admin,"heartbeat.txt")); } catch { }
            if(target!=null) Account.Enforce(target,false);
        }
        protected override void OnShutdown() { OnStop(); }
        public static void Watchdog() {
            Log.DirectoryPath=Admin; var target=Installation.Read(Admin); bool allowed=false;
            try {
                using(var sc=new ServiceController("KidGuard")) {
                    string heartbeat=Path.Combine(Admin,"heartbeat.txt"), ignored;
                    double age=(DateTime.UtcNow-File.GetLastWriteTimeUtc(heartbeat)).TotalSeconds;
                    allowed=sc.Status==ServiceControllerStatus.Running && File.Exists(heartbeat) && age>=0 && age<30 &&
                        Wfp.Present() && ConfigStore.Load(Admin,out ignored).AccountAllowed(DateTime.UtcNow);
                }
            } catch(Exception ex) { Log.Write("Watchdog fail closed: "+ex.Message); }
            Account.Enforce(target,allowed);
        }
    }

    // Parent commands. They write the Admin folder directly (Administrators-only ACL), so they work
    // even when the service is stopped; the service applies changes within about 5 seconds.
    public static class Commands {
        static string Admin { get { return GuardService.Admin; } }
        static int Minutes(string value) {
            int m;
            if(!Int32.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out m)) throw new ArgumentException("Minutes must be a whole number.");
            return m;
        }
        static string Arg(string[] args,int i,string usage) { if(args.Length<=i) throw new ArgumentException("Usage: KidGuard.exe "+usage); return args[i]; }
        static int Say(string message) { Console.WriteLine(message); return 0; }
        public static int Run(string[] args) {
            Log.DirectoryPath=Admin;
            switch(args[0]) {
                case "--lock": {
                    string v=Arg(args,1,"--lock <minutes|forever>");
                    return Say(Parent.Lock(Admin,v=="forever" ? (int?)null : Minutes(v)));
                }
                case "--unlock": return Say(Parent.Unlock(Admin));
                case "--bonus": return Say(Parent.Bonus(Admin,Minutes(Arg(args,1,"--bonus <minutes> [internet]")),args.Length>2 && args[2]=="internet"));
                case "--pause-internet": return Say(Parent.PauseInternet(Admin,Minutes(Arg(args,1,"--pause-internet <minutes>"))));
                case "--resume-internet": return Say(Parent.ResumeInternet(Admin));
                case "--clear-overrides": return Say(Parent.ClearOverrides(Admin));
                case "--allow": return Say(Parent.Allow(Admin,Arg(args,1,"--allow <domain|*.domain>")));
                case "--disallow": {
                    string entry=Domains.Entry(Arg(args,1,"--disallow <domain|*.domain>"));
                    if(ConfigStore.DisallowDomain(Admin,entry)) { Log.Write("Parent: Removed "+entry+"."); return Say("Removed "+entry+"."); }
                    Console.WriteLine(entry+" was not in the whitelist."); return 1;
                }
                case "--blocked":
                    foreach(var e in Blocked.Read(Admin)) Console.WriteLine(e.lastSeen+"  "+e.count.ToString().PadLeft(5)+"  "+e.domain);
                    return 0;
                case "--dismiss": {
                    string d=Arg(args,1,"--dismiss <domain>");
                    if(Blocked.Dismiss(Admin,d)) return Say("Dismissed "+d+".");
                    Console.WriteLine(d+" is not in the blocked list."); return 1;
                }
                case "--apply-whitelist": return Say(Parent.SaveWhitelist(Admin,File.ReadAllText(Arg(args,1,"--apply-whitelist <file>"))));
                case "--apply-schedule": return Say(Parent.SaveSchedule(Admin,File.ReadAllText(Arg(args,1,"--apply-schedule <file>"))));
                case "--check-whitelist": new Domains(File.ReadAllText(Arg(args,1,"--check-whitelist <file>"))); return Say("Whitelist valid.");
                case "--check-schedule": Schedule.Parse(File.ReadAllText(Arg(args,1,"--check-schedule <file>"))); return Say("Schedule valid.");
                // Used by Install.ps1 / Uninstall.ps1.
                case "--deny-rights": foreach(var r in LogonRights.Denied(Arg(args,1,"--deny-rights <sid>"))) Console.WriteLine(r); return 0;
                case "--close-gate": Account.Enforce(Installation.Read(Admin),false); return Say("Child sign-in blocked.");
                case "--restore-account": Account.Restore(Installation.Read(Admin)); return Say("Child account restored to its state before installation.");
                case "--startpage-import": return Say(StartPage.Import(Admin,Arg(args,1,"--startpage-import <file.html>")));
                case "--startpage-enable": return Say(StartPage.Enable(Admin));
                case "--startpage-disable": return Say(StartPage.Disable(Admin));
                case "--startpage-allow": return Say(StartPage.AllowLinks(Admin));
                case "--startpage-remove": StartPage.RemoveDeployed(Admin); return Say("Start page shortcuts and copy removed.");
                case "--child-remove": ChildHelper.Remove(); return Say("Child helper closed and its folder removed.");
                case "--panel-shortcuts":
                    foreach(string path in PanelShortcuts.Create(AppDomain.CurrentDomain.BaseDirectory)) Console.WriteLine(path);
                    return 0;
                case "--language": {
                    string lang=Arg(args,1,"--language <en|bg>");
                    if(lang!="en" && lang!="bg") throw new ArgumentException("Usage: KidGuard.exe --language <en|bg>");
                    return Say(Parent.SetLanguage(Admin,lang));
                }
                case "--helper": {
                    string v=Arg(args,1,"--helper <on|off>");
                    if(v!="on" && v!="off") throw new ArgumentException("Usage: KidGuard.exe --helper <on|off>");
                    return Say(Parent.SetChildHelper(Admin,v=="on"));
                }
                case "--requests":
                    foreach(var r in Requests.Read(Admin))
                        Console.WriteLine(r.asked+"  "+r.id+"  "+r.state.PadRight(8)+"  "+(r.kind=="site" ? r.site : "+"+r.minutes+" min")+(r.note!=null ? "  \""+r.note+"\"" : ""));
                    return 0;
                case "--answer": {
                    const string usage="--answer <id> <allow|allow-site|deny>";
                    string id=Arg(args,1,usage), how=Arg(args,2,usage);
                    if(how=="deny") return Say(Parent.DenyRequest(Admin,id));
                    if(how!="allow" && how!="allow-site") throw new ArgumentException("Usage: KidGuard.exe "+usage);
                    return Say(Requests.Waiting(Admin,id).kind=="time" ? Parent.GiveRequestedTime(Admin,id,true) : Parent.AllowRequestedSite(Admin,id,how=="allow-site"));
                }
                case "--startpage-check": {
                    string html=File.ReadAllText(Arg(args,1,"--startpage-check <file.html>"));
                    var hosts=StartPage.LinkHosts(html);
                    if(hosts.Count==0) throw new FormatException("The page contains no http:// or https:// links.");
                    return Say("Start page valid: "+hosts.Count+" linked sites; shortcut name: "+StartPage.ShortcutName(StartPage.Title(html)));
                }
                // Parent side, changes nothing: which addresses does a site need? (The Parent Panel does the same with checkboxes.)
                case "--check-site": {
                    string url=Arg(args,1,"--check-site <address> [extra pages]");
                    int extra=args.Length>2 ? Minutes(args[2]) : 2;
                    var seen=SiteCheck.Run(url,extra,(i,n,u)=>Console.Error.WriteLine("Page "+i+" of "+n+": "+u),System.Threading.CancellationToken.None);
                    Uri start=new Uri(url.Contains("://") ? url : "https://"+url);
                    foreach(var a in SiteCheck.Sort(Domains.Normalize(start.Host),seen))
                        Console.WriteLine((a.Suggested ? "allow  " : "skip   ")+a.Kind.PadRight(9)+a.Rule+(a.Count>0 ? "  ("+a.Count+")" : ""));
                    return 0;
                }
                case "--check-bundles": {
                    var list=Bundle.Parse(File.ReadAllText(Arg(args,1,"--check-bundles <file>")));
                    return Say("Bundles valid: "+list.Count+" sites.");
                }
            }
            return -1;
        }
        public static void Validate(string dir) {
            DateTime now=DateTime.UtcNow;
            var p=Policy.Read(dir);
            Console.WriteLine("Configuration valid. Account now: "+p.AccountAllowed(now)+"; internet now: "+p.InternetAllowed(now));
            DateTime? a=p.NextChange(now,p.AccountAllowed), i=p.NextChange(now,p.InternetAllowed);
            Console.WriteLine("Next account change: "+(a.HasValue ? Parent.Local(Overrides.Format(a.Value),p.Schedule) : "none within 8 days")+
                "; next internet change: "+(i.HasValue ? Parent.Local(Overrides.Format(i.Value),p.Schedule) : "none within 8 days"));
        }
    }

    public static class Program {
        public const string Version="0.1";
        public static int Main(string[] args) {
            try {
                if(args.Length==0) { ServiceBase.Run(new GuardService()); return 0; }
                switch(args[0]) {
                    case "--self-test": SelfTests.Run(); return 0;
                    case "--validate": Commands.Validate(args.Length>1 ? args[1] : GuardService.Admin); return 0;
                    case "--install-filter": Wfp.Install(args[1]); return 0;
                    case "--remove-filter": Wfp.Remove(); return 0;
                    case "--guard": GuardService.Watchdog(); return 0;
                    case "--status": Console.WriteLine("WFP filters present: "+Wfp.Present()); return Wfp.Present() ? 0 : 1;
                    case "--version": Console.WriteLine(Version); return 0;
                }
                int code=Commands.Run(args);
                if(code<0) throw new ArgumentException("Unknown command: "+args[0]);
                return code;
            } catch(System.ComponentModel.Win32Exception ex) {
                Console.Error.WriteLine(ex.NativeErrorCode==5 ? ex.Message+". Run from an elevated (Administrator) PowerShell." : ex.ToString()); return 1;
            } catch(UnauthorizedAccessException ex) {
                Console.Error.WriteLine(ex.Message+" Run from an elevated (Administrator) PowerShell."); return 1;
            } catch(Exception ex) {
                bool expected=ex is ArgumentException || ex is FormatException || ex is InvalidOperationException || ex is SerializationException || ex is IOException;
                Console.Error.WriteLine(expected ? ex.Message : ex.ToString()); return 1;
            }
        }
    }
}
