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
    internal static class SignInTests {
        static int checks;
        static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        static object Field(object owner,string name){return owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);}
        static Task Submit(SignInForm form){return (Task)form.GetType().GetMethod("Submit",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);}
        static void Pump(Func<bool> done){var watch=Stopwatch.StartNew();while(!done()){if(watch.ElapsedMilliseconds>7000)throw new Exception("Sign-in fixture timed out");Application.DoEvents();Thread.Sleep(5);}}
        static void Finish(Task task){Pump(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
        static void Prepare(SignInForm form){var handle=form.Handle;((TextBox)Field(form,"email")).Text="synthetic@example.invalid";((TextBox)Field(form,"password")).Text="fixture-password";}
        [STAThread]static int Main(){try{Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Run();Console.WriteLine("All "+checks+" sign-in lifecycle checks passed.");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        static void Run(){
            using(var fixture=new Fixture(false))using(var engine=new Engine("unused","unused",fixture.Endpoint,"fixture-local-token"))using(var form=new SignInForm(engine)){
                Prepare(form);var request=Submit(form);Pump(()=>fixture.LoginReceived.IsSet);form.Close();
                Check(form.IsDisposed&&(bool)Field(form,"closing"),"cancelling before the login response marks the window closed");
                Check(((TextBox)Field(form,"password")).Text=="","closing clears the password while the response is pending");
                fixture.ReleaseLogin.Set();Finish(request);fixture.Finish();
                Check(fixture.CancelCount==1,"a late successful response cancels its own temporary sign-in session");
                Check(form.IsDisposed&&form.DialogResult!=DialogResult.OK,"late completion cannot reopen or accept a cancelled login");
                Check(fixture.Count==2,"late cancellation performs only login and cancellation requests");
            }
            using(var fixture=new Fixture(false))using(var engine=new Engine("unused","unused",fixture.Endpoint,"fixture-local-token"))using(var form=new SignInForm(engine)){
                Prepare(form);var request=Submit(form);Pump(()=>fixture.LoginReceived.IsSet);
                var duplicate=Submit(form);Check(duplicate.IsCompleted&&fixture.Count==1,"duplicate submit cannot create a second login request");
                fixture.ReleaseLogin.Set();Finish(request);
                Check((string)Field(form,"session")=="fixture-session","the active window keeps its returned session");
                form.Close();Pump(()=>form.IsDisposed);fixture.Finish();
                Check(fixture.CancelCount==1,"closing an active waiting session cancels it exactly once");
            }
            using(var fixture=new Fixture(true))using(var engine=new Engine("unused","unused",fixture.Endpoint,"fixture-local-token"))using(var form=new SignInForm(engine)){
                Prepare(form);var request=Submit(form);Pump(()=>fixture.LoginReceived.IsSet);form.Close();fixture.ReleaseLogin.Set();Finish(request);fixture.Finish();
                Check(form.IsDisposed&&fixture.CancelCount==0,"a late failed response leaves the cancelled window closed");
            }
            using(var fixture=new Fixture(true))using(var engine=new Engine("unused","unused",fixture.Endpoint,"fixture-local-token"))using(var form=new SignInForm(engine)){
                Prepare(form);fixture.ReleaseLogin.Set();Finish(Submit(form));fixture.Finish();
                Check(((Button)Field(form,"submit")).Enabled&&!(bool)Field(form,"submitting"),"an active login can retry after a request failure");
                Check(((Label)Field(form,"feedback")).Text.Contains("Synthetic service unavailable"),"an active window reports its request failure");form.Close();
            }
        }
        sealed class Fixture:IDisposable {
            readonly TcpListener listener=new TcpListener(IPAddress.Loopback,0);readonly Task worker;readonly bool fail;bool disposed;int count,cancelCount;
            public readonly ManualResetEventSlim LoginReceived=new ManualResetEventSlim(false),ReleaseLogin=new ManualResetEventSlim(false);
            public int Count {get{return Volatile.Read(ref count);}}public int CancelCount {get{return Volatile.Read(ref cancelCount);}}
            public string Endpoint {get;private set;}
            public Fixture(bool fail){this.fail=fail;listener.Start();Endpoint="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;worker=Task.Run((Func<Task>)Serve);}
            async Task Serve(){try{
                for(int n=0;n<(fail?1:2);n++)using(var client=await listener.AcceptTcpClientAsync())using(var stream=client.GetStream())using(var reader=new StreamReader(stream,Encoding.UTF8,false,1024,true)){
                    string first=await reader.ReadLineAsync(),line;int length=0;
                    while(!String.IsNullOrEmpty(line=await reader.ReadLineAsync()))if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))length=Int32.Parse(line.Substring(15).Trim());
                    if(length>4096)throw new Exception("Fixture input exceeded limit");var body=new char[length];int read=0;while(read<length){int amount=await reader.ReadAsync(body,read,length-read);if(amount==0)break;read+=amount;}
                    Interlocked.Increment(ref count);
                    if(n==0){if(first!="POST /api/signin HTTP/1.1")throw new Exception("Unexpected initial request");LoginReceived.Set();if(!ReleaseLogin.Wait(5000))throw new Exception("Login response was not released");}
                    else{if(first!="DELETE /api/signin/fixture-session HTTP/1.1")throw new Exception("Unexpected cancellation request");Interlocked.Increment(ref cancelCount);}
                    string response=fail?"{\"error\":\"Synthetic service unavailable\"}":n==0?"{\"id\":\"fixture-session\",\"step\":\"waiting\"}":"{}";
                    byte[] bytes=Encoding.UTF8.GetBytes("HTTP/1.1 "+(fail?"503 Unavailable":"200 OK")+"\r\nContent-Type: application/json\r\nContent-Length: "+Encoding.UTF8.GetByteCount(response)+"\r\nConnection: close\r\n\r\n"+response);await stream.WriteAsync(bytes,0,bytes.Length);
                }
            }catch(ObjectDisposedException){if(!disposed)throw;}catch(SocketException){if(!disposed)throw;}}
            public void Finish(){SignInTests.Finish(worker);}
            public void Dispose(){disposed=true;ReleaseLogin.Set();listener.Stop();try{worker.Wait(1500);}catch{}LoginReceived.Dispose();ReleaseLogin.Dispose();}
        }
    }
}
