using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KidGuard {
    public static class TlsHello {
        static byte[] ReadExact(Stream s,int n) {
            var b=new byte[n]; int p=0;
            while(p<n) { int k=s.Read(b,p,n-p); if(k==0) throw new IOException("Truncated TLS record."); p+=k; }
            return b;
        }
        static int U16(byte[] b,int p) { if(p<0 || p+2>b.Length) throw new IOException("Truncated TLS vector."); return (b[p]<<8)|b[p+1]; }
        public static byte[] ReadAndValidate(Stream stream,string expected) {
            using(var wire=new MemoryStream()) using(var hs=new MemoryStream()) {
                int need=-1;
                for(int record=0;record<16;record++) {
                    byte[] h=ReadExact(stream,5); int n=U16(h,3);
                    if(h[0]!=22 || h[1]!=3 || n<1 || n>16384) throw new IOException("Expected a TLS handshake.");
                    byte[] b=ReadExact(stream,n); wire.Write(h,0,5); wire.Write(b,0,n); hs.Write(b,0,n);
                    byte[] hello=hs.ToArray();
                    if(hello.Length>=4 && need<0) {
                        if(hello[0]!=1) throw new IOException("Expected ClientHello.");
                        need=4+(hello[1]<<16)+(hello[2]<<8)+hello[3];
                        if(need>65536) throw new IOException("ClientHello too large.");
                    }
                    if(need>=0 && hello.Length>=need) {
                        // Reject trailing handshake bytes/early data in the opening records.
                        if(hello.Length!=need) throw new IOException("Unexpected opening TLS data.");
                        Validate(hello,expected); return wire.ToArray();
                    }
                }
                throw new IOException("Too many TLS fragments.");
            }
        }
        static void Validate(byte[] h,string expected) {
            int p=38;
            if(h.Length<=p) throw new IOException("Short ClientHello.");
            p+=1+h[p]; int cs=U16(h,p); p+=2+cs;
            if(p>=h.Length) throw new IOException("Invalid cipher vector.");
            p+=1+h[p]; int end=p+2+U16(h,p); p+=2;
            if(end!=h.Length) throw new IOException("Invalid extension vector.");
            string host=null; var seen=new HashSet<int>();
            while(p<end) {
                int type=U16(h,p),len=U16(h,p+2); p+=4; int next=p+len;
                if(next>end || !seen.Add(type)) throw new IOException("Invalid/duplicate TLS extension.");
                // 0xfe0d (Encrypted ClientHello) is accepted. Chrome/Edge send a GREASE (decoy) ECH extension on every
                // connection, even with ECH disabled by policy. Through a proxy the browser resolves no HTTPS DNS records,
                // so it has no ECH configuration and can only send GREASE. Real ECH would carry the provider's public
                // name as the visible SNI, which must still equal the allowed CONNECT host below; known ECH public names
                // cannot be whitelisted (Domains.EchPublicNames).
                if(type==0) {
                    int names=p+2+U16(h,p); int q=p+2;
                    if(names!=next) throw new IOException("Invalid SNI list.");
                    while(q<names) {
                        int kind=h[q++],count=U16(h,q); q+=2;
                        if(q+count>names || kind!=0 || host!=null) throw new IOException("Invalid SNI name.");
                        host=Domains.Normalize(Encoding.ASCII.GetString(h,q,count)); q+=count;
                    }
                }
                p=next;
            }
            if(host==null || !String.Equals(host,expected,StringComparison.OrdinalIgnoreCase)) throw new IOException("SNI must match the allowed CONNECT hostname.");
        }
    }
    public sealed class Proxy : IDisposable {
        readonly TcpListener listener;
        readonly ConcurrentDictionary<int,TcpClient> clients=new ConcurrentDictionary<int,TcpClient>();
        readonly Func<Policy> getPolicy;
        int sequence; volatile bool stopped;
        // Site check only (Parent Panel, on a spare port the child cannot reach: WFP lets the child connect to port 8877 alone).
        // Every host is let through and reported to Observe, so the parent sees all addresses a site uses.
        public bool AllowEverything;
        public Action<string> Observe;
        public Action<string> Report = Log.Write;
        public Proxy(Func<Policy> get) : this(get,8877) { }
        // Other ports are for end-to-end tests only; WFP exempts the child's traffic to 8877 alone.
        public Proxy(Func<Policy> get,int port) { getPolicy=get; listener=new TcpListener(IPAddress.Loopback,port); }
        public void Start() {
            listener.Server.ExclusiveAddressUse=true;
            listener.Start(64);
            Task.Run((System.Action)Accept);
        }
        void Accept() {
            while(!stopped) {
                try {
                    TcpClient c=listener.AcceptTcpClient();
                    if(clients.Count>=128) { c.Close(); continue; }
                    int id=Interlocked.Increment(ref sequence); clients[id]=c;
                    Task.Run(()=>Handle(c,id));
                } catch(Exception ex) { if(!stopped) { if(Observe!=null) return; Log.Write("Listener failed: "+ex.Message); Environment.Exit(2); } }
            }
        }
        public static bool PublicAddress(IPAddress ip) {
            if(ip.AddressFamily==AddressFamily.InterNetworkV6) {
                byte[] v=ip.GetAddressBytes();
                // Restrict to global unicast; exclude documentation and special 2001::/23 allocations.
                return (v[0]&0xe0)==0x20 && !(v[0]==0x20 && v[1]==1 && (v[2]<2 || (v[2]==0x0d && v[3]==0xb8)));
            }
            byte[] b=ip.GetAddressBytes();
            if(b.Length!=4) return false;
            return !(b[0]==0 || b[0]==10 || b[0]==127 || b[0]>=224 ||
                (b[0]==100 && b[1]>=64 && b[1]<=127) || (b[0]==169 && b[1]==254) ||
                (b[0]==172 && b[1]>=16 && b[1]<=31) ||
                (b[0]==192 && (b[1]==168 || (b[1]==0 && (b[2]==0 || b[2]==2)))) ||
                (b[0]==198 && (b[1]==18 || b[1]==19 || (b[1]==51 && b[2]==100))) ||
                (b[0]==203 && b[1]==0 && b[2]==113));
        }
        // Certificate-authority download hosts (intermediate certificates, OCSP, CRL). Many sites send an incomplete
        // certificate chain; browsers then download the missing certificate over plain HTTP. These hosts serve only
        // certificate data, so plain-HTTP GET to them is forwarded without being on the whitelist.
        static readonly HashSet<string> CertificateHosts=new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "cacerts.digicert.com","ocsp.digicert.com","crl3.digicert.com","crl4.digicert.com",
            "crt.sectigo.com","ocsp.sectigo.com","crl.sectigo.com","crt.usertrust.com","ocsp.usertrust.com","crl.usertrust.com",
            "secure.globalsign.com","ocsp.globalsign.com","ocsp2.globalsign.com","crl.globalsign.com",
            "aia.entrust.net","ocsp.entrust.net","crl.entrust.net","certificates.godaddy.com","ocsp.godaddy.com","crl.godaddy.com",
            "apps.identrust.com","validation.identrust.com"
        };
        // Domains used only for certificate infrastructure (Let's Encrypt, Google Trust Services, Amazon Trust Services).
        static readonly string[] CertificateDomains={ "lencr.org","pki.goog","amazontrust.com" };
        public static bool CertificateAuthorityHost(string host) {
            try { host=Domains.Normalize(host); } catch { return false; }
            return CertificateHosts.Contains(host) || CertificateDomains.Any(d => host==d || host.EndsWith("."+d,StringComparison.Ordinal));
        }
        // Connects to the first reachable public address of host (validated addresses only; resolved once).
        static async Task<TcpClient> ConnectPublic(string host,int port,CancellationToken token) {
            IPAddress[] addresses=await Dns.GetHostAddressesAsync(host);
            foreach(var ip in addresses.Where(PublicAddress).Take(4)) {
                var attempt=new TcpClient(ip.AddressFamily);
                try {
                    Task connect=attempt.ConnectAsync(ip,port);
                    if(await Task.WhenAny(connect,Task.Delay(4000,token))!=connect) throw new IOException("Upstream timeout.");
                    await connect; return attempt;
                } catch { attempt.Close(); }
            }
            throw new IOException("No reachable public address.");
        }
        static async Task ForwardCertificateDownload(NetworkStream down,Uri uri,string host,CancellationToken token) {
            using(var up=await ConnectPublic(host,80,token)) {
                NetworkStream s=up.GetStream();
                // A fresh, minimal request: nothing from the browser's own headers is passed on.
                byte[] request=Encoding.ASCII.GetBytes("GET "+uri.PathAndQuery+" HTTP/1.1\r\nHost: "+host+"\r\nAccept: */*\r\nUser-Agent: KidGuard-certificate-fetch\r\nConnection: close\r\n\r\n");
                await s.WriteAsync(request,0,request.Length,token);
                byte[] buffer=new byte[16384]; long total=0; int n;
                while((n=await s.ReadAsync(buffer,0,buffer.Length,token))>0) {
                    total+=n;
                    if(total>1024*1024) throw new IOException("Certificate download larger than 1 MB.");
                    await down.WriteAsync(buffer,0,n,token);
                }
            }
        }
        static void Reply(Stream s,string status,string extra) {
            byte[] b=Encoding.ASCII.GetBytes("HTTP/1.1 "+status+"\r\nContent-Length: 0\r\nConnection: close\r\n"+extra+"\r\n"); s.Write(b,0,b.Length);
        }
        static string Header(NetworkStream s) {
            using(var m=new MemoryStream()) {
                int state=0;
                while(m.Length<16384) {
                    int b=s.ReadByte(); if(b<0) throw new IOException("Truncated proxy request.");
                    if(b>127 || (b<32 && b!=9 && b!=10 && b!=13)) throw new IOException("Invalid header.");
                    m.WriteByte((byte)b);
                    state=(state==0 && b==13) ? 1 : (state==1 && b==10) ? 2 : (state==2 && b==13) ? 3 : (state==3 && b==10) ? 4 : (b==13 ? 1 : 0);
                    if(state==4) return Encoding.ASCII.GetString(m.ToArray());
                }
                throw new IOException("Proxy header too large.");
            }
        }
        async Task Handle(TcpClient client,int id) {
            TcpClient upstream=null; bool established=false; string host="";
            using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            using(timeout.Token.Register(()=>{try{client.Close();}catch{}})) {
                try {
                    NetworkStream down=client.GetStream(); down.ReadTimeout=5000; down.WriteTimeout=5000;
                    string[] lines=Header(down).Split(new[]{"\r\n"},StringSplitOptions.None);
                    string[] line=lines[0].Split(' ');
                    if(line.Length!=3 || (line[2]!="HTTP/1.1" && line[2]!="HTTP/1.0")) throw new IOException("Invalid request line.");
                    Policy p=getPolicy();
                    if(p==null || !p.InternetAllowed(DateTime.UtcNow)) { Reply(down,"403 Forbidden",""); return; }
                    if(line[0]!="CONNECT") {
                        Uri uri=null;
                        bool plain=(line[0]=="GET" || line[0]=="HEAD") && Uri.TryCreate(line[1],UriKind.Absolute,out uri) && uri.Scheme=="http" && uri.Port==80 && uri.UserInfo.Length==0;
                        if(plain && Observe!=null) { try { Observe(Domains.Normalize(uri.DnsSafeHost)); } catch { } }
                        if(plain && line[0]=="GET" && CertificateAuthorityHost(uri.DnsSafeHost)) {
                            // Missing intermediate certificate (AIA) or revocation data: browsers fetch these over plain HTTP.
                            host=Domains.Normalize(uri.DnsSafeHost); established=true;
                            await ForwardCertificateDownload(down,uri,host,timeout.Token);
                        } else if(plain && (AllowEverything || p.Domains.Allows(uri.DnsSafeHost))) {
                            var secure=new UriBuilder(uri) { Scheme="https",Port=443 };
                            Reply(down,"302 Found","Location: "+secure.Uri.AbsoluteUri+"\r\n");
                        } else {
                            if(uri!=null && uri.Scheme=="http") { try { string h=Domains.Normalize(uri.DnsSafeHost); Log.Write("Denied domain "+h+" (http)"); Blocked.Record(h); } catch { } }
                            Reply(down,"403 Forbidden","");
                        }
                        return;
                    }
                    if(!line[1].EndsWith(":443",StringComparison.Ordinal)) { Reply(down,"403 Forbidden",""); return; }
                    host=Domains.Normalize(line[1].Substring(0,line[1].Length-4));
                    if(Observe!=null) Observe(host);
                    if(!AllowEverything && !p.Domains.Allows(host)) { Log.Write("Denied domain "+host); Blocked.Record(host); Reply(down,"403 Forbidden",""); return; }
                    byte[] ok=Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n"); down.Write(ok,0,ok.Length); established=true;
                    byte[] hello=TlsHello.ReadAndValidate(down,host);
                    IPAddress[] addresses=await Dns.GetHostAddressesAsync(host);
                    // Pin the validated IP for this connection; never resolve the hostname a second time.
                    foreach(var ip in addresses.Where(PublicAddress).Take(4)) {
                        var attempt=new TcpClient(ip.AddressFamily);
                        try {
                            Task connect=attempt.ConnectAsync(ip,443);
                            if(await Task.WhenAny(connect,Task.Delay(4000,timeout.Token))!=connect) throw new IOException("Upstream timeout.");
                            await connect; upstream=attempt; break;
                        } catch { attempt.Close(); }
                    }
                    if(upstream==null) throw new IOException("No reachable public address.");
                    Policy now=getPolicy();
                    if(now==null || now.Revision!=p.Revision || !now.InternetAllowed(DateTime.UtcNow)) return;
                    NetworkStream up=upstream.GetStream();
                    await up.WriteAsync(hello,0,hello.Length);
                    timeout.CancelAfter(Timeout.Infinite); down.ReadTimeout=Timeout.Infinite; down.WriteTimeout=Timeout.Infinite;
                    // Every tunnel is closed on policy reload and at the internet cutoff by the service.
                    Task a=down.CopyToAsync(up), b=up.CopyToAsync(down);
                    await Task.WhenAny(a,b);
                    client.Close(); upstream.Close();
                    try { await Task.WhenAll(a,b); } catch { }
                } catch(Exception ex) {
                    if(Report!=null) Report("Proxy "+host+": "+ex.Message);
                    if(!established) try { Reply(client.GetStream(),"403 Forbidden",""); } catch { }
                } finally { if(upstream!=null) upstream.Close(); client.Close(); TcpClient removed; clients.TryRemove(id,out removed); }
            }
        }
        public void CloseAll() { foreach(var c in clients.Values) try { c.Close(); } catch { } }
        public void Dispose() { stopped=true; listener.Stop(); CloseAll(); }
    }
}
