using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

class LiveZ {
    static readonly object gate = new object();
    static readonly Dictionary<string, Guest> guests = new Dictionary<string, Guest>();
    class Guest {
        public string id = Token(), key = Token(), name, state = "pending", offer = "", answer = "";
        public DateTime seen = DateTime.UtcNow;
    }
        static object[] Ice() {
        var servers=new List<object>();
        servers.Add(new {urls="stun:stun.l.google.com:19302"});
        string relay=Environment.GetEnvironmentVariable("LIVEZ_TURN_URL");
        if(relay!=null && (relay.StartsWith("turn:") || relay.StartsWith("turns:")))
            servers.Add(new {urls=relay,username=Environment.GetEnvironmentVariable("LIVEZ_TURN_USER")??"",credential=Environment.GetEnvironmentVariable("LIVEZ_TURN_PASSWORD")??""});
        return servers.ToArray();
    }
    static string Token() { return Guid.NewGuid().ToString("N"); }
    static readonly string owner = Token(), invite = Token();
    static string address, publicUrl = "", tunnelStatus = "", html;
    static bool opening, closing;
    static Process tunnel;
    static TcpListener listener;
    static Dictionary<string,object> Json(string s) { return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(s); }
    static string Field(Dictionary<string,object> d,string k) { object v; return d.TryGetValue(k,out v) ? v as string ?? "" : ""; }
    static readonly Guest hostSeat=new Guest{id="host",name="Anfitrião",state="accepted"};
    static Guest presenter;
    static string stage="";
    static IEnumerable<Guest> Seats(){return guests.Values.Concat(new[]{hostSeat});}
    static void EndStage(){
        presenter=null;stage="";
        foreach(var seat in Seats()){seat.offer="";seat.answer="";}
    }
    static object Snapshot(Guest me){
        var receivers=Seats().Where(v=>v!=presenter && v.state=="accepted")
            .Select(v=>new {v.id,v.answer,answeredOffer=v.answer==""?"":v.offer});
        return new {me.state,me.offer,stage,mine=me==presenter,speaker=presenter==null?"":presenter.name,
            ice=me.state=="accepted"?Ice():null,receivers=me==presenter?receivers.ToArray():null};
    }
    static object Api(Dictionary<string,object> d) {
        var cmd=Field(d,"cmd");var key=Field(d,"key");
        lock(gate){
            if(presenter!=null && (DateTime.UtcNow-presenter.seen).TotalSeconds>90)EndStage();
            foreach(var id in guests.Where(x=>(DateTime.UtcNow-x.Value.seen).TotalSeconds>90).Select(x=>x.Key).ToArray())guests.Remove(id);
            if(cmd=="join" && key==invite){
                if(guests.Count>=12)throw new Exception("Sala cheia. Tente novamente em instantes.");
                var name=Field(d,"name").Trim();
                if(name.Length<1||name.Length>40)throw new Exception("Informe um nome de até 40 caracteres.");
                var g=new Guest{name=name};guests.Add(g.id,g);return new {key=g.key};
            }
            var me=key==owner?hostSeat:guests.Values.FirstOrDefault(x=>x.key==key);
            if(me==null)throw new Exception("Acesso encerrado ou convite inválido.");
            me.seen=DateTime.UtcNow;
            if(me==hostSeat){
                if(cmd=="list")return new {room=Snapshot(me),url=publicUrl,status=tunnelStatus,invite=invite,
                    guests=guests.Values.Select(v=>new {v.id,v.name,v.state}).ToArray()};
                if(cmd=="open"){
                    if(!opening && publicUrl==""){opening=true;tunnelStatus="Preparando convite…";ThreadPool.QueueUserWorkItem(_=>OpenTunnel());}
                    return new {};
                }
                if(cmd=="accept" || cmd=="remove"){
                    Guest target;if(!guests.TryGetValue(Field(d,"id"),out target))throw new Exception("Este visitante saiu.");
                    if(cmd=="remove"){if(presenter==target)EndStage();guests.Remove(target.id);}
                    else{if(guests.Values.Count(v=>v.state=="accepted")>=3)throw new Exception("A sala aceita até três convidados.");target.state="accepted";}
                    return new {};
                }
            }
            if(cmd=="poll")return Snapshot(me);
            if(cmd=="leave" && me!=hostSeat){if(presenter==me)EndStage();guests.Remove(me.id);return new {};}
            if(me.state!="accepted")throw new Exception("Aguarde a aprovação.");
            if(cmd=="claim"){
                if(presenter!=null)throw new Exception("A sala já tem uma apresentação em andamento.");
                presenter=me;stage=Token();return new {stage};
            }
            if(stage=="" || Field(d,"stage")!=stage)throw new Exception("A apresentação mudou.");
            if(cmd=="stop" && me==presenter){EndStage();return new {};}
            if(cmd=="offer" && me==presenter){
                var target=Seats().FirstOrDefault(v=>v.id==Field(d,"id") && v!=me && v.state=="accepted");
                if(target==null)throw new Exception("O participante saiu.");
                target.offer=Field(d,"sdp");target.answer="";return new {};
            }
            if(cmd=="answer" && me!=presenter && me.offer!="" && me.offer==Field(d,"offer")){
                me.answer=Field(d,"sdp");return new {};
            }
            throw new Exception("Pedido inválido.");
        }
    }
    static void OpenTunnel() {
        try {
            string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LiveZ");
            Directory.CreateDirectory(dir);
            string exe=Path.Combine(dir,"cloudflared-amd64.exe");
            if(!Environment.Is64BitOperatingSystem) throw new Exception("O convite pela internet requer Windows de 64 bits.");
            if(!File.Exists(exe)) {
                ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
                using(var wc=new WebClient()) {
                    wc.Headers["User-Agent"]="LiveZ"; var release=Json(wc.DownloadString("https://api.github.com/repos/cloudflare/cloudflared/releases/latest"));
                    var asset=((System.Collections.IEnumerable)release["assets"]).Cast<Dictionary<string,object>>().First(x=>Field(x,"name")=="cloudflared-windows-amd64.exe");
                    var tmp=exe+"."+Token()+".tmp";
                    try {
                        wc.DownloadFile(Field(asset,"browser_download_url"),tmp);
                        using(var sha=SHA256.Create()) using(var f=File.OpenRead(tmp)) {
                            var digest="sha256:"+BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();
                            if(digest!=Field(asset,"digest")) throw new Exception("Falha de integridade no download.");
                        }
                        if(!File.Exists(exe)) File.Move(tmp,exe);
                    } finally {if(File.Exists(tmp)) File.Delete(tmp);}
                }
            }
            var p=new Process(); p.StartInfo=new ProcessStartInfo(exe,"tunnel --no-autoupdate --url "+address){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
            p.ErrorDataReceived+=(s,e)=>{if(e.Data==null)return; var m=System.Text.RegularExpressions.Regex.Match(e.Data,@"https://[a-z0-9-]+\.trycloudflare\.com\b"); if(m.Success) lock(gate){publicUrl=m.Value;tunnelStatus="Convite disponível";}};
            lock(gate){if(closing)return; p.Start();tunnel=p;} p.BeginErrorReadLine();
            for(int i=0;i<90 && !p.WaitForExit(1000);i++){lock(gate) if(publicUrl!="") break;}
            lock(gate){if(publicUrl=="" && !p.HasExited)p.Kill();}
            p.WaitForExit(); throw new Exception("O túnel foi encerrado. Tente gerar outro convite.");
        } catch(Exception e){lock(gate){publicUrl="";tunnelStatus=e.Message;}}
        finally{lock(gate){opening=false;}}
    }
    static void Serve(TcpClient client) {
        using(client) try {
            client.ReceiveTimeout=5000;client.SendTimeout=5000;
            var stream=client.GetStream(); var buf=new MemoryStream(); int b;
            while(buf.Length<8192 && (b=stream.ReadByte())>=0){buf.WriteByte((byte)b);var a=buf.GetBuffer();int n=(int)buf.Length;if(n>=4 && a[n-4]==13 && a[n-3]==10 && a[n-2]==13 && a[n-1]==10)break;}
            var lines=Encoding.ASCII.GetString(buf.ToArray()).Split(new[]{"\r\n"},StringSplitOptions.None);
            var path=lines[0].Split(' '); string result,type="text/plain",status="200 OK";
            if(path[0]=="POST" && (path[1]=="/audio/desktop" || path[1].StartsWith("/audio/window/")) && lines.Contains("Authorization: Bearer "+owner,StringComparer.OrdinalIgnoreCase)){
                AppAudio.Send(stream,path[1].Substring(7));return;
            }
            if(path[0]=="GET" && path[1]=="/favicon.ico"){
                using(var icon=typeof(LiveZ).Assembly.GetManifestResourceStream("icon")){
                    var head=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: image/x-icon\r\nContent-Length: "+icon.Length+"\r\nConnection: close\r\n\r\n");
                    stream.Write(head,0,head.Length);icon.CopyTo(stream);
                }
                return;
            }
            if(path[0]=="GET" && path[1]=="/"){result=html;type="text/html";}
            else if(path[0]=="POST" && path[1]=="/api" && lines.Any(x=>x.StartsWith("Content-Type: application/json",StringComparison.OrdinalIgnoreCase))){
                int size;var len=lines.FirstOrDefault(x=>x.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase));
                if(len==null || !int.TryParse(len.Substring(15),out size)||size<1||size>160000)return;
                var body=new byte[size]; for(int i=0;i<size;){int read=stream.Read(body,i,size-i);if(read==0)return;i+=read;}
                type="application/json";var js=new JavaScriptSerializer();
                try{result=js.Serialize(Api(Json(Encoding.UTF8.GetString(body))));}
                catch(Exception e){status="400 Bad Request";result=js.Serialize(new {error=e.Message});}
            } else {status="404 Not Found";result="";}
            var data=Encoding.UTF8.GetBytes(result);
            var header=Encoding.ASCII.GetBytes("HTTP/1.1 "+status+"\r\nContent-Type: "+type+"; charset=utf-8\r\nContent-Length: "+data.Length+"\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nX-Frame-Options: DENY\r\nConnection: close\r\n\r\n");
            stream.Write(header,0,header.Length);stream.Write(data,0,data.Length);
        }catch(IOException){}catch(SocketException){}catch(FormatException){}catch(IndexOutOfRangeException){}
    }
    [STAThread] static void Main(string[] args) {
        if(args.Contains("--audio-anchor")){Console.ReadLine();return;}
        Application.EnableVisualStyles();
        using(var r=new StreamReader(typeof(LiveZ).Assembly.GetManifestResourceStream("ui"))) html=r.ReadToEnd();
        listener=new TcpListener(IPAddress.Loopback,0);listener.Start();address="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;
        var thread=new Thread(()=>{try{while(true){var c=listener.AcceptTcpClient();ThreadPool.QueueUserWorkItem(_=>Serve(c));}}catch(SocketException){}});
        thread.IsBackground=true;thread.Start();
        var url=address+"/#host="+owner;
        if(args.Contains("--smoke")){Console.WriteLine(url);Console.ReadLine();return;}
        var ink=System.Drawing.Color.FromArgb(19,26,34);
        var mint=System.Drawing.Color.FromArgb(146,245,198);
        var form=new Form {Icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath),Text="LiveZ · Sala de transmissão",ClientSize=new System.Drawing.Size(440,208),BackColor=ink,ForeColor=System.Drawing.Color.White,StartPosition=FormStartPosition.CenterScreen,MaximizeBox=false,FormBorderStyle=FormBorderStyle.FixedDialog};
        var title=new Label {Text="LiveZ",Left=24,Top=16,Width=370,Height=49,ForeColor=mint,Font=new System.Drawing.Font("Segoe UI",26,System.Drawing.FontStyle.Bold)};
        var info=new Label {Text="Sua sala está aberta. Use o estúdio para escolher a tela, ajustar o som e convidar amigos.",Left=26,Top=78,Width=385,Height=55,Font=new System.Drawing.Font("Segoe UI",10)};
        var open=new Button {Text="Abrir estúdio",Left=26,Top=149,Width=185,Height=38,FlatStyle=FlatStyle.Flat,BackColor=mint,ForeColor=ink};
        var end=new Button {Text="Encerrar sala",Left=225,Top=149,Width=185,Height=38,FlatStyle=FlatStyle.Flat,ForeColor=System.Drawing.Color.White};
        open.FlatAppearance.BorderSize=0;end.FlatAppearance.BorderColor=System.Drawing.Color.FromArgb(76,92,110);form.Controls.Add(title);
        open.Click+=(s,e)=>{try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(ex.Message);}};
        end.Click+=(s,e)=>form.Close();form.Controls.AddRange(new Control[]{info,open,end});form.Shown+=(s,e)=>open.PerformClick();
        Application.Run(form);lock(gate){closing=true;if(tunnel!=null && !tunnel.HasExited)tunnel.Kill();}listener.Stop();
    }
}







