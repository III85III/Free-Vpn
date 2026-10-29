using System;
using System.IO;
namespace III85III.Vpn {
    internal static class UpdateTests {
        static string Document(string url,string hash="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",long size=12000000){return Json.Encode(new{schema=1,windows=new{version="2.2.2",url=url,sha256=hash,size=size}});}
        static void Reject(string text){try{AppUpdate.Read(Json.Decode(text),"windows");throw new Exception("Unsafe update accepted");}catch(IOException){}}
        static int Main(){try{string url="https://github.com/III85III/Free-Vpn/releases/download/v2.3.1/III85III-VPN-Setup.exe";var valid=AppUpdate.Read(Json.Decode(Document(url)),"windows");if(valid.Version!="2.2.2")throw new Exception("Version was lost");Reject(Document(url.Replace("https:","http:")));Reject(Document(url.Replace("III85III/Free-Vpn","other/project")));Reject(Document(url,""));Reject(Document(url,"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",300L*1024*1024));Reject(Document(url.Replace("Setup.exe","evil.exe")));Console.WriteLine("All 6 update metadata checks passed.");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
    }
}
