using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Forms;
[assembly:AssemblyTitle("III85III VPN Setup")]
[assembly:AssemblyCompany("III85III")]
[assembly:AssemblyProduct("III85III VPN")]
[assembly:AssemblyVersion("2.2.0.0")]
internal static class Installer {
    [STAThread]static int Main(){
        Application.EnableVisualStyles();
        if(Process.GetProcessesByName("III85III VPN").Length>0){MessageBox.Show("Close III85III VPN from the notification tray before installing this update, then run Setup again. Your accounts will be kept.","III85III VPN Setup",MessageBoxButtons.OK,MessageBoxIcon.Information);return 1;}
        if(MessageBox.Show("Install III85III VPN for this Windows user?\n\nEnglish is the default language. Your account data is kept separately.\n\nThe package includes the required components and licenses.","III85III VPN Setup",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK)return 0;
        try{
            string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","III85III VPN");
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload"))InstallPackage.Install(stream,root,delegate(string stage){
                var security=new DirectorySecurity();var sid=WindowsIdentity.GetCurrent().User;security.SetAccessRuleProtection(true,false);security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));new DirectoryInfo(stage).SetAccessControl(security);
            });
            string exe=Path.Combine(root,"III85III VPN.exe");if(!File.Exists(exe))throw new IOException("Incomplete package.");
            CreateShortcut(exe,root);
            if(MessageBox.Show("Installation complete. Open the preview now?\n\nLocation: "+root,"III85III VPN Setup",MessageBoxButtons.YesNo,MessageBoxIcon.Information)==DialogResult.Yes)Process.Start(new ProcessStartInfo(exe){UseShellExecute=true,WorkingDirectory=root});
            return 0;
        }catch(Exception){MessageBox.Show("Installation could not finish. Close any running copy of this preview and try again. Your saved accounts were not removed.","III85III VPN Setup",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
    }
    static void CreateShortcut(string exe,string root){
        object shell=null,shortcut=null;
        try{
            Type shellType=Type.GetTypeFromProgID("WScript.Shell");shell=Activator.CreateInstance(shellType);
            string link=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"III85III VPN.lnk");
            shortcut=shellType.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{link});
            Type linkType=shortcut.GetType();
            foreach(var pair in new[]{new[]{"TargetPath",exe},new[]{"WorkingDirectory",root},new[]{"IconLocation",exe+",0"},new[]{"Description","III85III VPN"}})linkType.InvokeMember(pair[0],BindingFlags.SetProperty,null,shortcut,new object[]{pair[1]});
            linkType.InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);
        }finally{if(shortcut!=null)System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);if(shell!=null)System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
    }
}
