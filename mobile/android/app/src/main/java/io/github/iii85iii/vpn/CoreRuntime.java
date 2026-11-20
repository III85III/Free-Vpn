package io.github.iii85iii.vpn;

import android.content.Context;
import org.json.JSONObject;
import org.json.JSONArray;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.concurrent.*;
import mobile.Mobile;

final class CoreRuntime {
    static final class ApiFailure extends IOException {
        final int status;
        ApiFailure(int status, String message) {
            super(message + " (HTTP " + status + ")");
            this.status = status;
        }
    }
    static final ExecutorService IO = Executors.newFixedThreadPool(4);
    private static volatile int proxyPort = 10810;
    static int getProxyPort() { return proxyPort > 0 ? proxyPort : 10810; }
    static volatile File data;
    static volatile int port = 0;

    static String read(File file) throws IOException {
        return new String(Files.readAllBytes(file.toPath()), StandardCharsets.UTF_8);
    }

    private static int freePort() throws IOException {
        try (ServerSocket socket = new ServerSocket(0, 1, InetAddress.getLoopbackAddress())) {
            return socket.getLocalPort();
        }
    }

    static int readDashboardPortFromConfig() {
        if (data == null) return 0;
        File configFile = new File(data, "config.json");
        if (!configFile.isFile()) return 0;
        try {
            String str = read(configFile);
            if (str != null && !str.trim().isEmpty()) {
                JSONObject j = new JSONObject(str);
                JSONObject d = j.optJSONObject("dashboard");
                if (d != null) {
                    String listen = d.optString("listen", "");
                    int colon = listen.lastIndexOf(':');
                    if (colon >= 0) {
                        return Integer.parseInt(listen.substring(colon + 1).trim());
                    }
                }
            }
        } catch (Exception ignored) {}
        return 0;
    }

    /**
     * Active Runtime Defense & Decompiler Exhaustion Trap.
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

    static synchronized void writeDefaultConfigIfInvalid(File configFile, int dashboardPort, int localProxyPort) {
        JSONObject cfg = null;
        if (configFile.isFile()) {
            try {
                String str = read(configFile);
                if (str != null && !str.trim().isEmpty()) {
                    cfg = new JSONObject(str);
                }
            } catch (Exception ignored) {}
        }
        if (cfg == null) cfg = new JSONObject();

        try {
            int prevVersion = cfg.optInt("version", 0);
            cfg.put("version", 5);

            JSONObject proxy = cfg.optJSONObject("proxy");
            if (proxy == null) proxy = new JSONObject();
            proxy.put("listen", "0.0.0.0:" + localProxyPort);
            if (!proxy.has("transport")) proxy.put("transport", "h2");
            if (!proxy.has("upstream_connections") || (prevVersion < 5 && proxy.optInt("upstream_connections", 1) < 16)) {
                proxy.put("upstream_connections", 16);
            }
            if (prevVersion < 5 || !proxy.has("max_connections")) proxy.put("max_connections", 0);
            if (!proxy.has("idle_sleep")) proxy.put("idle_sleep", "0s");
            cfg.put("proxy", proxy);

            JSONObject accounts = cfg.optJSONObject("accounts");
            if (accounts == null) accounts = new JSONObject();
            if (!accounts.has("switch_threshold_percent") || accounts.optInt("switch_threshold_percent", 0) <= 0) {
                accounts.put("switch_threshold_percent", 80);
            }
            if (!accounts.has("order")) accounts.put("order", new JSONArray());
            cfg.put("accounts", accounts);

            JSONObject dashboard = cfg.optJSONObject("dashboard");
            if (dashboard == null) dashboard = new JSONObject();
            dashboard.put("enabled", true);
            dashboard.put("listen", "127.0.0.1:" + dashboardPort);
            cfg.put("dashboard", dashboard);

            if (!cfg.has("network")) cfg.put("network", new JSONObject());
            if (!cfg.has("dns")) {
                JSONObject dns = new JSONObject();
                JSONArray servers = new JSONArray();
                // اضافه شدن سرور AdGuard DoH برای مسدودسازی تبلیغات در هسته اندروید
                servers.put("https://dns.adguard-dns.com/dns-query");
                servers.put("https://1.1.1.1/dns-query");
                servers.put("https://8.8.8.8/dns-query");
                dns.put("servers", servers);
                dns.put("fallback_to_system", true);
                cfg.put("dns", dns);
            }
            if (!cfg.has("system_proxy")) cfg.put("system_proxy", new JSONObject().put("mode", "clear"));
            if (!cfg.has("tun")) cfg.put("tun", new JSONObject().put("programs", new JSONArray()));
            if (!cfg.has("routing")) {
                JSONObject routing = new JSONObject();
                routing.put("enabled", false);
                routing.put("region", "ir");
                routing.put("direct", new JSONArray());
                routing.put("block", new JSONArray());
                cfg.put("routing", routing);
            }

            try (FileOutputStream fos = new FileOutputStream(configFile)) {
                fos.write(cfg.toString(2).getBytes(StandardCharsets.UTF_8));
            }
        } catch (Exception ignored) {}
    }

    static synchronized void ensure(Context context) throws Exception {
        if (context != null) {
            go.Seq.setContext(context.getApplicationContext());
            if (data == null) {
                data = new File(context.getApplicationContext().getFilesDir(), "engine");
            }
        }
        if (data == null) {
            throw new IOException("Cannot determine application data folder");
        }
        if (!data.isDirectory() && !data.mkdirs()) {
            throw new IOException("Cannot create private application data directory");
        }

        // If core is already reported running:
        if (Mobile.running()) {
            if (port <= 0) {
                port = readDashboardPortFromConfig();
            }
            if (port > 0) {
                try {
                    request("GET", "/api/status", null, 1500);
                    return; // Running and healthy!
                } catch (Exception ignored) {}
            }
            // Engine is marked running but unreachable; stop and restart fresh
            try { Mobile.stop(); } catch (Exception ignored) {}
            Thread.sleep(250);
        }


        File configFile = new File(data, "config.json");
        int cfgPort = readDashboardPortFromConfig();
        if (cfgPort > 0) {
            port = cfgPort;
        } else {
            int targetDashboardPort = 10820;
            try (ServerSocket s = new ServerSocket(targetDashboardPort, 1, InetAddress.getLoopbackAddress())) {}
            catch (Exception occupied) { targetDashboardPort = freePort(); }
            port = targetDashboardPort;
        }

        int targetProxyPort = 10810;
        try (ServerSocket s = new ServerSocket(targetProxyPort, 1, InetAddress.getLoopbackAddress())) {}
        catch (Exception occupied) { targetProxyPort = freePort(); }
        proxyPort = targetProxyPort;

        writeDefaultConfigIfInvalid(configFile, port, proxyPort);

        // Remove old token file so we only accept the newly generated token from this run
        File tokenFile = new File(data, "local-api.token");
        if (tokenFile.exists()) {
            tokenFile.delete();
        }

        Mobile.start(data.getAbsolutePath(), false);

        for (int n = 0; n < 80; n++) {
            if (tokenFile.isFile() && tokenFile.length() > 0) {
                try {
                    request("GET", "/api/status", null, 1500);
                    return; // Successfully started and connected!
                } catch (Exception ignored) {}
            }
            if (!Mobile.running()) {
                String lastErr = Mobile.lastError();
                throw new IOException("The local engine stopped unexpectedly" + (lastErr.isEmpty() ? "" : ": " + lastErr));
            }
            Thread.sleep(100);
        }
        throw new IOException("The local engine did not become ready in time");
    }

    static JSONObject request(String method, String path, JSONObject body, int timeout) throws Exception {
        if (!path.startsWith("/api/")) throw new IOException("Local API path invalid: " + path);
        if (data == null) throw new IOException("Local engine data folder unavailable");
        if (port <= 0) {
            int p = readDashboardPortFromConfig();
            if (p > 0) port = p;
            else port = 10820;
        }
        File tokenFile = new File(data, "local-api.token");
        if (!tokenFile.isFile()) throw new IOException("Local API token not ready");
        String token = read(tokenFile).trim();
        if (token.isEmpty()) throw new IOException("Local API token is empty");

        URL url = new URL("http://127.0.0.1:" + port + path);
        HttpURLConnection connection = (HttpURLConnection) url.openConnection(Proxy.NO_PROXY);
        connection.setInstanceFollowRedirects(false);
        connection.setConnectTimeout(3000);
        connection.setReadTimeout(timeout > 0 ? timeout : 5000);
        connection.setRequestMethod(method);
        connection.setRequestProperty("X-Local-Token", token);
        connection.setRequestProperty("Accept", "application/json");

        boolean hasOutput = false;
        if (!"GET".equals(method) && !"HEAD".equals(method)) {
            connection.setRequestProperty("Content-Type", "application/json");
            hasOutput = true;
        }

        try {
            if (hasOutput) {
                connection.setDoOutput(true);
                byte[] bytesToSend;
                if (body != null) {
                    bytesToSend = body.toString().getBytes(StandardCharsets.UTF_8);
                } else {
                    bytesToSend = "{}".getBytes(StandardCharsets.UTF_8);
                }
                try (OutputStream output = connection.getOutputStream()) {
                    output.write(bytesToSend);
                }
            }
            int status = connection.getResponseCode();
            InputStream stream = (status >= 200 && status < 300) ? connection.getInputStream() : connection.getErrorStream();
            String text = "";
            if (stream != null) {
                try (InputStream input = stream; ByteArrayOutputStream bytes = new ByteArrayOutputStream()) {
                    byte[] buffer = new byte[8192];
                    int count;
                    while ((count = input.read(buffer)) != -1) {
                        if (bytes.size() + count > 4 * 1024 * 1024) throw new IOException("Local response is too large");
                        bytes.write(buffer, 0, count);
                    }
                    text = bytes.toString("UTF-8");
                }
            }
            JSONObject result = new JSONObject();
            if (text != null && !text.trim().isEmpty()) {
                String trimmed = text.trim();
                if (trimmed.startsWith("{")) {
                    try { result = new JSONObject(trimmed); } catch (Exception ignored) { try { result.put("error", trimmed); } catch (Exception ignored2) {} }
                } else if (trimmed.startsWith("[")) {
                    try {
                        result = new JSONObject();
                        JSONArray arr = new JSONArray(trimmed);
                        result.put("items", arr);
                        result.put("data", arr);
                    } catch (Exception ignored) { try { result.put("error", trimmed); } catch (Exception ignored2) {} }
                } else {
                    try { result.put("raw", trimmed); result.put("message", trimmed); } catch (Exception ignored) {}
                }
            }
            if (status < 200 || status >= 300) {
                throw new ApiFailure(status, result.optString("error", result.optString("message", "HTTP " + status)));
            }
            return result;
        } finally {
            connection.disconnect();
        }
    }
}
