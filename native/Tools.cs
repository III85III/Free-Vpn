using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;
[assembly:System.Reflection.AssemblyCompany("III85III")]
[assembly:System.Reflection.AssemblyProduct("III85III VPN")]
[assembly:System.Reflection.AssemblyTitle("III85III private account helper")]
[assembly:System.Reflection.AssemblyVersion("2.0.0.0")]
internal static class Tools {
    static readonly byte[] Entropy=Encoding.UTF8.GetBytes("III85III.VPN.Independent.v1");
    static int Main(string[] args){
        try{
            if(args.Length==0)throw new Exception("Missing helper operation.");
            switch(args[0]){
                case "protect":{var plain=Encoding.UTF8.GetBytes(Console.In.ReadToEnd());try{Console.Write(Convert.ToBase64String(ProtectedData.Protect(plain,Entropy,DataProtectionScope.CurrentUser)));}finally{Array.Clear(plain,0,plain.Length);}break;}
                case "unprotect":{var plain=ProtectedData.Unprotect(Convert.FromBase64String(Console.In.ReadToEnd().Trim()),Entropy,DataProtectionScope.CurrentUser);try{Console.Write(Encoding.UTF8.GetString(plain));}finally{Array.Clear(plain,0,plain.Length);}break;}
                case "private-dir":{
                    if(args.Length!=2)throw new Exception("Missing private data folder.");var folder=Path.GetFullPath(args[1]);Directory.CreateDirectory(folder);
                    var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
                    foreach(var sid in new[]{WindowsIdentity.GetCurrent().User,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)})acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
                    new DirectoryInfo(folder).SetAccessControl(acl);break;
                }
                case "processes":{
                    var found=new SortedDictionary<string,object>(StringComparer.OrdinalIgnoreCase);
                    foreach(var process in Process.GetProcesses())using(process){try{string file=process.MainModule.FileName;string name=Path.GetFileName(file);if(!found.ContainsKey(name))found[name]=new{name=name,path=file};}catch{}}
                    Console.Write(new JavaScriptSerializer().Serialize(new List<object>(found.Values)));break;
                }
                default:throw new Exception("Unknown helper operation.");
            }return 0;
        }catch{Console.Error.Write("Private helper operation failed.");return 1;}
    }
}
