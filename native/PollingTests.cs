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
    internal static class PollingTests {
        static int checks;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static object Field(object owner,string name){return owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);}
        static void Ready(MainForm form){form.GetType().GetField("ready",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,true);}
        static object Call(object owner,string name,params object[] args){return owner.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(owner,args);}
        static void Pump(Func<bool> done,int timeout=6500){var watch=Stopwatch.StartNew();while(!done()){if(watch.ElapsedMilliseconds>timeout)throw new Exception("Polling fixture timed out");Application.DoEvents();Thread.Sleep(5);}}
        static void Finish(Task task,int timeout=6500){Pump(()=>task.IsCompleted,timeout);task.GetAwaiter().GetResult();}
        static bool Connected(MainForm form){return ((LuxuryDial)Field(form,"dial")).State==DialState.Connected;}
        [STAThread]static int Main(){try{Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Run();Console.WriteLine("All "+checks+" polling checks passed.");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        static void Run(){
            using(var fixture=new Fixture(2,false))using(var engine=new Engine("unused","unused",fixture.Endpoint,"synthetic-token"))using(var form=new MainForm(engine,true)){
                Ready(form);Finish((Task)Call(form,"Poll"));Finish((Task)Call(form,"Poll"));fixture.Finish();
                Check(fixture.StatusCount==2,"home keeps refreshing connection status");
                Check(fixture.LogCount==0,"hidden diagnostics cause no log requests");
                Check(Connected(form),"verified status remains connected without reading diagnostics");
            }
            using(var fixture=new Fixture(3,true))using(var engine=new Engine("unused","unused",fixture.Endpoint,"synthetic-token"))using(var form=new MainForm(engine,true)){
                Ready(form);Call(form,"SwitchPage",5);var first=(Task)Call(form,"Poll");Pump(()=>fixture.LogReceived.IsSet);Finish(first,2000);
                Check(!fixture.ReleaseLogs.IsSet&&first.IsCompleted,"held diagnostics do not hold the status refresh open");
                Finish((Task)Call(form,"Poll"),2000);Check(fixture.StatusCount==2,"another status request completes while diagnostics are still pending");
                var duplicate=(Task)Call(form,"RefreshLogs",true);Check(duplicate.IsCompleted&&fixture.LogCount==1,"only one diagnostic request can be in flight");
                Check(Connected(form),"connection display stays verified during delayed diagnostics");
                fixture.ReleaseLogs.Set();Pump(()=>!(bool)Field(form,"logsPolling"));fixture.Finish();
                Check(Connected(form),"diagnostic HTTP failure cannot reset the connection display");
                string text=((Control)Field(form,"logsBox")).Text;
                Check(text.Contains("Diagnostics couldn't refresh"),"help reports a diagnostic-only refresh failure");
                Check(!text.Contains("private@example.invalid")&&!text.Contains("fixture-secret"),"diagnostic error does not echo private service details");
            }
            using(var fixture=new Fixture(2,false))using(var engine=new Engine("unused","unused",fixture.Endpoint,"synthetic-token"))using(var form=new MainForm(engine,true)){
                fixture.ReleaseLogs.Set();Ready(form);Call(form,"SwitchPage",5);Pump(()=>fixture.LogCount==1&&!(bool)Field(form,"logsPolling"));
                Check((long)Field(form,"lastLogId")==2,"first diagnostic batch advances the cursor");
                Finish((Task)Call(form,"RefreshLogs",true));fixture.Finish();
                Check(fixture.LogPaths.Count==2&&fixture.LogPaths[1]=="/api/logs?after=2","manual help refresh uses the last displayed cursor");
                string text=((Control)Field(form,"logsBox")).Text;
                Check(text.Split(new[]{"second-fixture-entry"},StringSplitOptions.None).Length==2,"already displayed entries are not appended again");
                Check(text.Contains("third-fixture-entry")&&(long)Field(form,"lastLogId")==3,"a fresh diagnostic batch appends its new entry");
            }
        }
        sealed class Fixture:IDisposable {
            readonly TcpListener listener=new TcpListener(IPAddress.Loopback,0);readonly Task worker;readonly int requests;readonly bool failLogs;bool disposed;int statusCount,logCount;
            public readonly ManualResetEventSlim LogReceived=new ManualResetEventSlim(false),ReleaseLogs=new ManualResetEventSlim(false);
            public readonly List<string> LogPaths=new List<string>();
            public int StatusCount {get{return Volatile.Read(ref statusCount);}}public int LogCount {get{return Volatile.Read(ref logCount);}}
            public string Endpoint {get;private set;}
            public Fixture(int requests,bool failLogs){this.requests=requests;this.failLogs=failLogs;listener.Start();Endpoint="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;worker=Task.Run((Func<Task>)Serve);}
            async Task Serve(){var clients=new List<Task>();try{for(int n=0;n<requests;n++){var client=await listener.AcceptTcpClientAsync();clients.Add(Task.Run(()=>Handle(client)));}await Task.WhenAll(clients);}catch(ObjectDisposedException){if(!disposed)throw;}catch(SocketException){if(!disposed)throw;}}
            async Task Handle(TcpClient client){try{using(client)using(var stream=client.GetStream())using(var reader=new StreamReader(stream,Encoding.UTF8,false,1024,true)){
                string first=await reader.ReadLineAsync(),line;while(!String.IsNullOrEmpty(line=await reader.ReadLineAsync())){}string path=first.Split(' ')[1];string response;int code=200;
                if(path=="/api/status"){
                    Interlocked.Increment(ref statusCount);string now=DateTime.UtcNow.ToString("o");response=Json.Encode(new{state=new{phase="running",since=DateTime.UtcNow.AddMinutes(-1).ToString("o")},proxy=new{started_at="fixture-start",wake_error=""},system_proxy="tun",tun=new{state="on"},last_test=new{ok=true,at=now},active_account=new{email="synthetic@example.invalid",usage=new{unlimited=true}}});
                }else if(path.StartsWith("/api/logs?after=",StringComparison.Ordinal)){
                    lock(LogPaths)LogPaths.Add(path);int index=Interlocked.Increment(ref logCount);LogReceived.Set();if(!ReleaseLogs.Wait(5000))throw new Exception("Diagnostic gate was not released");
                    if(failLogs){code=503;response="{\"error\":\"private@example.invalid fixture-secret\"}";}
                    else response=index==1?"{\"entries\":[{\"id\":1,\"message\":\"first-fixture-entry\"},{\"id\":2,\"message\":\"second-fixture-entry\"}]}":"{\"entries\":[{\"id\":2,\"message\":\"second-fixture-entry\"},{\"id\":3,\"message\":\"third-fixture-entry\"}]}";
                }else throw new Exception("Unexpected fixture endpoint");
                byte[] bytes=Encoding.UTF8.GetBytes("HTTP/1.1 "+code+" Fixture\r\nContent-Type: application/json\r\nContent-Length: "+Encoding.UTF8.GetByteCount(response)+"\r\nConnection: close\r\n\r\n"+response);await stream.WriteAsync(bytes,0,bytes.Length);
            }}catch(IOException){if(!disposed)throw;}catch(ObjectDisposedException){if(!disposed)throw;}}
            public void Finish(){PollingTests.Finish(worker);}
            public void Dispose(){disposed=true;ReleaseLogs.Set();listener.Stop();try{worker.Wait(1500);}catch{}LogReceived.Dispose();ReleaseLogs.Dispose();}
        }
    }
}
