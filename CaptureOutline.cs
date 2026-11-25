using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class CaptureOutline : Form {
    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out Rect rect,int size);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint command);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    public CaptureOutline(){
        FormBorderStyle=FormBorderStyle.None;
        ShowInTaskbar=false;
        BackColor=Color.FromArgb(173,104,255);
        AutoScaleMode=AutoScaleMode.None;
    }
    protected override bool ShowWithoutActivation { get{return true;} }
    protected override CreateParams CreateParams {
        get {var p=base.CreateParams;p.ExStyle|=0x080800A0;return p;} // No activation, layered, tool window, click-through.
    }
    protected override void OnSizeChanged(EventArgs e){
        base.OnSizeChanged(e);
        var ring=new Region(ClientRectangle);
        if(ClientSize.Width>6 && ClientSize.Height>6)
            ring.Exclude(Rectangle.Inflate(ClientRectangle,-3,-3));
        var old=Region;Region=ring;if(old!=null)old.Dispose();
    }
    public void Follow(IntPtr target){
        Rect bounds;
        if(target==IntPtr.Zero || !IsWindowVisible(target) || IsIconic(target) ||
           DwmGetWindowAttribute(target,9,out bounds,16)!=0){if(Visible)Hide();return;}
        IntPtr previous=IntPtr.Zero;
        try{
            try{previous=SetThreadDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}
            var above=GetWindow(target,3); // Previous window in desktop stacking order.
            uint flags=0x10; // Keep keyboard focus on the current application.
            if(above==Handle)flags|=4;
            SetWindowPos(Handle,above,bounds.Left,bounds.Top,bounds.Right-bounds.Left,bounds.Bottom-bounds.Top,flags);
            if(!Visible)Show();
        }finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
    }
}
