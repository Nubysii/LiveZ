using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Reflection;

class Setup {
    [STAThread] static void Main() {
        Application.EnableVisualStyles();
        if(MessageBox.Show("Instalar o LiveZ para este usuário e criar um atalho no menu Iniciar?", "Instalar LiveZ", MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        try {
            var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LiveZ");
            Directory.CreateDirectory(dir);var exe=Path.Combine(dir,"LiveZ.exe");var temp=Path.Combine(dir,Guid.NewGuid()+".tmp");
            try {
                using(var source=typeof(Setup).Assembly.GetManifestResourceStream("app"))
                using(var dest=File.Create(temp))source.CopyTo(dest);
                if(File.Exists(exe))File.Replace(temp,exe,null);else File.Move(temp,exe);
            } finally {if(File.Exists(temp))File.Delete(temp);}
            var shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            var shortcut=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"LiveZ.lnk")});
            shortcut.GetType().InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,new object[]{exe});
            shortcut.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);
            if(MessageBox.Show("LiveZ instalado. Abrir agora?","LiveZ",MessageBoxButtons.YesNo)==DialogResult.Yes)Process.Start(exe);
        } catch(Exception e){MessageBox.Show(e.Message,"Instalação do LiveZ");}
    }
}
