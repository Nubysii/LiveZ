using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// WASAPI process loopback in desktop exclusion or single-window inclusion mode.
public static class AppAudio {
    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOperation {
        [PreserveSig] int GetActivateResult(out int hr, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }
    [ComVisible(true), Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICompletion { [PreserveSig] int ActivateCompleted(IOperation operation); }
    [ComVisible(true), Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAgile {}
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public class Completion : ICompletion, IAgile {
        internal readonly ManualResetEvent ready=new ManualResetEvent(false);
        internal int status; internal object instance;
        public int ActivateCompleted(IOperation operation) {
            try { int code=operation.GetActivateResult(out status,out instance); if(code<0)status=code; }
            catch(Exception e){status=Marshal.GetHRForException(e);} finally{ready.Set();}return 0;
        }
    }
    [StructLayout(LayoutKind.Sequential)] struct Blob {public int size; public IntPtr pointer;}
    [StructLayout(LayoutKind.Explicit)] struct Variant {
        [FieldOffset(0)] public ushort type;
        [FieldOffset(8)] public Blob blob;
    }
    [StructLayout(LayoutKind.Sequential, Pack=2)] struct Wave {
        public ushort tag, channels; public uint rate, bytes; public ushort align,bits,extra;
    }
    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient {
        void Initialize(int mode,uint flags,long duration,long period,ref Wave format,IntPtr session);
        void GetBufferSize(out uint frames);void GetStreamLatency(out long latency);void GetCurrentPadding(out uint frames);
        void IsFormatSupported();void GetMixFormat();void GetDevicePeriod();void Start();void Stop();void Reset();
        void SetEventHandle(IntPtr handle);
        void GetService(ref Guid id,[MarshalAs(UnmanagedType.Interface)] out ICapture capture);
    }
    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICapture {
        void GetBuffer(out IntPtr data,out uint frames,out uint flags,IntPtr device,IntPtr counter);
        void ReleaseBuffer(uint frames);void GetNextPacketSize(out uint frames);
    }
    [DllImport("Mmdevapi.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    static extern int ActivateAudioInterfaceAsync(string device,ref Guid id,ref Variant args,ICompletion callback,out IOperation operation);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct ProcessEntry {
        public uint size,usage,pid;public UIntPtr heap;public uint module,threads,parent;
        public int priority;public uint flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string exe;
    }
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool Process32FirstW(IntPtr snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool Process32NextW(IntPtr snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out int pid);
    static bool IsDiscord(string exe){
        return new[]{"Discord","DiscordPTB","DiscordCanary","DiscordDevelopment"}.Contains(Path.GetFileNameWithoutExtension(exe),StringComparer.OrdinalIgnoreCase);
    }
    // Zero means that the desktop client is closed. Independent installations cannot
    // share one WASAPI exclusion target, so keep audio paused until there is one root.
    static int DiscordRoot(){
        var snapshot=CreateToolhelp32Snapshot(2,0);
        if(snapshot==new IntPtr(-1))throw new IOException();
        try{
            var entry=new ProcessEntry{size=(uint)Marshal.SizeOf(typeof(ProcessEntry))};
            if(!Process32FirstW(snapshot,ref entry))throw new IOException();
            var parents=new Dictionary<uint,uint>();var discord=new HashSet<uint>();
            do{parents[entry.pid]=entry.parent;if(IsDiscord(entry.exe))discord.Add(entry.pid);}while(Process32NextW(snapshot,ref entry));
            if(Marshal.GetLastWin32Error()!=18)throw new IOException();
            var roots=new List<uint>();
            foreach(uint id in discord){
                uint parent=id;var visited=new HashSet<uint>{id};bool child=false;
                while(parents.TryGetValue(parent,out parent)&&visited.Add(parent)){
                    if(discord.Contains(parent)){child=true;break;}
                }
                if(!child)roots.Add(id);
            }
            return roots.Count==0?0:roots.Count==1?(int)roots[0]:-1;
        }finally{CloseHandle(snapshot);}
    }
    static Process WindowProcess(string label){
        var parts=label.Split(':');long hwnd;int pid;
        if(parts.Length>=2 && parts[0]=="window" && long.TryParse(parts[1],out hwnd) &&
           GetWindowThreadProcessId(new IntPtr(hwnd),out pid)!=0)return Process.GetProcessById(pid);
        int match=0;
        foreach(var p in Process.GetProcesses())using(p)try{
            if(label.Length>0 && p.MainWindowHandle!=IntPtr.Zero && p.MainWindowTitle==label){
                if(match!=0)throw new IOException();match=p.Id;
            }
        }catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
        return Process.GetProcessById(match);
    }
    static Process IdleAnchor(){
        using(var self=Process.GetCurrentProcess()){
            return Process.Start(new ProcessStartInfo(self.MainModule.FileName,"--audio-anchor"){
                UseShellExecute=false,RedirectStandardInput=true,CreateNoWindow=true});
        }
    }
    public static void Send(Stream output,string captureMode) {
        bool headers=false;Process anchor=null;
        try {
            bool desktop=captureMode=="desktop";
            int root=desktop?DiscordRoot():0;
            if(root<0){Write(output,"HTTP/1.1 423 Locked\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");return;}
            if(desktop && root==0)anchor=IdleAnchor();
            using(var process=desktop?Process.GetProcessById(root==0?anchor.Id:root):WindowProcess(Uri.UnescapeDataString(captureMode.Substring(7)))){
                if(!desktop && IsDiscord(process.ProcessName))throw new IOException();
                int pid=process.Id;
                var callback=new Completion(); var id=typeof(IAudioClient).GUID;
                var memory=Marshal.AllocCoTaskMem(12);
                IOperation operation=null;
                try {
                    Marshal.Copy(new int[]{1,pid,desktop?1:0},0,memory,3);
                    var variant=new Variant{type=65,blob=new Blob{size=12,pointer=memory}};
                    Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync(@"VAD\Process_Loopback",ref id,ref variant,callback,out operation));
                    if(!callback.ready.WaitOne(5000))throw new TimeoutException();
                    Marshal.ThrowExceptionForHR(callback.status);
                }finally{Marshal.FreeCoTaskMem(memory);if(operation!=null)Marshal.ReleaseComObject(operation);}
                var client=(IAudioClient)callback.instance;ICapture capture=null;
                try {
                    var format=new Wave{tag=1,channels=2,rate=48000,bytes=192000,align=4,bits=16};
                    client.Initialize(0,0x80020000,200000,0,ref format,IntPtr.Zero);
                    var service=typeof(ICapture).GUID;client.GetService(ref service,out capture);client.Start();
                    Write(output,"HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nX-LiveZ-Filter: "+(desktop?(root==0?"waiting":"discord"):"window")+"\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");headers=true;
                    byte[] buffer=new byte[8192],quiet=new byte[4];var idle=Stopwatch.StartNew();
                    var guard=Stopwatch.StartNew();var pending=new MemoryStream();
                    while(!process.HasExited){
                        if(desktop && guard.ElapsedMilliseconds>=180){
                            if(DiscordRoot()!=root)break;
                            if(pending.Length>0){output.Write(pending.GetBuffer(),0,(int)pending.Length);pending.SetLength(0);}
                            guard.Restart();
                        }
                        uint frames;capture.GetNextPacketSize(out frames);
                        if(frames==0){if(idle.ElapsedMilliseconds>200){output.Write(quiet,0,4);idle.Restart();}Thread.Sleep(10);continue;}
                        IntPtr data;uint flags;capture.GetBuffer(out data,out frames,out flags,IntPtr.Zero,IntPtr.Zero);
                        int size=checked((int)frames*4);
                        try{if(buffer.Length<size)buffer=new byte[size];if((flags&2)!=0)Array.Clear(buffer,0,size);else Marshal.Copy(data,buffer,0,size);}
                        finally{capture.ReleaseBuffer(frames);}
                        if(desktop)pending.Write(buffer,0,size);else output.Write(buffer,0,size);idle.Restart();
                    }
                }finally{if(capture!=null)Marshal.ReleaseComObject(capture);Marshal.ReleaseComObject(client);callback.ready.Dispose();}
            }
        }catch(Exception){if(!headers)Write(output,"HTTP/1.1 409 Conflict\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");}
        finally{if(anchor!=null){anchor.StandardInput.Close();anchor.Dispose();}}
    }
    static void Write(Stream stream,string message){var bytes=Encoding.ASCII.GetBytes(message);stream.Write(bytes,0,bytes.Length);}
}

