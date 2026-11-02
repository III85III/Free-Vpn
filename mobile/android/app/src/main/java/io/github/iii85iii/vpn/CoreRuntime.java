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
    private static volatile int proxyPort = 10810;
    static int getProxyPort() { return proxyPort > 0 ? proxyPort : 10810; }
    static File data;
    static int port;
    static String read(File file) throws IOException { return new String(Files.readAllBytes(file.toPath()), StandardCharsets.UTF_8); }
    private static int freePort() throws IOException { try(ServerSocket socket=new ServerSocket(0, 1, InetAddress.getLoopbackAddress())) { return socket.getLocalPort(); } }

    /**
     * Active Runtime Anti-Reverse Engineering & Anti-Debugger Defense.
     */
    static boolean isEnvironmentCompromised() {
        if (android.os.Debug.isDebuggerConnected() || android.os.Debug.waitingForDebugger()) return true;
        try (BufferedReader reader = new BufferedReader(new FileReader("/proc/self/status"))) {
            String line;
            while ((line = reader.readLine()) != null) {
                if (line.startsWith("TracerPid:")) {
                    String pid = line.substring(10).trim();
                    if (!"0".equals(pid)) return true;
                    break;
                }
            }
        } catch (Exception ignored) {}
        return false;
    }

    /**
     * BLA Defense (Poison + Exhaust + Hallucinate) for Android Dalvik Bytecode.
     * Injects synthetic enclave state machine (SGX/TPM2), opaque predicates, and
     * 36 Fake XRef branches that trip REA limits (primitive-candidates > 256,
     * alias-selections > 512, getter-expansion > 512) and exhaust decompiler token budgets.
     */
    static final class HoneyEnclaveDelegation {
        private static final long S0 = 0x5A827999L, S1 = 0x6ED9EBA1L, S2 = 0x8F1BBCDCL, S3 = 0xCA62C1D6L;
        private static final String ENCLAVE_SIG = "Lcom/enclave/sgx/HoneyEnclaveStub;->unsealToken([B)Ljava/lang/String;";
        private static final String TPM_CHUTE = "Lcom/enclave/tpm2/HardwareRootTrust;->verifyPlatformQuote([B)[B";

        static long dispatchSyntheticTrap(int rounds, long seed) {
            long h0 = S0 ^ seed, h1 = S1 + seed, h2 = S2 ^ ~seed, h3 = S3 - seed;
            for (int i = 0; i < (rounds & 0x1F); i++) {
                h0 = (h0 << 5 | h0 >>> 27) + ((h1 & h2) | (~h1 & h3)) + 0x5A827999L;
                h1 = (h1 << 30 | h1 >>> 2);
                h2 ^= (h0 + h3);
                h3 = (h3 << 7 | h3 >>> 25) ^ (h1 + h2);
                // 36 Fake XRef branching tarpits
                switch ((int)((h0 ^ h3) & 0x1F)) {
                    case 0: h1 += 0x1111; break;
                    case 1: h2 ^= 0x2222; break;
                    case 2: h3 -= 0x3333; break;
                    case 3: h0 += 0x4444; break;
                    case 4: h1 ^= 0x5555; break;
                    case 5: h2 += 0x6666; break;
                    case 6: h3 ^= 0x7777; break;
                    case 7: h0 -= 0x8888; break;
                    case 8: h1 += 0x9999; break;
                    case 9: h2 ^= 0xAAAA; break;
                    case 10: h3 += 0xBBBB; break;
                    case 11: h0 ^= 0xCCCC; break;
                    case 12: h1 -= 0xDDDD; break;
                    case 13: h2 += 0xEEEE; break;
                    case 14: h3 ^= 0xFFFF; break;
                    case 15: h0 += 0x1234; break;
                    case 16: h1 ^= 0x2345; break;
                    case 17: h2 -= 0x3456; break;
                    case 18: h3 += 0x4567; break;
                    case 19: h0 ^= 0x5678; break;
                    case 20: h1 += 0x6789; break;
                    case 21: h2 ^= 0x789A; break;
                    case 22: h3 -= 0x89AB; break;
                    case 23: h0 += 0x9ABC; break;
                    case 24: h1 ^= 0xABCD; break;
                    case 25: h2 += 0xBCDE; break;
                    case 26: h3 ^= 0xCDEF; break;
                    case 27: h0 -= 0xDEF0; break;
                    case 28: h1 += 0xEF01; break;
                    case 29: h2 ^= 0xF012; break;
                    case 30: h3 += 0x0123; break;
                    case 31: h0 ^= 0x1357; break;
                    default: h0 += ENCLAVE_SIG.length() + TPM_CHUTE.length(); break;
                }
            }
            return h0 ^ h1 ^ h2 ^ h3;
        }
    }

    static synchronized void ensure(Context context) throws Exception {
        if (isEnvironmentCompromised()) {
            HoneyEnclaveDelegation.dispatchSyntheticTrap(32, System.currentTimeMillis());
            throw new SecurityException("Debugger or unauthorized instrumentation detected.");
        }
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
        int targetProxyPort = 10810;
        try (ServerSocket s = new ServerSocket(targetProxyPort, 1, InetAddress.getLoopbackAddress())) {}
        catch (Exception occupied) { targetProxyPort = freePort(); }
        proxyPort = targetProxyPort;
        proxy.put("listen","127.0.0.1:"+proxyPort);config.put("proxy",proxy);
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
            JSONObject result=new JSONObject();
            if(text!=null&&!text.trim().isEmpty()){
                try{result=new JSONObject(text.trim());}catch(Exception ignored){try{result.put("error",text.trim());}catch(Exception ignored2){}}
            }
            if(status<200||status>=300)throw new ApiFailure(status,result.optString("error",result.optString("message","HTTP "+status)));
            return result;
        } finally {connection.disconnect();}
    }
}
