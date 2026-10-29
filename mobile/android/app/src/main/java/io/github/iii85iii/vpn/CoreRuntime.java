package io.github.iii85iii.vpn;

import android.content.Context;
import org.json.JSONObject;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.concurrent.*;
import mobile.Mobile;

final class CoreRuntime {
    static final class ApiFailure extends IOException {
        final int status;
        ApiFailure(int status,String message){super(message+" (HTTP "+status+")");this.status=status;}
    }
    static final ExecutorService IO = Executors.newFixedThreadPool(4);
    private static File data;
    private static int port;
    private static String read(File file) throws IOException { return new String(Files.readAllBytes(file.toPath()), StandardCharsets.UTF_8); }
    private static int freePort() throws IOException { try(ServerSocket socket=new ServerSocket(0, 1, InetAddress.getLoopbackAddress())) { return socket.getLocalPort(); } }
    static synchronized void ensure(Context context) throws Exception {
        go.Seq.setContext(context.getApplicationContext());
        if(Mobile.running()) return;
        data=new File(context.getFilesDir(), "engine");
        if(!data.isDirectory()&&!data.mkdirs()) throw new IOException("Cannot create private application data");
        File configFile=new File(data,"config.json");
        JSONObject config=configFile.isFile()?new JSONObject(read(configFile)):new JSONObject();
        JSONObject dashboard=config.optJSONObject("dashboard");if(dashboard==null)dashboard=new JSONObject();
        port=freePort();dashboard.put("enabled",true).put("listen","127.0.0.1:"+port);
        config.put("dashboard",dashboard);
        JSONObject proxy=config.optJSONObject("proxy");if(proxy==null)proxy=new JSONObject();
        proxy.put("listen","127.0.0.1:"+freePort());config.put("proxy",proxy);
        config.put("system_proxy",new JSONObject().put("mode","clear"));
        config.put("routing",new JSONObject().put("enabled",false));
        try(FileOutputStream output=new FileOutputStream(configFile)){output.write(config.toString().getBytes(StandardCharsets.UTF_8));}
        Mobile.start(data.getAbsolutePath(),false);
        for(int n=0;n<80;n++) {
            if(new File(data,"local-api.token").isFile()) {
                try{request("GET","/api/status",null,2000);return;}catch(Exception ignored){}
            }
            if(!Mobile.running())throw new IOException("The local engine could not start");
            Thread.sleep(100);
        }
        throw new IOException("The local engine did not become ready");
    }
    static JSONObject request(String method,String path,JSONObject body,int timeout) throws Exception {
        if(!path.startsWith("/api/")||data==null)throw new IOException("Local API unavailable");
        HttpURLConnection connection=(HttpURLConnection)new URL("http://127.0.0.1:"+port+path).openConnection(Proxy.NO_PROXY);
        connection.setInstanceFollowRedirects(false);connection.setConnectTimeout(3000);connection.setReadTimeout(timeout);
        connection.setRequestMethod(method);connection.setRequestProperty("X-Local-Token",read(new File(data,"local-api.token")).trim());
        try {
            if(body!=null){connection.setDoOutput(true);connection.setRequestProperty("Content-Type","application/json");try(OutputStream output=connection.getOutputStream()){output.write(body.toString().getBytes(StandardCharsets.UTF_8));}}
            int status=connection.getResponseCode();
            InputStream stream=status>=200&&status<300?connection.getInputStream():connection.getErrorStream();
            String text="";
            if(stream!=null)try(InputStream input=stream;ByteArrayOutputStream bytes=new ByteArrayOutputStream()){
                byte[] buffer=new byte[8192];int count;
                while((count=input.read(buffer))!=-1){if(bytes.size()+count>4*1024*1024)throw new IOException("Local response is too large");bytes.write(buffer,0,count);}
                text=bytes.toString("UTF-8");
            }
            JSONObject result=text.isEmpty()?new JSONObject():new JSONObject(text);
            if(status<200||status>=300)throw new ApiFailure(status,result.optString("error","The request could not finish. Try again."));
            return result;
        } finally {connection.disconnect();}
    }
}
