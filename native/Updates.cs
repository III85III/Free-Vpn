using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace III85III.Vpn {
    internal sealed class AppUpdate {
        internal string Version, Url, Hash; internal long Size;
        internal static AppUpdate Read(Dictionary<string,object> document,string platform) {
            if(Json.Num(document,"schema")!=1)throw new IOException("Unsupported update information.");
            var item=Json.Child(document,platform);System.Version parsed;Uri url;
            string version=Json.Str(item,"version"),address=Json.Str(item,"url"),hash=Json.Str(item,"sha256");
            if(!System.Version.TryParse(version,out parsed)||parsed.Revision>=0||!Uri.TryCreate(address,UriKind.Absolute,out url)||url.Scheme!="https"||url.Host!="github.com"||!url.AbsolutePath.StartsWith("/III85III/Free-Vpn/releases/download/",StringComparison.Ordinal)||!url.AbsolutePath.EndsWith("/III85III-VPN-Setup.exe",StringComparison.Ordinal)||url.Query!=""||url.UserInfo!="")throw new IOException("Invalid update download.");
            if(hash.Length!=64||!System.Text.RegularExpressions.Regex.IsMatch(hash,"\\A[0-9a-fA-F]{64}\\z"))throw new IOException("Invalid update checksum.");
            long size=(long)Json.Num(item,"size");if(size<1024||size>250L*1024*1024)throw new IOException("Invalid update size.");
            return new AppUpdate{Version=version,Url=address,Hash=hash,Size=size};
        }
    }
    internal static class AppUpdates {
        const string Feed="https://raw.githubusercontent.com/III85III/Free-Vpn/main/docs/updates.json";
        static HttpClient Client(){var c=new HttpClient(new HttpClientHandler{UseProxy=false});c.Timeout=TimeSpan.FromMinutes(5);c.DefaultRequestHeaders.UserAgent.ParseAdd("III85III-VPN/"+Assembly.GetExecutingAssembly().GetName().Version);return c;}
        internal static async Task Check(Form owner,Action<string> report,Action<string> install,bool manual){
            try{
                AppUpdate update;
                using(var client=Client())using(var response=await client.GetAsync(Feed,HttpCompletionOption.ResponseHeadersRead)){
                    response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>32768)throw new IOException("Update information is too large.");
                    using(var stream=await response.Content.ReadAsStreamAsync())using(var data=new MemoryStream()){
                        var buffer=new byte[4096];int count;while((count=await stream.ReadAsync(buffer,0,buffer.Length))>0){if(data.Length+count>32768)throw new IOException("Update information is too large.");data.Write(buffer,0,count);}
                        update=AppUpdate.Read(Json.Decode(System.Text.Encoding.UTF8.GetString(data.ToArray())),"windows");
                    }
                }
                if(owner.IsDisposed||(!manual&&Form.ActiveForm!=owner))return;
                if(new Version(update.Version)<=Assembly.GetExecutingAssembly().GetName().Version){if(manual)MessageBox.Show(owner,"You're using the latest Windows version.","III85III VPN",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
                if(MessageBox.Show(owner,"III85III VPN "+update.Version+" is available.\n\nDownload and open the installer? The VPN will disconnect during installation. Your local accounts will be kept.","Update available",MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes)return;
                string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"III85III VPN","updates");Engine.PrivateDirectory(directory);
                string temporary=Path.Combine(directory,Guid.NewGuid().ToString("N")+".part"),target=Path.Combine(directory,"III85III-VPN-Setup-"+update.Version+".exe");
                try{
                    using(var client=Client())using(var response=await client.GetAsync(update.Url,HttpCompletionOption.ResponseHeadersRead)){
                        response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength.HasValue&&response.Content.Headers.ContentLength.Value!=update.Size)throw new IOException("The update size does not match.");
                        using(var source=await response.Content.ReadAsStreamAsync())using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true)){
                            byte[] buffer=new byte[65536];long total=0;int count;while((count=await source.ReadAsync(buffer,0,buffer.Length))>0){total+=count;if(total>update.Size)throw new IOException("The update is larger than expected.");await file.WriteAsync(buffer,0,count);if(!owner.IsDisposed)report("Downloading update… "+(total*100/update.Size)+"%");}if(total!=update.Size)throw new IOException("The download was interrupted.");
                        }
                    }
                    string hash;using(var algorithm=SHA256.Create())using(var file=File.OpenRead(temporary))hash=BitConverter.ToString(algorithm.ComputeHash(file)).Replace("-","").ToLowerInvariant();
                    if(!String.Equals(hash,update.Hash,StringComparison.OrdinalIgnoreCase))throw new IOException("The update checksum does not match. Nothing was installed.");
                    if(File.Exists(target))File.Delete(target);File.Move(temporary,target);
                    if(!owner.IsDisposed)install(target);
                }finally{if(File.Exists(temporary))File.Delete(temporary);}
            }catch(Exception){if(!owner.IsDisposed){if(manual)MessageBox.Show(owner,"The update could not be checked or downloaded. Check your connection and try again. Nothing was installed.","III85III VPN",MessageBoxButtons.OK,MessageBoxIcon.Information);report("Update unavailable. You can retry from Settings.");}}
        }
    }
}
