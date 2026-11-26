using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class Studio : Form {
    class Source {internal string Text,Filter,Audio;internal IntPtr Handle;public override string ToString(){return Text;}}
    [DllImport("user32.dll")]static extern IntPtr MonitorFromPoint(Point pt,uint flags);
    [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
    readonly string owner=Guid.NewGuid().ToString("N"),invite=Guid.NewGuid().ToString("N");
    readonly ComboBox source=new ComboBox(),quality=new ComboBox();
    readonly TextBox link=new TextBox(),friend=new TextBox();
    readonly Button start=new Button(),end=new Button(),join=new Button(),publish=new Button(),accept=new Button(),deny=new Button();
    readonly Label status=new Label(),metrics=new Label();
    readonly ListBox pending=new ListBox();readonly CheckBox preview=new CheckBox();readonly TrackBar gain=new TrackBar();
    readonly CaptureOutline outline=new CaptureOutline();
    readonly System.Windows.Forms.Timer poll=new System.Windows.Forms.Timer{Interval=850};
    Process server,tunnel;Connection local,active;Task<string> encoder;
    string stage="",watching="",remoteUrl="";Broadcast broadcast;Playback playback;
    bool polling,busy,closing,opening;DateTime keepMessage;
    void Ui(Action action){if(!closing && IsHandleCreated)try{BeginInvoke(action);}catch(InvalidOperationException){}}
    void Info(string message){status.Text=message;keepMessage=DateTime.UtcNow.AddSeconds(6);}
    void ButtonAt(Button b,string text,int x,int y,int w,EventHandler click){
        b.Text=text;b.SetBounds(x,y,w,34);b.FlatStyle=FlatStyle.Flat;b.Click+=click;Controls.Add(b);
    }
    void LabelAt(string text,int x,int y,int w){var l=new Label{Text=text,AutoSize=false};l.SetBounds(x,y,w,23);Controls.Add(l);}
    void Place(Control c,int x,int y,int w,int h){c.SetBounds(x,y,w,h);Controls.Add(c);}
    Studio(){
        Text="LiveZ · Estúdio nativo";ClientSize=new Size(760,515);MinimumSize=Size;MaximumSize=Size;StartPosition=FormStartPosition.CenterScreen;
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(20,25,34);ForeColor=Color.White;
        var title=new Label{Text="LiveZ",Font=new Font("Segoe UI",27,FontStyle.Bold),ForeColor=Color.FromArgb(152,240,201)};Place(title,22,12,400,55);
        LabelAt("Tela ou janela",24,78,400);Place(source,24,101,465,28);source.DropDownStyle=ComboBoxStyle.DropDownList;source.DropDown+=(s,e)=>Sources();
        quality.Items.AddRange(new object[]{"720p · 30 FPS","1080p · 60 FPS"});quality.SelectedIndex=0;quality.DropDownStyle=ComboBoxStyle.DropDownList;Place(quality,508,101,221,28);
        ButtonAt(start,"Transmitir",24,146,145,async(s,e)=>await StartCast());ButtonAt(end,"Parar",180,146,100,async(s,e)=>await StopCast());end.Enabled=false;
        preview.Text="Ver prévia";Place(preview,305,147,115,32);
        LabelAt("Volume enviado",450,142,145);gain.Minimum=0;gain.Maximum=150;gain.Value=100;gain.TickStyle=TickStyle.None;Place(gain,445,165,285,26);gain.ValueChanged+=(s,e)=>{if(broadcast!=null)broadcast.Audio.Gain=gain.Value;};
        LabelAt("Convite da sua sala",24,204,350);link.ReadOnly=true;Place(link,24,229,490,28);
        ButtonAt(publish,"Gerar convite",527,224,202,async(s,e)=>await Publish());
        LabelAt("Entrar na sala de um amigo",24,275,390);Place(friend,24,298,490,28);ButtonAt(join,"Entrar",527,294,202,async(s,e)=>await Join());
        LabelAt("Pedidos de entrada",24,340,400);Place(pending,24,365,490,58);
        ButtonAt(accept,"Aceitar",527,364,96,async(s,e)=>await Decide(true));ButtonAt(deny,"Recusar",630,364,99,async(s,e)=>await Decide(false));
        Place(status,24,435,706,45);Place(metrics,24,482,706,25);metrics.ForeColor=Color.FromArgb(152,240,201);
        Shown+=async(s,e)=>{try{
            var i=Tools.Info("LiveZ.Room.exe","");i.RedirectStandardInput=true;i.RedirectStandardOutput=true;i.EnvironmentVariables["LIVEZ_OWNER"]=owner;i.EnvironmentVariables["LIVEZ_INVITE"]=invite;
            server=Process.Start(i);int port=int.Parse(await server.StandardOutput.ReadLineAsync());local=active=new Connection("http://127.0.0.1:"+port,owner);Sources();
            encoder=Task.Run(()=>Broadcast.Encoder());Info("Sala pronta. Escolha a fonte e clique em Transmitir.");poll.Start();
        }catch(Exception ex){Info(ex.Message);}};
        poll.Tick+=async(s,e)=>await TickRoom();
        FormClosed+=(s,e)=>{closing=true;poll.Stop();if(broadcast!=null)broadcast.Dispose();if(playback!=null)playback.Dispose();outline.Dispose();Tools.Close(tunnel);Tools.Close(server);};
    }
    void Sources(){
        string previous=source.SelectedItem==null?"":source.SelectedItem.ToString();source.Items.Clear();
        foreach(var screen in Screen.AllScreens){
            var m=MonitorFromPoint(new Point(screen.Bounds.Left+1,screen.Bounds.Top+1),2);
            source.Items.Add(new Source{Text="Monitor · "+screen.DeviceName,Filter="gfxcapture=hmonitor="+m.ToInt64(),Audio="desktop"});
        }
        foreach(var p in Process.GetProcesses())using(p)try{
            if(p.MainWindowHandle==IntPtr.Zero||string.IsNullOrEmpty(p.MainWindowTitle)||p.MainWindowHandle==Handle)continue;
            source.Items.Add(new Source{Text=p.MainWindowTitle,Handle=p.MainWindowHandle,Filter="gfxcapture=hwnd="+p.MainWindowHandle.ToInt64(),Audio="window/window:"+p.MainWindowHandle.ToInt64()+":0"});
        }catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
        var index=source.FindStringExact(previous);if(source.Items.Count>0)source.SelectedIndex=Math.Max(0,index);
    }
    async Task StartCast(){
        if(active==null||busy||stage!="")return;var choice=source.SelectedItem as Source;if(choice==null)return;
        var conn=active;busy=true;start.Enabled=false;string failure=null;
        try{
            var codec=await encoder;stage=await Task.Run(()=>conn.Text("/claim",""));string id=stage;
            var high=quality.SelectedIndex==1;
            broadcast=new Broadcast(conn,id,choice.Filter,choice.Audio,high?1080:720,high?60:30,codec,
              msg=>Ui(async()=>{if(stage==id){await StopCast();Info(msg);}}),msg=>Ui(()=>{if(stage==id)metrics.Text=msg;}));
            broadcast.Audio.Gain=gain.Value;end.Enabled=true;source.Enabled=quality.Enabled=false;
            outline.Follow(choice.Handle);Info("Transmitindo · "+choice.Text);
        }catch(Exception e){failure=e.Message;}finally{busy=false;}
        if(failure!=null){await StopCast();Info(failure);}
    }
    async Task StopCast(){
        if(broadcast!=null){broadcast.Dispose();broadcast=null;}string id=stage;stage="";outline.Follow(IntPtr.Zero);
        if(playback!=null){playback.Dispose();playback=null;}watching="";source.Enabled=quality.Enabled=true;end.Enabled=false;metrics.Text="";
        if(id!="")try{var c=active;await Task.Run(()=>c.Text("/stop/"+id,""));}catch{}
        Info("Transmissão encerrada.");
    }
    async Task TickRoom(){
        if(polling||busy||active==null)return;polling=true;var c=active;
        try{var state=await Task.Run(()=>c.Text("/state"));if(c!=active||busy||closing)return;
            if(c==local){var list=await Task.Run(()=>local.Text("/pending"));var rows=list.Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries);
                if(!pending.Items.Cast<string>().SequenceEqual(rows)){pending.Items.Clear();pending.Items.AddRange(rows);if(rows.Length>0)pending.SelectedIndex=0;}}
            if(c!=active||busy||closing)return;
            start.Enabled=state=="idle";publish.Enabled=c==local&&!opening;
            if(stage!="" && state!=stage){await StopCast();return;}
            if(stage!=""){var choice=source.SelectedItem as Source;outline.Follow(choice.Handle);}
            var desired=state=="idle"||state=="pending"||(stage==state&&!preview.Checked)?"":state;
            if(desired!=watching){if(playback!=null){playback.Dispose();playback=null;}watching=desired;
                if(desired!="")playback=new Playback(c,desired,stage==desired,msg=>Ui(()=>{if(watching==desired){Info(msg);watching="";}}));}
            if(stage=="" && DateTime.UtcNow>=keepMessage)status.Text=state=="pending"?"Aguardando aprovação.":state=="idle"?"Sala livre. Você pode transmitir.":"Assistindo · 9 e 0 ajustam o volume no player.";
        }catch(Exception e){Info(e.Message);}finally{polling=false;}
    }
    async Task Decide(bool allow){
        var row=pending.SelectedItem as string;if(row==null)return;
        try{await Task.Run(()=>local.Text((allow?"/accept/":"/deny/")+row.Split(' ')[0],""));}catch(Exception e){Info(e.Message);}
    }
    async Task Join(){
        if(active==null||busy||stage!="")return;
        if(active!=local){active=local;join.Text="Entrar";watching="";if(playback!=null){playback.Dispose();playback=null;}return;}
        Uri uri;
        if(!Uri.TryCreate(friend.Text.Trim(),UriKind.Absolute,out uri)||uri.Scheme!="https"||!uri.Host.EndsWith(".trycloudflare.com")||!Regex.IsMatch(uri.Fragment,"^#n=[a-f0-9]{32}$")){Info("Cole um convite da edição nativa do LiveZ.");return;}
        busy=true;
        try{var serverUrl=uri.GetLeftPart(UriPartial.Authority);var c=new Connection(serverUrl,uri.Fragment.Substring(3));
            string key=await Task.Run(()=>c.Text("/join",Environment.UserName));active=new Connection(serverUrl,key);join.Text="Voltar à minha sala";
            if(playback!=null){playback.Dispose();playback=null;}watching="";Info("Aguardando aprovação.");
        }catch(Exception e){Info(e.Message);}finally{busy=false;}
    }
    async Task Publish(){
        if(remoteUrl!=""){Clipboard.SetText(link.Text);Info("Convite copiado.");return;}
        if(local==null||opening)return;opening=true;publish.Enabled=false;Info("Preparando o convite…");
        try{string exe=await Task.Run(()=>Tunnel.Binary());if(closing)return;
            var info=new ProcessStartInfo(exe,"tunnel --no-autoupdate --url "+local.Address){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
            var p=new Process{StartInfo=info,EnableRaisingEvents=true};tunnel=p;
            p.ErrorDataReceived+=(s,e)=>{if(e.Data==null)return;var match=Regex.Match(e.Data,@"https://[a-z0-9-]+\.trycloudflare\.com\b");if(match.Success)Ui(()=>{remoteUrl=match.Value;link.Text=remoteUrl+"/#n="+invite;publish.Text="Copiar convite";opening=false;});};
            p.Exited+=(s,e)=>Ui(()=>{remoteUrl="";link.Clear();publish.Text="Gerar convite";opening=false;Info("O túnel de convite fechou.");});p.Start();p.BeginErrorReadLine();
            await Task.Delay(75000);if(!closing && tunnel==p && remoteUrl=="")Tools.Close(p);
        }catch(Exception e){opening=false;Info(e.Message);}
    }
    [STAThread]static void Main(string[] args){if(args.Contains("--audio-anchor")){Console.ReadLine();return;}SetProcessDPIAware();Application.EnableVisualStyles();Application.Run(new Studio());}
}
