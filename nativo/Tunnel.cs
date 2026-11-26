using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
internal static class Tunnel {
    static string Token(){return Guid.NewGuid().ToString("N");}
    static Dictionary<string,object> Json(string data){return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(data);}
    static string Field(Dictionary<string,object> d,string k){object v;return d.TryGetValue(k,out v)?v as string??"":"";}
    internal static string Binary(){
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
return exe;}}
