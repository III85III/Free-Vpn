using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

internal static class InstallPackage {
    internal static void Install(Stream payload,string destination,Action<string> prepareDirectory){
        string root=Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
        string parent=Path.GetDirectoryName(root);
        Directory.CreateDirectory(parent);
        string stage=Path.Combine(parent,".install-"+Guid.NewGuid().ToString("N"));
        string backup=Path.Combine(parent,".previous-"+Guid.NewGuid().ToString("N"));
        bool installed=false;
        try{
            Directory.CreateDirectory(stage);
            if(prepareDirectory!=null)prepareDirectory(stage);
            string prefix=stage+Path.DirectorySeparatorChar;
            var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string packageRoot=null;
            using(var archive=new ZipArchive(payload,ZipArchiveMode.Read,true)){
                foreach(var entry in archive.Entries){
                    string name=entry.FullName.Replace('\\','/');
                    int first=name.IndexOf('/');
                    if(first<=0)throw new IOException("Invalid package layout.");
                    string folder=name.Substring(0,first);
                    if(packageRoot==null)packageRoot=folder;
                    if(folder!=packageRoot)throw new IOException("Mixed package roots.");
                    name=name.Substring(first+1);
                    if(name.Length==0)continue;
                    foreach(string part in name.TrimEnd('/').Split('/'))
                        if(part.Length==0||part=="."||part==".."||part.IndexOfAny(Path.GetInvalidFileNameChars())>=0||part.EndsWith(".")||part.EndsWith(" "))throw new IOException("Invalid package path.");
                    string output=Path.GetFullPath(Path.Combine(stage,name.Replace('/',Path.DirectorySeparatorChar)));
                    if(!output.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!paths.Add(output.TrimEnd(Path.DirectorySeparatorChar)))throw new IOException("Invalid or duplicate package path.");
                    if(name.EndsWith("/")){Directory.CreateDirectory(output);continue;}
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    using(var input=entry.Open())using(var target=new FileStream(output,FileMode.CreateNew,FileAccess.Write))input.CopyTo(target);
                }
            }
            string executable=Path.Combine(stage,"III85III VPN.exe");
            if(!File.Exists(executable)||new FileInfo(executable).Length==0)throw new IOException("Incomplete package.");
            Promote(stage,root,backup,Directory.Move);
            installed=true;
        }finally{
            TryDelete(stage);
            // A failed rollback must retain the previous installation for recovery.
            if(installed)TryDelete(backup);
        }
    }
    internal static void Promote(string stage,string root,string backup,Action<string,string> move){
        bool movedOld=Directory.Exists(root);
        if(movedOld)move(root,backup);
        try{move(stage,root);}
        catch{if(movedOld)move(backup,root);throw;}
    }
    static void TryDelete(string path){try{if(Directory.Exists(path))Directory.Delete(path,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
}
