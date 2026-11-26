using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal sealed class Connection {
    internal readonly string Address,Key;
    internal Connection(string address,string key){Address=address.TrimEnd('/');Key=key;}
    internal byte[] Call(string path,byte[] body=null){
        var req=(HttpWebRequest)WebRequest.Create(Address+path);
        req.Method=body==null?"GET":"POST";req.Headers["Authorization"]="Bearer "+Key;
        req.Proxy=Address.StartsWith("http://127.0.0.1:")?null:WebRequest.DefaultWebProxy;
        req.AllowAutoRedirect=false;req.Timeout=10000;req.ReadWriteTimeout=10000;
        req.ServicePoint.Expect100Continue=false;req.ServicePoint.ConnectionLimit=16;
        if(body!=null){req.ContentLength=body.Length;if(body.Length>0)using(var dest=req.GetRequestStream())dest.Write(body,0,body.Length);}
        try{using(var response=req.GetResponse())using(var src=response.GetResponseStream())using(var ms=new MemoryStream()){src.CopyTo(ms);return ms.ToArray();}}
        catch(WebException e){if(e.Response==null)throw;using(var r=e.Response)using(var reader=new StreamReader(r.GetResponseStream()))throw new IOException(reader.ReadToEnd());}
    }
    internal string Text(string path,string post=null){return Encoding.UTF8.GetString(Call(path,post==null?null:Encoding.UTF8.GetBytes(post)));}
}
internal static class Tools {
    internal static string FileName(string name){return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name);}
    internal static ProcessStartInfo Info(string name,string args){return new ProcessStartInfo(FileName(name),args){UseShellExecute=false,CreateNoWindow=true};}
    internal static void Close(Process p){if(p==null)return;try{if(!p.HasExited)p.Kill();}catch(InvalidOperationException){}}
}
// Limited PCM queue. A silent app supplies zeroes on the same 48 kHz clock.
internal sealed class AudioFeed : Stream {
    readonly byte[] ring=new byte[48000*4/2];int read,count;volatile bool closed;
    internal volatile int Gain=100;
    public override void Write(byte[] b,int off,int len){
        lock(ring){
            if(closed)throw new IOException();
            for(int i=0;i<len;i++){
                if(count==ring.Length){read=(read+1)%ring.Length;count--;}
                ring[(read+count)%ring.Length]=b[off+i];count++;
            }
        }
    }
    internal void Run(Stream output,string mode){
        Task.Run(()=>{while(!closed){AppAudio.Send(this,mode,true);if(!closed)Thread.Sleep(250);}});
        var clock=Stopwatch.StartNew();long due=0;byte[] packet=new byte[3840];
        try{while(!closed){
            Array.Clear(packet,0,packet.Length);
            lock(ring){int n=Math.Min(count,packet.Length)&~3;for(int i=0;i<n;i++){packet[i]=ring[read];read=(read+1)%ring.Length;}count-=n;}
            int gain=Gain;
            if(gain!=100)for(int i=0;i<packet.Length;i+=2){short v=(short)(packet[i]|packet[i+1]<<8);v=(short)Math.Max(-32768,Math.Min(32767,v*gain/100));packet[i]=(byte)v;packet[i+1]=(byte)(v>>8);}
            output.Write(packet,0,packet.Length);due+=20;long delay=due-clock.ElapsedMilliseconds;
            if(delay>0)Thread.Sleep((int)delay);else if(delay < -100)due=clock.ElapsedMilliseconds;
        }}catch(IOException){}catch(ObjectDisposedException){}finally{closed=true;}
    }
    internal void End(){closed=true;}
    public override bool CanRead{get{return false;}}public override bool CanSeek{get{return false;}}public override bool CanWrite{get{return true;}}
    public override void Flush(){}public override long Length{get{throw new NotSupportedException();}}public override long Position{get{throw new NotSupportedException();}set{throw new NotSupportedException();}}
    public override long Seek(long o,SeekOrigin s){throw new NotSupportedException();}public override void SetLength(long l){throw new NotSupportedException();}public override int Read(byte[] b,int o,int n){throw new NotSupportedException();}
}
internal sealed class Broadcast : IDisposable {
    internal readonly AudioFeed Audio=new AudioFeed();
    readonly Connection room;readonly string epoch;readonly Process proc;volatile bool ended;
    internal static string Encoder(){
        foreach(string codec in new[]{"h264_nvenc","h264_qsv","h264_amf"}){
            var info=Tools.Info("ffmpeg.exe","-nostdin -v error -f lavfi -i color=size=320x240 -frames:v 1 -c:v "+codec+" -f null -");
            using(var p=Process.Start(info)){if(!p.WaitForExit(5000)){Tools.Close(p);continue;}if(p.ExitCode==0)return codec;}
        }
        return "libx264";
    }
    internal Broadcast(Connection connection,string stage,string capture,string audioMode,int height,int fps,string codec,Action<string> error,Action<string> stats){
        room=connection;epoch=stage;
        string filter=capture+":max_framerate="+fps+":width="+(height==1080?1920:1280)+":height="+height+":resize_mode=scale_aspect,hwdownload,format=bgra,format=yuv420p";
        string encoder=codec=="libx264"?"-c:v libx264 -preset ultrafast -tune zerolatency":"-c:v "+codec;
        string args="-hide_banner -loglevel warning -stats_period 2 -progress pipe:2 -f s16le -ar 48000 -ac 2 -probesize 8192 -analyzeduration 0 -i pipe:0 -filter_complex \""+filter+"[v]\" -map \"[v]\" -map 0:a "+encoder+" -bf 0 -g "+fps+" -b:v "+(height>=1080?"6500k":"3000k")+" -c:a aac -b:a 128k -flush_packets 1 -muxdelay 0 -muxpreload 0 -mpegts_flags resend_headers -f mpegts pipe:1";
        var info=Tools.Info("ffmpeg.exe",args);info.RedirectStandardInput=true;info.RedirectStandardOutput=true;info.RedirectStandardError=true;
        proc=new Process{StartInfo=info};string lastError="";
        proc.ErrorDataReceived+=(s,e)=>{if(e.Data==null)return;if(e.Data.StartsWith("fps="))stats(codec+" · "+e.Data.Substring(4)+" FPS");else if(!e.Data.Contains("=") || e.Data.StartsWith("[")){lastError=lastError+" "+e.Data;if(lastError.Length>2500)lastError=lastError.Substring(lastError.Length-2500);}};
        proc.Start();proc.BeginErrorReadLine();
        var outgoing=new BlockingCollection<byte[]>(8);
        Task.Run(()=>{
            try{
                foreach(var first in outgoing.GetConsumingEnumerable()){
                    if(ended)break;
                    using(var group=new MemoryStream()){
                        group.Write(first,0,first.Length);
                        byte[] more;
                        while(group.Length<320000 && outgoing.TryTake(out more))group.Write(more,0,more.Length);
                        room.Call("/send/"+epoch,group.ToArray());
                    }
                }
            }catch(Exception e){if(!ended)error("Falha no envio: "+e.Message);}
        });
        Task.Run(()=>Audio.Run(proc.StandardInput.BaseStream,audioMode));
        Task.Run(()=>{try{
            var b=new byte[32768];using(var batch=new MemoryStream()){
                var time=Stopwatch.StartNew();int n;
                while(!ended&&(n=proc.StandardOutput.BaseStream.Read(b,0,b.Length))>0){
                    batch.Write(b,0,n);
                    if(batch.Length<131072 && time.ElapsedMilliseconds<120)continue;
                    if(!outgoing.TryAdd(batch.ToArray(),150))throw new IOException("O upload não acompanhou o vídeo. Tente o perfil 720p.");
                    batch.SetLength(0);time.Restart();
                }
            }
            if(!ended)error("A captura terminou. "+lastError);
        }catch(Exception e){if(!ended)error(e.Message);}finally{outgoing.CompleteAdding();}});
    }
    public void Dispose(){ended=true;Audio.End();Tools.Close(proc);}
}
internal sealed class Playback : IDisposable {
    volatile bool ended;readonly Process process;
    internal Playback(Connection room,string epoch,bool preview,Action<string> error){
        var info=Tools.Info("ffplay.exe","-hide_banner -loglevel error -framedrop -autoexit -x 960 -y 540 -window_title \"LiveZ · "+(preview?"Prévia":"Transmissão")+"\" "+(preview?"-an ":"")+"-probesize 256k -analyzeduration 350000 -f mpegts pipe:0");
        info.RedirectStandardInput=true;process=Process.Start(info);
        Task.Run(()=>{try{long cursor=0;while(!ended&&!process.HasExited){
            var b=room.Call("/read/"+epoch+"/"+cursor);if(b.Length<8)throw new IOException("Resposta de vídeo incompleta.");
            cursor=BitConverter.ToInt64(b,0);if(b.Length>8)process.StandardInput.BaseStream.Write(b,8,b.Length-8);
        }}catch(Exception e){if(!ended)error(e.Message);}finally{Tools.Close(process);}});
    }
    public void Dispose(){ended=true;Tools.Close(process);}
}
