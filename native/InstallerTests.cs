using System;
using System.IO;
using System.IO.Compression;
using System.Text;

internal static class InstallerTests {
    static int checks;
    static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
    static MemoryStream Package(params string[] names){
        var stream=new MemoryStream();
        using(var archive=new ZipArchive(stream,ZipArchiveMode.Create,true))foreach(string name in names){
            var entry=archive.CreateEntry(name);
            if(!name.EndsWith("/"))using(var writer=new StreamWriter(entry.Open(),Encoding.UTF8))writer.Write("new");
        }
        stream.Position=0;return stream;
    }
    static int Main(){
        string workspace=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"installer-"+Guid.NewGuid().ToString("N"));
        string root=Path.Combine(workspace,"app");
        try{
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root,"III85III VPN.exe"),"old");
            File.WriteAllText(Path.Combine(root,"old.dll"),"old");
            File.WriteAllText(Path.Combine(workspace,"accounts.fixture"),"untouched");
            string[][] invalid={
                new[]{"app/assets/logo.png"},
                new[]{"app/III85III VPN.exe","app/../escape.txt"},
                new[]{"app/III85III VPN.exe","other/file.txt"},
                new[]{"app/III85III VPN.exe","app/iii85iii vpn.EXE"},
                new[]{"app/III85III VPN.exe","app/docs/bad:stream"},
                new[]{"app/III85III VPN.exe","app/docs/trailing. "},
                new[]{"III85III VPN.exe"},
                new[]{"app/III85III VPN.exe","app/docs","app/docs/guide.txt"}
            };
            foreach(var names in invalid){
                bool rejected=false;
                try{using(var payload=Package(names))InstallPackage.Install(payload,root,null);}catch(IOException){rejected=true;}
                Check(rejected,"invalid package rejected");
                Check(File.ReadAllText(Path.Combine(root,"III85III VPN.exe"))=="old","old executable preserved");
                Check(Directory.GetDirectories(workspace).Length==1,"staging removed on failure");
            }
            bool callbackFailed=false;
            try{using(var payload=Package("app/III85III VPN.exe"))InstallPackage.Install(payload,root,delegate(string stage){throw new IOException("fixture disk failure");});}catch(IOException){callbackFailed=true;}
            Check(callbackFailed&&File.ReadAllText(Path.Combine(root,"III85III VPN.exe"))=="old","preparation failure preserves installation");
            string stageFixture=Path.Combine(workspace,"staged"),backupFixture=Path.Combine(workspace,"previous");
            Directory.CreateDirectory(stageFixture);
            int moves=0;bool promotionFailed=false;
            try{InstallPackage.Promote(stageFixture,root,backupFixture,delegate(string source,string target){if(++moves==2)throw new IOException("fixture promotion failure");Directory.Move(source,target);});}catch(IOException){promotionFailed=true;}
            Check(promotionFailed&&moves==3,"promotion failure triggers rollback");
            Check(File.ReadAllText(Path.Combine(root,"III85III VPN.exe"))=="old"&&!Directory.Exists(backupFixture),"rollback restores old installation");
            Directory.Delete(stageFixture);
            using(var payload=Package("app/","app/III85III VPN.exe","app/docs/","app/docs/guide.txt"))InstallPackage.Install(payload,root,null);
            Check(File.ReadAllText(Path.Combine(root,"III85III VPN.exe")).Trim('\uFEFF')=="new","complete package promoted");
            Check(File.Exists(Path.Combine(root,"docs","guide.txt")),"supporting folders installed");
            Check(!File.Exists(Path.Combine(root,"old.dll")),"obsolete app file removed with previous tree");
            Check(File.ReadAllText(Path.Combine(workspace,"accounts.fixture"))=="untouched","separate data untouched");
            Check(Directory.GetDirectories(workspace).Length==1,"successful install leaves no staging trees");
            string fresh=Path.Combine(workspace,"fresh");
            using(var payload=Package("app/III85III VPN.exe"))InstallPackage.Install(payload,fresh,null);
            Check(File.Exists(Path.Combine(fresh,"III85III VPN.exe")),"fresh install supported");
            Console.WriteLine("All "+checks+" installer checks passed.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{if(Directory.Exists(workspace))Directory.Delete(workspace,true);}
    }
}
