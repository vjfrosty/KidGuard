using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace KidGuard {
    // x64 Windows ABI only. No packet polling, DNS reverse lookup or kernel driver.
    public static class Wfp {
        static readonly Guid ProviderKey = new Guid("d6f0df1b-c4c8-47da-a91c-0ae473224101");
        static readonly Guid SubKey = new Guid("d6f0df1b-c4c8-47da-a91c-0ae473224102");
        static readonly Guid V4 = new Guid("c38d57d1-05a7-4c33-904f-7fbceee60e82");
        static readonly Guid V6 = new Guid("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
        static readonly Guid User = new Guid("af043a0a-b34d-4f86-979c-c90371af6e66");
        static readonly Guid Remote = new Guid("b235ae9a-1d64-49b8-a44c-5ff3d9095045");
        static readonly Guid Port = new Guid("c35a604d-d22b-4e1a-91b4-68f674ee674b");
        static readonly Guid Protocol = new Guid("3971ef2b-623e-4f9a-8cb1-6e79b806b9a7");
        static Guid FilterKey(int n) { return new Guid("d6f0df1b-c4c8-47da-a91c-0ae47322420" + n); }
        [StructLayout(LayoutKind.Sequential)] struct Display { public IntPtr name, description; }
        [StructLayout(LayoutKind.Sequential)] struct Blob { public uint size; public IntPtr data; }
        [StructLayout(LayoutKind.Explicit, Size=16)] struct Value {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public IntPtr pointer;
            [FieldOffset(8)] public uint number;
        }
        [StructLayout(LayoutKind.Sequential)] struct Condition { public Guid field; public uint match; public Value value; }
        [StructLayout(LayoutKind.Sequential)] struct Action { public uint type; public Guid key; }
        [StructLayout(LayoutKind.Explicit, Size=16)] struct Context {
            [FieldOffset(0)] public ulong raw; [FieldOffset(0)] public Guid key;
        }
        [StructLayout(LayoutKind.Sequential)] struct Filter {
            public Guid key; public Display display; public uint flags; public IntPtr provider;
            public Blob data; public Guid layer, sublayer; public Value weight;
            public uint count; public IntPtr conditions; public Action action;
            public Context context; public IntPtr reserved; public ulong id; public Value effectiveWeight;
        }
        [StructLayout(LayoutKind.Sequential)] struct Provider { public Guid key; public Display display; public uint flags; public Blob data; public IntPtr service; }
        [StructLayout(LayoutKind.Sequential)] struct Sublayer { public Guid key; public Display display; public uint flags; public IntPtr provider; public Blob data; public ushort weight; }
        [DllImport("fwpuclnt.dll", CharSet=CharSet.Unicode)] static extern uint FwpmEngineOpen0(string server, uint auth, IntPtr identity, IntPtr session, out IntPtr engine);
        [DllImport("fwpuclnt.dll")] static extern uint FwpmEngineClose0(IntPtr engine);
        [DllImport("fwpuclnt.dll")] static extern uint FwpmTransactionBegin0(IntPtr engine, uint flags);
        [DllImport("fwpuclnt.dll")] static extern uint FwpmTransactionCommit0(IntPtr engine);
        [DllImport("fwpuclnt.dll")] static extern uint FwpmTransactionAbort0(IntPtr engine);
        [DllImport("fwpuclnt.dll")] static extern uint FwpmProviderAdd0(IntPtr e, ref Provider p, IntPtr sd);
        [DllImport("fwpuclnt.dll")] static extern uint FwpmSubLayerAdd0(IntPtr e, ref Sublayer p, IntPtr sd);
        [DllImport("fwpuclnt.dll")] static extern uint FwpmFilterAdd0(IntPtr e, ref Filter f, IntPtr sd, out ulong id);
        [DllImport("fwpuclnt.dll")] static extern uint FwpmFilterGetByKey0(IntPtr e, ref Guid key, out IntPtr f);
        [DllImport("fwpuclnt.dll")] static extern void FwpmFreeMemory0(ref IntPtr p);
        [DllImport("fwpuclnt.dll")] static extern uint FwpmFilterDeleteByKey0(IntPtr e, ref Guid k);
        [DllImport("fwpuclnt.dll")] static extern uint FwpmSubLayerDeleteByKey0(IntPtr e, ref Guid k);
        [DllImport("fwpuclnt.dll")] static extern uint FwpmProviderDeleteByKey0(IntPtr e, ref Guid k);
        [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string s, uint rev, out IntPtr sd, out uint size);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
        static void Check(uint code) { if (code != 0) throw new Win32Exception(unchecked((int)code)); }
        sealed class Arena : IDisposable {
            readonly List<IntPtr> pointers = new List<IntPtr>();
            public IntPtr Raw(int size) { var p=Marshal.AllocHGlobal(size); pointers.Add(p); return p; }
            public IntPtr Put<T>(T v) where T:struct { var p=Raw(Marshal.SizeOf(typeof(T))); Marshal.StructureToPtr(v,p,false); return p; }
            public IntPtr Text(string v) { var p=Marshal.StringToHGlobalUni(v); pointers.Add(p); return p; }
            public void Dispose() { foreach(var p in pointers) Marshal.FreeHGlobal(p); }
        }
        static IntPtr Open() { AssertLayout(); IntPtr e; Check(FwpmEngineOpen0(null,10,IntPtr.Zero,IntPtr.Zero,out e)); return e; }
        public static void AssertLayout() {
            if (IntPtr.Size != 8 || Marshal.SizeOf(typeof(Filter)) != 200 || Marshal.SizeOf(typeof(Condition)) != 40 ||
                Marshal.OffsetOf(typeof(Filter),"context").ToInt32() != 152) throw new Exception("Unsupported WFP ABI; use x64 Windows.");
        }
        public static void Install(string sid) {
            new SecurityIdentifier(sid);
            IntPtr e=Open(), sd=IntPtr.Zero; bool tx=false;
            try {
                Check(FwpmTransactionBegin0(e,0)); tx=true;
                using(var a=new Arena()) {
                    IntPtr provider=a.Put(ProviderKey);
                    var p=new Provider { key=ProviderKey,display=new Display{name=a.Text("KidGuard")},flags=1,service=a.Text("KidGuard") };
                    Check(FwpmProviderAdd0(e,ref p,IntPtr.Zero));
                    var sub=new Sublayer { key=SubKey,display=new Display{name=a.Text("KidGuard child SID")},flags=1,provider=provider,weight=0xF000 };
                    Check(FwpmSubLayerAdd0(e,ref sub,IntPtr.Zero));
                    uint size;
                    if(!ConvertStringSecurityDescriptorToSecurityDescriptor("D:(A;;0x1;;;"+sid+")",1,out sd,out size)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    var user=new Condition {field=User,value=new Value{type=14,pointer=a.Put(new Blob{size=size,data=sd})}};
                    Add(e,a,provider,0,V4,new[]{user},false);
                    Add(e,a,provider,1,V6,new[]{user},false);
                    Add(e,a,provider,2,V4,new[]{user,
                        new Condition{field=Remote,value=new Value{type=3,number=0x7f000001}},
                        new Condition{field=Port,value=new Value{type=2,number=8877}},
                        new Condition{field=Protocol,value=new Value{type=1,number=6}}},true);
                }
                Check(FwpmTransactionCommit0(e)); tx=false;
            } finally { if(tx) FwpmTransactionAbort0(e); if(sd!=IntPtr.Zero) LocalFree(sd); FwpmEngineClose0(e); }
        }
        static void Add(IntPtr e,Arena a,IntPtr provider,int n,Guid layer,Condition[] cs,bool allow) {
            int size=Marshal.SizeOf(typeof(Condition)); IntPtr list=a.Raw(size*cs.Length);
            for(int i=0;i<cs.Length;i++) Marshal.StructureToPtr(cs[i],IntPtr.Add(list,i*size),false);
            var f=new Filter { key=FilterKey(n),display=new Display{name=a.Text(allow ? "KidGuard local proxy only" : "KidGuard direct outbound block")},flags=1,
                provider=provider,layer=layer,sublayer=SubKey,weight=new Value{type=4,pointer=a.Put((ulong)(allow ? 100 : 10))},
                count=(uint)cs.Length,conditions=list,action=new Action{type=(uint)(allow ? 0x1002 : 0x1001)} };
            ulong id; Check(FwpmFilterAdd0(e,ref f,IntPtr.Zero,out id));
        }
        public static bool Present() {
            IntPtr e=Open(); try { for(int i=0;i<3;i++) { Guid k=FilterKey(i); IntPtr p; uint r=FwpmFilterGetByKey0(e,ref k,out p); if(r!=0) return false; bool enabled; try { enabled=(((Filter)Marshal.PtrToStructure(p,typeof(Filter))).flags & 0x20)==0; } finally { FwpmFreeMemory0(ref p); } if(!enabled) return false; } return true; }
            finally { FwpmEngineClose0(e); }
        }
        public static void Remove() {
            IntPtr e=Open(); bool tx=false;
            try {
                Check(FwpmTransactionBegin0(e,0)); tx=true;
                for(int i=0;i<3;i++) { Guid k=FilterKey(i); uint r=FwpmFilterDeleteByKey0(e,ref k); if(r!=0 && r!=0x80320003) Check(r); }
                Guid sub=SubKey, provider=ProviderKey;
                uint s=FwpmSubLayerDeleteByKey0(e,ref sub); if(s!=0 && s!=0x80320007) Check(s);
                uint p=FwpmProviderDeleteByKey0(e,ref provider); if(p!=0 && p!=0x80320005) Check(p);
                Check(FwpmTransactionCommit0(e)); tx=false;
            } finally { if(tx) FwpmTransactionAbort0(e); FwpmEngineClose0(e); }
        }
    }
}
