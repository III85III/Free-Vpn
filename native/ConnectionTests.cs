using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace III85III.Vpn {
    internal static class ConnectionTests {
        static void Set(object owner,string name,object value){owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(owner,value);}
        static void Pump(Func<bool> complete){var clock=Stopwatch.StartNew();while(!complete()){if(clock.ElapsedMilliseconds>7000)throw new Exception("Connection fixture timed out");Application.DoEvents();Thread.Sleep(5);}}
        [STAThread]static int Main(){try{Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Run();return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        static void Run(){
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
            string[] expected={"GET /api/accounts ","POST /api/system-proxy ","POST /api/power ","GET /api/status "};
            var worker=Task.Run(async delegate{
                for(int index=0;index<expected.Length;index++)using(var socket=await listener.AcceptTcpClientAsync())using(var stream=socket.GetStream())using(var reader=new StreamReader(stream,Encoding.UTF8,false,1024,true)){
                    string first=await reader.ReadLineAsync();if(!first.StartsWith(expected[index]))throw new Exception("Saved-account connect did not reach the expected API step");int size=0;string header;
                    while(!String.IsNullOrEmpty(header=await reader.ReadLineAsync()))if(header.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))size=Int32.Parse(header.Substring(15).Trim());if(size>4096)throw new Exception("Fixture body too large");char[] body=new char[size];int read=0;while(read<size){int count=await reader.ReadAsync(body,read,size-read);if(count==0)break;read+=count;}
                    string content=new string(body);if(index==1&&Json.Str(Json.Decode(content),"mode")!="clear")throw new Exception("Unexpected mode");if(index==2&&!Json.Bool(Json.Decode(content),"on"))throw new Exception("Connect was not requested");
                    string response=index==0?"{\"accounts\":[{\"id\":\"fixture\",\"enabled\":true,\"status\":\"ready\"}]}":index==3?"{\"state\":{\"phase\":\"off\"},\"accounts\":{\"total\":1}}":"{}";
                    var bytes=Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: "+Encoding.UTF8.GetByteCount(response)+"\r\nConnection: close\r\n\r\n"+response);await stream.WriteAsync(bytes,0,bytes.Length);
                }
            });
            try{using(var engine=new Engine("unused","unused","http://127.0.0.1:"+port,"synthetic-token"))using(var form=new MainForm(engine,true)){
                form.Show();Set(form,"ready",true);Set(form,"pendingModeIndex",2);Set(form,"lastStatus",Json.Decode("{\"state\":{\"phase\":\"off\"},\"accounts\":{\"total\":1}}"));
                var connect=(Task)form.GetType().GetMethod("ConnectFromHome",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);Pump(()=>connect.IsCompleted);connect.GetAwaiter().GetResult();Pump(()=>worker.IsCompleted);worker.GetAwaiter().GetResult();
                Console.WriteLine("PASS saved account without active_account reaches power-on through the real home-button handler");
            }}finally{listener.Stop();}
        }
    }
}
