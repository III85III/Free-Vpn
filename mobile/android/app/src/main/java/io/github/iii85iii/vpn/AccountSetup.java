package io.github.iii85iii.vpn;

import android.content.Context;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.*;
import mobile.Mobile;

final class AccountSetup {
    static final String MESSAGE="Connecting you with an automated 50 GB secure Mozilla profile.";
    static final String DEFAULT_POOL_URL="https://85-accounts.parsaghelich062.workers.dev/v1/account";

    static boolean shouldShow(boolean seen,int accountCount,boolean updateRequired){
        return false;
    }

    interface ProvisionCallback {
        void onSuccess();
        void onFailure(String error);
    }

    static boolean writeAccountDirectly(String email, String refreshToken){
        return writeAccountDirectly(null, email, refreshToken);
    }

    static boolean writeAccountDirectly(Context context, String email, String refreshToken){
        if(CoreRuntime.data==null && context!=null){
            try{CoreRuntime.ensure(context);}catch(Exception ignored){}
        }
        if(CoreRuntime.data==null||email==null||refreshToken==null)return false;
        try{
            String id=UUID.randomUUID().toString().replace("-","").substring(0,12).toLowerCase(Locale.US);
            File accDir=new File(CoreRuntime.data,"accounts/"+id);
            if(!accDir.exists())accDir.mkdirs();

            String now=new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'",Locale.US).format(new Date());
            JSONObject acc=new JSONObject();
            acc.put("id",id);
            acc.put("email",email);
            acc.put("status","ready");
            acc.put("added_at",now);
            try(FileOutputStream fos=new FileOutputStream(new File(accDir,"account.json"))){
                fos.write(acc.toString(2).getBytes(StandardCharsets.UTF_8));
            }

            JSONObject creds=new JSONObject();
            creds.put("access_token","");
            creds.put("refresh_token",refreshToken);
            creds.put("scope","https://identity.mozilla.com/apps/vpn");
            creds.put("obtained_at",now);
            try(FileOutputStream fos=new FileOutputStream(new File(accDir,"credentials.json"))){
                fos.write(creds.toString(2).getBytes(StandardCharsets.UTF_8));
            }

            File cfgFile=new File(CoreRuntime.data,"config.json");
            JSONObject cfg=new JSONObject();
            if(cfgFile.exists()){
                try{
                    String str=CoreRuntime.read(cfgFile);
                    if(str!=null&&!str.trim().isEmpty())cfg=new JSONObject(str);
                }catch(Exception ignored){}
            }
            JSONObject accts=cfg.optJSONObject("accounts");
            if(accts==null)accts=new JSONObject();
            JSONArray order=accts.optJSONArray("order");
            if(order==null)order=new JSONArray();
            boolean has=false;
            for(int i=0;i<order.length();i++){
                Object item=order.opt(i);
                String itemID="";
                if(item instanceof JSONObject){
                    itemID=((JSONObject)item).optString("id");
                }else if(item instanceof String){
                    itemID=(String)item;
                }
                if(id.equals(itemID)){has=true;break;}
            }
            if(!has){
                JSONObject ref=new JSONObject();
                ref.put("id",id);
                ref.put("enabled",true);
                order.put(ref);
            }
            accts.put("order",order);
            cfg.put("accounts",accts);
            try(FileOutputStream fos=new FileOutputStream(cfgFile)){
                fos.write(cfg.toString(2).getBytes(StandardCharsets.UTF_8));
            }

            // Restart daemon so it re-opens accounts directory
            try{
                Mobile.stop();
                Thread.sleep(300);
                if(context!=null)CoreRuntime.ensure(context);
                
            }catch(Exception ignored){}

            try{
                JSONObject en=new JSONObject();en.put("enabled",true);
                CoreRuntime.request("POST","/api/accounts/"+id+"/enabled",en,5000);
                CoreRuntime.request("POST","/api/accounts/"+id+"/activate",null,5000);
                CoreRuntime.request("POST","/api/accounts/"+id+"/refresh",null,10000);
            }catch(Exception ignored){}
            return true;
        }catch(Exception e){
            return false;
        }
    }

    static void addFreshAccount(Context context, ProvisionCallback callback){
        CoreRuntime.IO.execute(()->{
            try{
                Set<String> existingEmails=new HashSet<>();
                try{
                    JSONObject reply=CoreRuntime.request("GET","/api/accounts",null,5000);
                    JSONArray accounts=reply!=null?reply.optJSONArray("accounts"):null;
                    if(accounts!=null){
                        for(int i=0;i<accounts.length();i++){
                            JSONObject acc=accounts.optJSONObject(i);
                            if(acc!=null){
                                String em=acc.optString("email","").trim().toLowerCase(Locale.US);
                                String st=acc.optString("status","");
                                boolean isBad="reauth_required".equals(st)||"exhausted".equals(st)||"renewal_failed".equals(st)||"reserved".equals(st)||"error".equals(st)||st.contains("fail");
                                if(!em.isEmpty()&&!isBad)existingEmails.add(em);
                            }
                        }
                    }
                }catch(Exception ignored){}

                JSONObject accountToImport=fetchAccountFromPool(context, existingEmails);
                if(accountToImport==null){
                    if(callback!=null)callback.onFailure("No accounts available in pool.");
                    return;
                }

                String mail=accountToImport.optString("email");
                String tok=accountToImport.optString("refresh_token");
                boolean imported=false;
                String accId="";
                try{
                    JSONObject importReply=CoreRuntime.request("POST","/api/accounts/import",accountToImport,15000);
                    if(importReply!=null&&!importReply.has("error")&&(importReply.has("id")||importReply.has("email")||importReply.optBoolean("ok",false))){
                        accId=importReply.optString("id");
                        imported=true;
                    }
                }catch(Exception ignored){}

                if(!imported){
                    imported=writeAccountDirectly(context,mail,tok);
                }else if(!accId.isEmpty()){
                    try{
                        JSONObject en=new JSONObject();en.put("enabled",true);
                        CoreRuntime.request("POST","/api/accounts/"+accId+"/enabled",en,5000);
                        CoreRuntime.request("POST","/api/accounts/"+accId+"/activate",null,5000);
                        CoreRuntime.request("POST","/api/accounts/"+accId+"/refresh",null,10000);
                    }catch(Exception ignored){}
                }

                if(imported){
                    if(callback!=null)callback.onSuccess();
                }else{
                    if(callback!=null)callback.onFailure("Account import failed.");
                }
            }catch(Exception e){
                if(callback!=null)callback.onFailure(e.getMessage()!=null?e.getMessage():"Network error");
            }
        });
    }

       static void autoAssignPoolAccount(Context context, Runnable onComplete) {
        CoreRuntime.IO.execute(() -> {
            try {
                JSONObject reply = CoreRuntime.request("GET", "/api/accounts", null, 5000);
                JSONArray accounts = reply != null ? reply.optJSONArray("accounts") : null;
                boolean hasUsable = false;
                Set<String> badIds = new HashSet<>();
                Set<String> existingEmails = new HashSet<>();
                if (accounts != null && accounts.length() > 0) {
                    for (int i = 0; i < accounts.length(); i++) {
                        JSONObject acc = accounts.optJSONObject(i);
                        if (acc != null) {
                            String id = acc.optString("id", "");
                            String em = acc.optString("email", "").trim().toLowerCase(Locale.US);
                            String st = acc.optString("status", "");
                            boolean en = acc.optBoolean("enabled", true);
                            boolean isBad = "reauth_required".equals(st) || "sign in again".equals(st) ||
                                              "exhausted".equals(st) || "error".equals(st) ||
                                              "renewal_failed".equals(st) || st.contains("fail");
                            if (en && !isBad) {
                                hasUsable = true;
                                if (!em.isEmpty()) existingEmails.add(em);
                            } else if (isBad && !id.isEmpty()) {
                                badIds.add(id);
                            }
                        }
                    }
                }
                for (String badId : badIds) {
                    try { CoreRuntime.request("DELETE", "/api/accounts/" + badId, null, 3000); } catch (Exception ignored) {}
                }
                if (hasUsable) {
                    if (onComplete != null) onComplete.run();
                    return;
                }
                JSONObject fresh = fetchAccountFromPool(context, existingEmails);
                if (fresh != null) {
                    String mail = fresh.optString("email");
                    String tok = fresh.optString("refresh_token");
                    boolean imported = false;
                    String accId = "";
                    try {
                        JSONObject importReply = CoreRuntime.request("POST", "/api/accounts/import", fresh, 15000);
                        if (importReply != null && !importReply.has("error") && (importReply.has("id") || importReply.has("email") || importReply.optBoolean("ok", false))) {
                            accId = importReply.optString("id");
                            imported = true;
                        }
                    } catch (Exception ignored) {}
                    if (!imported) {
                        imported = writeAccountDirectly(context, mail, tok);
                    } else if (!accId.isEmpty()) {
                        try {
                            JSONObject en = new JSONObject(); en.put("enabled", true);
                            CoreRuntime.request("POST", "/api/accounts/" + accId + "/enabled", en, 5000);
                            CoreRuntime.request("POST", "/api/accounts/" + accId + "/activate", null, 5000);
                        } catch (Exception ignored) {}
                    }
                }
            } catch (Exception ignored) {
            } finally {
                if (onComplete != null) onComplete.run();
            }
        });
    }

    static void ensureAccount(Context context, ProvisionCallback callback){
        autoAssignPoolAccount(context, () -> {
            if (callback != null) callback.onSuccess();
        });
    }

 static void resetAllAccounts(Context context, ProvisionCallback callback){
        CoreRuntime.IO.execute(()->{
            try {
                if(CoreRuntime.data != null){
                    File accs = new File(CoreRuntime.data, "accounts");
                    if(accs.exists()){
                        File[] list = accs.listFiles();
                        if(list != null){
                            for(File f : list){
                                if(f.isDirectory()){
                                    File[] sub = f.listFiles();
                                    if(sub != null){ for(File s : sub) s.delete(); }
                                    f.delete();
                                } else {
                                    f.delete();
                                }
                            }
                        }
                    }
                    File cfg = new File(CoreRuntime.data, "config.json");
                    if(cfg.exists()){
                        try {
                            String str = CoreRuntime.read(cfg);
                            if(str != null && !str.trim().isEmpty()){
                                JSONObject j = new JSONObject(str);
                                JSONObject ac = j.optJSONObject("accounts");
                                if(ac != null){
                                    ac.put("order", new JSONArray());
                                    j.put("accounts", ac);
                                    try(FileOutputStream fos = new FileOutputStream(cfg)){
                                        fos.write(j.toString(2).getBytes(StandardCharsets.UTF_8));
                                    }
                                }
                            }
                        } catch(Exception ignored){}
                    }
                }
                if(context != null){
                    try {
                        context.getSharedPreferences("vpn_prefs", Context.MODE_PRIVATE).edit()
                            .remove("exhausted_accounts")
                            .apply();
                    } catch(Exception ignored){}
                }
                try {
                    Mobile.stop();
                    Thread.sleep(300);
                    if(context != null) CoreRuntime.ensure(context);
                } catch(Exception ignored){}
                addFreshAccount(context, callback);
            } catch(Exception e){
                if(callback != null) callback.onFailure(e.getMessage() != null ? e.getMessage() : "Reset failed");
            }
        });
    }

    static void renewAccount(Context context, String oldEmail, String oldId, ProvisionCallback callback){
        CoreRuntime.IO.execute(()->{
            try {
                Set<String> exclude = new HashSet<>();
                if(oldEmail != null && !oldEmail.trim().isEmpty()){
                    exclude.add(oldEmail.trim().toLowerCase(Locale.US));
                }
                JSONObject newAcc = fetchAccountFromPool(context, exclude);
                if(newAcc == null){
                    if(callback != null) callback.onFailure("No replacement accounts available in pool.");
                    return;
                }
                String mail = newAcc.optString("email");
                String tok = newAcc.optString("refresh_token");
                boolean imported = false;
                String accId = "";
                try {
                    JSONObject reply = CoreRuntime.request("POST", "/api/accounts/import", newAcc, 15000);
                    if(reply != null && !reply.has("error") && (reply.has("id") || reply.has("email") || reply.optBoolean("ok", false))){
                        accId = reply.optString("id");
                        imported = true;
                    }
                } catch(Exception ignored){}
                if(!imported){
                    imported = writeAccountDirectly(context, mail, tok);
                } else if(!accId.isEmpty()){
                    try {
                        JSONObject en = new JSONObject(); en.put("enabled", true);
                        CoreRuntime.request("POST", "/api/accounts/" + accId + "/enabled", en, 5000);
                        CoreRuntime.request("POST", "/api/accounts/" + accId + "/activate", null, 5000);
                        CoreRuntime.request("POST", "/api/accounts/" + accId + "/refresh", null, 10000);
                    } catch(Exception ignored){}
                }
                if(oldId != null && !oldId.isEmpty() && !oldId.equals(accId)){
                    try {
                        CoreRuntime.request("DELETE", "/api/accounts/" + oldId, null, 5000);
                    } catch(Exception ignored){}
                }
                if(imported){
                    if(callback != null) callback.onSuccess();
                } else {
                    if(callback != null) callback.onFailure("Failed to activate replacement account.");
                }
            } catch(Exception e){
                if(callback != null) callback.onFailure(e.getMessage() != null ? e.getMessage() : "Renewal failed");
            }
        });
    }

    static JSONObject fetchAccountFromPool(Context context){ return fetchAccountFromPool(context, (Collection<String>)null); }
    static JSONObject fetchAccountFromPool(Context context, String exclude){
        return fetchAccountFromPool(context, exclude == null ? null : Collections.singleton(exclude));
    }
    static JSONObject fetchAccountFromPool(Context context, Collection<String> excludeEmails){
        if(context == null) return null;
        java.net.HttpURLConnection connection = null;
        try {
            android.content.SharedPreferences prefs=context.getSharedPreferences("account_allocation",Context.MODE_PRIVATE);
            String credential;
            synchronized(AccountSetup.class){
                credential=prefs.getString("credential", "");
                if(credential.isEmpty()){
                    byte[] bytes=new byte[32]; new java.security.SecureRandom().nextBytes(bytes);
                    StringBuilder hex=new StringBuilder();
                    for(byte b:bytes) hex.append(String.format(Locale.US,"%02x",b & 255));
                    credential=hex.toString();
                    if(!prefs.edit().putString("credential",credential).commit())return null;
                }
            }
            connection=(java.net.HttpURLConnection)new java.net.URL("https://85-accounts.parsaghelich062.workers.dev/v1/account").openConnection();
            connection.setConnectTimeout(3000); connection.setReadTimeout(3000);
            connection.setInstanceFollowRedirects(false);
            connection.setRequestMethod("POST"); connection.setDoOutput(true);
            connection.setRequestProperty("Authorization","Bearer "+credential);
            connection.setRequestProperty("Content-Type","application/json");
            connection.setFixedLengthStreamingMode(2);
            try(java.io.OutputStream output=connection.getOutputStream()){output.write("{}".getBytes(StandardCharsets.UTF_8));}
            if(connection.getResponseCode()!=200) return fetchFromLocalAssetPool(context, excludeEmails);
            java.io.ByteArrayOutputStream data=new java.io.ByteArrayOutputStream();
            try(java.io.InputStream input=connection.getInputStream()){
                byte[] buffer=new byte[2048]; int n;
                while((n=input.read(buffer))!=-1){data.write(buffer,0,n);if(data.size()>32768)return fetchFromLocalAssetPool(context, excludeEmails);}
            }
            JSONObject account=new JSONObject(new String(data.toByteArray(),StandardCharsets.UTF_8));
            String email=account.optString("email","");
            if(email.isEmpty()||account.optString("refresh_token","").isEmpty()) return fetchFromLocalAssetPool(context, excludeEmails);
            if(excludeEmails!=null)for(String excluded:excludeEmails)if(email.equalsIgnoreCase(excluded)) return fetchFromLocalAssetPool(context, excludeEmails);
            return account;
        }catch(Exception ignored){
            return fetchFromLocalAssetPool(context, excludeEmails);
        }
        finally{if(connection!=null)connection.disconnect();}
    }

    private static JSONObject fetchFromLocalAssetPool(Context context, Collection<String> excludeEmails) {
        if(context == null) return null;
        try (java.io.InputStream is = context.getAssets().open("accounts_pool.json")) {
            java.io.ByteArrayOutputStream buffer = new java.io.ByteArrayOutputStream();
            byte[] data = new byte[4096];
            int nRead;
            while ((nRead = is.read(data, 0, data.length)) != -1) {
                buffer.write(data, 0, nRead);
            }
            byte[] raw = buffer.toByteArray();
            if (raw.length >= 3 && raw[0] == (byte)0xEF && raw[1] == (byte)0xBB && raw[2] == (byte)0xBF) {
                raw = java.util.Arrays.copyOfRange(raw, 3, raw.length);
            }
            JSONObject vault = new JSONObject(new String(raw, StandardCharsets.UTF_8));
            String ivB64 = vault.optString("iv", "");
            String ctB64 = vault.optString("payload", "");
            if (ivB64.isEmpty() || ctB64.isEmpty()) return null;

            byte[] key = "III85III-Secure-Vault-2026-KEY85".getBytes(StandardCharsets.UTF_8);
            byte[] iv = android.util.Base64.decode(ivB64, android.util.Base64.DEFAULT);
            byte[] ct = android.util.Base64.decode(ctB64, android.util.Base64.DEFAULT);

            javax.crypto.Cipher cipher = javax.crypto.Cipher.getInstance("AES/CBC/PKCS5Padding");
            cipher.init(javax.crypto.Cipher.DECRYPT_MODE, new javax.crypto.spec.SecretKeySpec(key, "AES"), new javax.crypto.spec.IvParameterSpec(iv));
            byte[] plain = cipher.doFinal(ct);

            JSONObject pool = new JSONObject(new String(plain, StandardCharsets.UTF_8));
            org.json.JSONArray accounts = pool.optJSONArray("accounts");
            if (accounts == null || accounts.length() == 0) return null;

            int len = accounts.length();
            int start = new java.util.Random().nextInt(len);
            for (int i = 0; i < len; i++) {
                JSONObject acc = accounts.getJSONObject((start + i) % len);
                String email = acc.optString("email", "");
                String tok = acc.optString("refresh_token", "");
                if (email.isEmpty() || tok.isEmpty()) continue;
                if (excludeEmails != null && excludeEmails.contains(email)) continue;
                return acc;
            }
        } catch (Exception e) {
            android.util.Log.e("AccountSetup", "Local pool error: " + e.getMessage(), e);
        }
        return null;
    }

    private AccountSetup() {}
}
