using System;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading;
using System.Web.Script.Serialization;
[assembly:System.Reflection.AssemblyCompany("III85III")]
[assembly:System.Reflection.AssemblyProduct("III85III VPN")]
[assembly:System.Reflection.AssemblyTitle("III85III selected-app routing")]
[assembly:System.Reflection.AssemblyVersion("2.0.0.0")]
internal static class RouteHost {
    static string stateFile;
    static void Status(string state,bool up){File.WriteAllText(stateFile,new JavaScriptSerializer().Serialize(new{state=state,interface_up=up}));}
    static int Main(string[] args){
        if(args.Length!=2)return 2;
        Process child=null;
        try{
            string config=Path.GetFullPath(args[0]);string dir=Path.GetDirectoryName(config);
            string allowed=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"III85III VPN Independent");
            if(!dir.Equals(allowed,StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected routing data directory.");
            stateFile=Path.Combine(dir,"route-state.json");string stop=Path.Combine(dir,"route-stop");
            var parent=Process.GetProcessById(Int32.Parse(args[1]));var start=parent.StartTime;
            string root=AppDomain.CurrentDomain.BaseDirectory;string router=Path.Combine(root,"runtime","sing-box.exe");
            string expectedNode=Path.Combine(root,"runtime","node.exe");if(!parent.MainModule.FileName.Equals(expectedNode,StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected parent runtime.");
            Status("starting",false);
            child=Process.Start(new ProcessStartInfo(router,"run -c \""+config+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true});
            child.OutputDataReceived+=delegate{};child.ErrorDataReceived+=delegate{};child.BeginOutputReadLine();child.BeginErrorReadLine();
            bool lastUp=false;
            while(!child.HasExited&&!File.Exists(stop)){
                try{if(parent.HasExited||parent.StartTime!=start)break;}catch{break;}
                bool up=false;foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces())if(adapter.Name.Equals("III85III",StringComparison.OrdinalIgnoreCase)&&adapter.OperationalStatus==OperationalStatus.Up)up=true;
                if(up!=lastUp){Status("running",up);lastUp=up;}Thread.Sleep(300);
            }
            bool failed=child.HasExited&&!File.Exists(stop);
            if(!child.HasExited){child.Kill();child.WaitForExit(5000);}Status(failed?"failed":"stopped",false);return failed?1:0;
        }catch{if(stateFile!=null)try{Status("failed",false);}catch{}return 1;}
        finally{if(child!=null){try{if(!child.HasExited)child.Kill();}catch{}child.Dispose();}}
    }
}
