using System;
using System.Collections.Generic;
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
    internal static class LatencyTests {
        static int checks;
        static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        static object Field(object value,string name){return value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);}
        static void Set(object value,string name,object data){value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(value,data);}
        static object Call(object value,string name,params object[] args){return value.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,args);}
        static void Pump(Func<bool> complete){var elapsed=Stopwatch.StartNew();while(!complete()){if(elapsed.ElapsedMilliseconds>6000)throw new Exception("Local fixture timed out");Application.DoEvents();Thread.Sleep(5);}}
        static void Finish(Task task){Pump(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
        [STAThread]static int Main(){try{Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Run();Console.WriteLine("All "+checks+" latency checks passed.");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        static void Run(){
            using(var fixture=new Fixture(false))
            using(var engine=new Engine("unused","unused",fixture.Endpoint,"synthetic-local-token"))
            using(var form=new MainForm(engine,true)){
                Set(form,"ready",true);Call(form,"SetActions",true);
                Call(form,"SwitchPage",1);Call(form,"SwitchPage",0);
                Check(fixture.Count==0,"opening locations does not initiate a latency scan");
                var scan=(Task)Call(form,"MeasureLocationLatency");Pump(()=>fixture.Received.IsSet);
                Check(fixture.Count==1&&fixture.Paths[0]=="POST /api/locations/ping HTTP/1.1","manual measurement really reaches the local engine");
                Check(!((Button)Field(form,"pingBtn")).Enabled,"only latency action is locked while measuring");
                Check(((Button)Field(form,"connectBtn")).Enabled,"connection controls stay usable during latency measurement");
                Check(!(bool)Field(form,"busy"),"measurement does not seize the general action lock");
                var duplicate=(Task)Call(form,"MeasureLocationLatency");Check(duplicate.IsCompleted&&fixture.Count==1,"repeated clicks do not queue duplicate scans");
                fixture.Release.Set();Finish(scan);fixture.Finish();
                Check(fixture.Count==2&&fixture.Paths[1]=="GET /api/locations HTTP/1.1","completed scan refreshes the location measurements once");
                var locations=(ListBox)Field(form,"locationList");bool measured=false;foreach(ServerChoice choice in locations.Items)if(choice.Code=="gb"&&choice.PingMs==12)measured=true;
                Check(measured,"real fixture latency appears in the locations list");
                Check(((Button)Field(form,"pingBtn")).Enabled&&!((bool)Field(form,"pingScanBusy")),"measurement control is restored after success");
            }
            using(var fixture=new Fixture(true))
            using(var engine=new Engine("unused","unused",fixture.Endpoint,"synthetic-local-token"))
            using(var form=new MainForm(engine,true)){
                Set(form,"ready",true);Call(form,"SetActions",true);fixture.Release.Set();
                bool noticeSeen=false;
                using(var timer=new System.Windows.Forms.Timer{Interval=50}){
                    timer.Tick+=delegate{foreach(Form notice in Application.OpenForms)if(notice!=form){noticeSeen=true;notice.DialogResult=DialogResult.OK;break;}};timer.Start();
                    Finish((Task)Call(form,"MeasureLocationLatency"));fixture.Finish();
                }
                Check(noticeSeen,"failed ping test opens an in-app notice instead of exposing a raw error on Home");
                Check(((Button)Field(form,"pingBtn")).Enabled&&!((bool)Field(form,"pingScanBusy")),"failed measurement can be retried");
                Check(fixture.Count==1,"failed measurement does not refresh locations or retry silently");
            }
            using(var fixture=new Fixture(false))
            using(var engine=new Engine("unused","unused",fixture.Endpoint,"synthetic-local-token"))
            using(var form=new MainForm(engine,true)){
                Set(form,"ready",true);Call(form,"SetActions",true);
                var helpButton=(Button)Field(form,"diagPingBtn");
                typeof(Control).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(helpButton,new object[]{EventArgs.Empty});
                Pump(()=>fixture.Received.IsSet);
                Check((int)Field(form,"activePageIndex")==1,"Help measurement opens the location results page");
                Check(!helpButton.Enabled&&!((Button)Field(form,"pingBtn")).Enabled,"both measurement entry points stay disabled during one scan");
                Check(((Button)Field(form,"connectBtn")).Enabled&&!(bool)Field(form,"busy"),"Help measurement cannot lock connection controls");
                Call(form,"SetActions",true);Check(!helpButton.Enabled,"status refresh cannot reenable Help measurement during scan");
                Finish((Task)Call(form,"MeasureLocationLatency"));Check(fixture.Count==1,"location action cannot duplicate a Help scan");
                fixture.Release.Set();Pump(()=>!(bool)Field(form,"pingScanBusy"));fixture.Finish();
                Check(fixture.Count==2&&helpButton.Enabled&&((Button)Field(form,"pingBtn")).Enabled,"Help measurement refreshes results and restores both controls");
            }
        }
        sealed class Fixture:IDisposable {
            readonly TcpListener listener=new TcpListener(IPAddress.Loopback,0);readonly Task worker;readonly bool fail;bool disposed;
            public readonly ManualResetEventSlim Received=new ManualResetEventSlim(false),Release=new ManualResetEventSlim(false);
            public readonly List<string> Paths=new List<string>();
            public int Count {get{lock(Paths)return Paths.Count;}}
            public string Endpoint {get;private set;}
            public Fixture(bool fail){this.fail=fail;listener.Start();Endpoint="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;worker=Task.Run((Func<Task>)Serve);}
            async Task Serve(){
                try{for(int n=0;n<(fail?1:2);n++){
                    using(var client=await listener.AcceptTcpClientAsync())using(var stream=client.GetStream())using(var reader=new StreamReader(stream,Encoding.UTF8,false,1024,true)){
                        string first=await reader.ReadLineAsync(),line;int length=0;
                        while(!String.IsNullOrEmpty(line=await reader.ReadLineAsync()))if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))length=Int32.Parse(line.Substring(15).Trim());
                        if(length>4096)throw new Exception("Fixture body too large");var body=new char[length];int read=0;while(read<length){int amount=await reader.ReadAsync(body,read,length-read);if(amount==0)break;read+=amount;}
                        lock(Paths)Paths.Add(first);Received.Set();if(n==0&&!Release.Wait(5000))throw new Exception("Fixture response not released");
                        string response=fail?"{\"error\":\"Latency fixture unavailable\"}":n==0?"{}":"{\"locations\":[{\"code\":\"gb\",\"name\":\"United Kingdom\",\"ping_ms\":12}]}";
                        byte[] bytes=Encoding.UTF8.GetBytes("HTTP/1.1 "+(fail?"503 Unavailable":"200 OK")+"\r\nContent-Type: application/json\r\nContent-Length: "+Encoding.UTF8.GetByteCount(response)+"\r\nConnection: close\r\n\r\n"+response);await stream.WriteAsync(bytes,0,bytes.Length);
                    }
                }}catch(ObjectDisposedException){if(!disposed)throw;}catch(SocketException){if(!disposed)throw;}
            }
            public void Finish(){LatencyTests.Finish(worker);}
            public void Dispose(){disposed=true;Release.Set();listener.Stop();try{worker.Wait(1500);}catch{}Received.Dispose();Release.Dispose();}
        }
    }
}
