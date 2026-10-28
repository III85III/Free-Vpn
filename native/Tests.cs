using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace III85III.Vpn {
    internal static class Tests {
        static int checks;
        static void Check(bool condition,string name){if(!condition)throw new Exception("FAILED: "+name);checks++;Console.WriteLine("PASS "+name);}
        static Dictionary<string,object> State(string phase="running",string tun="on",bool ok=true,string testAt="2026-10-27T12:01:00Z"){
            return Json.Decode(Json.Encode(new{state=new{phase=phase,since="2026-10-27T12:00:00Z"},proxy=new{wake_error=""},system_proxy="tun",tun=new{state=tun},last_test=new{ok=ok,at=testAt}}));
        }
        static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("All "+checks+" checks passed.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
        static async Task Run(){
            Check(ConnectionState.Verified(State()),"fresh successful test + TUN on is connected");
            Check(!ConnectionState.Verified(State("off")),"off is disconnected despite previous successful test");
            Check(!ConnectionState.Verified(State("running","error")),"failed TUN never displays connected");
            Check(!ConnectionState.Verified(State("running","on",false)),"failed upstream test never displays connected");
            Check(!ConnectionState.Verified(State("running","on",true,"2026-10-27T11:59:00Z")),"stale test from previous connection is rejected");
            var network=State();network["network_changed_at"]="2026-10-27T12:02:00Z";Check(!ConnectionState.Verified(network),"network change invalidates earlier success");
            var failed=State();failed["connect_failure"]="quota";Check(!ConnectionState.Verified(failed),"quota failure invalidates connection");
            var replaced=State();replaced["system_proxy_taken"]=new{by="other proxy"};Check(!ConnectionState.Verified(replaced),"another program replacing the system proxy invalidates connection");
            var wake=State();wake["proxy"]=new Dictionary<string,object>{{"wake_error","refused"}};Check(!ConnectionState.Verified(wake),"upstream wake failure invalidates connection");
            var local=State();local["system_proxy"]="clear";local["tun"]=new{};Check(ConnectionState.Verified(local),"local proxy can verify without a system TUN");
            await RequestTest("GET",null,200,"{\"state\":\"ok\"}",false);
            await RequestTest("POST",new{on=false},200,"{}",false);
            await RequestTest("POST",new{value="123456"},409,"{\"error\":\"verification refused\"}",true);
            await RequestTest("POST",new{},302,"{\"error\":\"redirect refused\"}",true);
            Check(Theme.Bytes(50000000000d)=="50.0 GB","quota displayed in decimal GB like service");
            Check(Theme.Bytes(0)=="0 B","zero traffic rendered correctly");
            var before=Json.Decode("{\"proxy\":{\"transport\":\"h2\",\"country\":\"DE\"},\"dashboard\":{\"listen\":\"127.0.0.1:12345\"},\"tun\":{\"future_option\":true,\"programs\":[]}}");
            SplitRules.Apply(before,new[]{new AppBypass("C:\\Games\\Game.exe","C:\\Games\\Game.exe"),new AppBypass("game.EXE","duplicate"),new AppBypass("discord.exe","",true)});
            var split=SplitRules.Read(Json.Decode(Json.Encode(before)));
            Check(split.Count==2&&split.ContainsKey("GAME.exe"),"bypass app names normalized and deduplicated case-insensitively");
            Check(split["discord.exe"].UdpOnly,"existing UDP-only rule is preserved");
            Check(!split["game.exe"].UdpOnly,"new selected app bypasses all traffic");
            Check(Json.Str(Json.Child(before,"proxy"),"country")=="DE"&&Json.Bool(Json.Child(before,"tun"),"future_option"),"saving split apps preserves unrelated engine settings");
            SplitRules.Apply(before,new AppBypass[0]);Check(SplitRules.Read(Json.Decode(Json.Encode(before))).Count==0,"unchecking every app removes all bypasses");
            var policy=new WarmupPolicy();var start=new DateTime(2026,10,27,12,2,0,DateTimeKind.Utc);
            var cold=State("running","on",false);
            Check(policy.ShouldTest(cold,start),"new unverified connection is warmed immediately");
            Check(!policy.ShouldTest(cold,start.AddSeconds(1)),"warmup retries are rate limited");
            policy.Finished(start.AddSeconds(20));Check(!policy.ShouldTest(cold,start.AddSeconds(21)),"slow test does not immediately queue another test");
            Check(policy.ShouldTest(cold,start.AddSeconds(26)),"failed warmup retries after backoff");
            Check(!policy.ShouldTest(State(),start.AddMinutes(1)),"verified connection does not generate repeated speed-test traffic");
            Check(!new WarmupPolicy().ShouldTest(State("off"),start),"opening disconnected app never initiates a connection");
            Check(ConnectionState.QuickCountry("GB")=="GB","quick connect preserves the measured faster UK server");
            Check(ConnectionState.QuickCountry("fastest")=="","quick connect avoids a full lowest-latency scan");
            Check(ConnectionState.QuickCountry("")=="","quick connect retains recommended when selected");
        }
        static async Task RequestTest(string method,object body,int code,string response,bool expectError){
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
            string first="",headers="",payload="";
            var server=Task.Run(async delegate{
                using(var client=await listener.AcceptTcpClientAsync())using(var stream=client.GetStream())using(var reader=new StreamReader(stream,Encoding.UTF8,false,1024,true)){
                    first=await reader.ReadLineAsync();string line;int length=0;
                    while(!String.IsNullOrEmpty(line=await reader.ReadLineAsync())){headers+=line+"\n";if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))length=Int32.Parse(line.Substring(15).Trim());}
                    var buffer=new char[length];int got=0;while(got<length){int n=await reader.ReadAsync(buffer,got,length-got);if(n==0)break;got+=n;}payload=new string(buffer,0,got);
                    string head="HTTP/1.1 "+code+" Test\r\nContent-Type: application/json\r\nContent-Length: "+Encoding.UTF8.GetByteCount(response)+"\r\nConnection: close\r\n"+(code==302?"Location: http://127.0.0.1:1/forbidden\r\n":"")+"\r\n";
                    var bytes=Encoding.UTF8.GetBytes(head+response);await stream.WriteAsync(bytes,0,bytes.Length);
                }
            });
            bool rejected=false;
            try{using(var engine=new Engine("unused","unused","http://127.0.0.1:"+port,"test-local-token")){try{await engine.Api(method,"/api/test",body,5000);}catch(IOException){rejected=true;}}await server;
                Check(first==method+" /api/test HTTP/1.1","HTTP method/path "+method+" "+code);
                Check(headers.IndexOf("X-Local-Token: test-local-token",StringComparison.OrdinalIgnoreCase)>=0,"authenticated loopback header "+code);
                if(method!="GET"){Check(headers.IndexOf("application/json",StringComparison.OrdinalIgnoreCase)>=0,"JSON request content type "+code);Check(Json.Encode(Json.Decode(payload))==Json.Encode(body),"JSON body preserved "+code);}
                Check(rejected==expectError,"HTTP error / redirect handling "+code);
            }finally{listener.Stop();}
        }
    }
}
