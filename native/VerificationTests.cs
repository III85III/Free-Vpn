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
    internal static class VerificationTests {
        static object Field(object value,string name){return value.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(value);}
        static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
        static void Pump(Func<bool> complete){var clock=Stopwatch.StartNew();while(!complete()){if(clock.ElapsedMilliseconds>7000)throw new Exception("Verification fixture timed out");Application.DoEvents();Thread.Sleep(5);}}
        [STAThread]static int Main(){try{Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Run(409);Run(202);return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        static void Run(int response){
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port,posts=0;
            var worker=Task.Run(async delegate{
                for(int i=0;i<2;i++)using(var client=await listener.AcceptTcpClientAsync())using(var stream=client.GetStream())using(var reader=new StreamReader(stream,Encoding.UTF8,false,1024,true)){
                    string first=await reader.ReadLineAsync();if(i==0){if(!first.StartsWith("POST /api/signin/fixture "))throw new Exception("Unexpected code request");posts++;}else if(!first.StartsWith("GET /api/signin/fixture "))throw new Exception("Expected state reconciliation, never another code");
                    int length=0;string line;while(!String.IsNullOrEmpty(line=await reader.ReadLineAsync()))if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))length=Int32.Parse(line.Substring(15).Trim());
                    var body=new char[length];int offset=0;while(offset<length){int count=await reader.ReadAsync(body,offset,length-offset);if(count==0)break;offset+=count;}
                    string json=i==1?"{\"step\":\"done\",\"updated\":\"stamp2\"}":response==409?"{\"error\":\"this sign-in is not waiting for input\"}":"{\"step\":\"need_email_code\",\"updated\":\"stamp1\"}";
                    byte[] bytes=Encoding.UTF8.GetBytes("HTTP/1.1 "+(i==0?response:200)+" Test\r\nContent-Type: application/json\r\nContent-Length: "+Encoding.UTF8.GetByteCount(json)+"\r\nConnection: close\r\n\r\n"+json);await stream.WriteAsync(bytes,0,bytes.Length);
                }
            });
            try{using(var engine=new Engine("unused","unused","http://127.0.0.1:"+port,"synthetic-token"))using(var form=new VerificationForm(engine,"fixture",Json.Decode("{\"step\":\"need_email_code\",\"updated\":\"stamp1\"}"))){
                form.Show();((TextBox)Field(form,"code")).Text="123456";
                var submit=(Task)form.GetType().GetMethod("Submit",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,null);Pump(()=>submit.IsCompleted);submit.GetAwaiter().GetResult();
                if(response==202)Check(form.IsDisposed||!((Button)Field(form,"verify")).Enabled,"stale code prompt cannot submit an already accepted answer twice");
                Pump(()=>form.IsDisposed);Pump(()=>worker.IsCompleted);worker.GetAwaiter().GetResult();
                Check(form.DialogResult==DialogResult.OK,"completed login is recognized after HTTP "+response);Check(posts==1,"reconciliation never sends the verification code again");
            }}finally{listener.Stop();}
        }
    }
}
