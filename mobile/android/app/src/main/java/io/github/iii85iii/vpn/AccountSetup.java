package io.github.iii85iii.vpn;

import android.content.Context;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;

final class AccountSetup {
    static final String MESSAGE="Connecting you with an automated 50 GB secure Mozilla profile.";
    static final String DEFAULT_POOL_URL="https://raw.githubusercontent.com/III85III/III85III-VPN/main/dist/provision/pool.json";

    static boolean shouldShow(boolean seen,int accountCount,boolean updateRequired){
        return false;
    }

    interface ProvisionCallback {
        void onSuccess();
        void onFailure(String error);
    }

    static void ensureAccount(Context context, ProvisionCallback callback){
        CoreRuntime.IO.execute(()->{
            try{
                JSONObject reply=CoreRuntime.request("GET","/api/accounts",null,5000);
                JSONArray accounts=reply!=null?reply.optJSONArray("accounts"):null;
                if(accounts!=null&&accounts.length()>0){
                    if(callback!=null)callback.onSuccess();
                    return;
                }

                JSONObject accountToImport=fetchAccountFromPool(context);
                if(accountToImport==null){
                    if(callback!=null)callback.onFailure("No accounts available in pool.");
                    return;
                }

                JSONObject importReply=CoreRuntime.request("POST","/api/accounts/import",accountToImport,20000);
                if(importReply!=null && !importReply.has("error") && (importReply.has("id") || importReply.has("email") || importReply.has("account") || importReply.optBoolean("ok",false))){
                    String accId=importReply.optString("id");
                    if(!accId.isEmpty()){
                        try{
                            JSONObject en=new JSONObject();en.put("enabled",true);
                            CoreRuntime.request("POST","/api/accounts/"+accId+"/enabled",en,5000);
                            CoreRuntime.request("POST","/api/accounts/"+accId+"/activate",null,5000);
                        }catch(Exception ignored){}
                    }
                    if(callback!=null)callback.onSuccess();
                }else{
                    String err=importReply!=null?importReply.optString("error","Account import failed."):"Account import failed.";
                    if(callback!=null)callback.onFailure(err);
                }
            }catch(Exception e){
                if(callback!=null)callback.onFailure(e.getMessage()!=null?e.getMessage():"Network error");
            }
        });
    }

    static void renewAccount(Context context, String oldEmail, String oldId, ProvisionCallback callback){
        CoreRuntime.IO.execute(()->{
            try{
                if(oldId!=null && !oldId.isEmpty()){
                    try{CoreRuntime.request("DELETE","/api/accounts/"+oldId,null,5000);}catch(Exception ignored){}
                }
                JSONObject accountToImport=fetchAccountFromPool(context, oldEmail);
                if(accountToImport==null){
                    if(callback!=null)callback.onFailure("No accounts available in pool.");
                    return;
                }
                JSONObject importReply=CoreRuntime.request("POST","/api/accounts/import",accountToImport,20000);
                if(importReply!=null && !importReply.has("error") && (importReply.has("id") || importReply.has("email") || importReply.has("account") || importReply.optBoolean("ok",false))){
                    String accId=importReply.optString("id");
                    if(!accId.isEmpty()){
                        try{
                            JSONObject en=new JSONObject();en.put("enabled",true);
                            CoreRuntime.request("POST","/api/accounts/"+accId+"/enabled",en,5000);
                            CoreRuntime.request("POST","/api/accounts/"+accId+"/activate",null,5000);
                        }catch(Exception ignored){}
                    }
                    if(callback!=null)callback.onSuccess();
                }else{
                    String err=importReply!=null?importReply.optString("error","Account renewal failed."):"Account renewal failed.";
                    if(callback!=null)callback.onFailure(err);
                }
            }catch(Exception e){
                if(callback!=null)callback.onFailure(e.getMessage()!=null?e.getMessage():"Network error");
            }
        });
    }

    static JSONObject fetchAccountFromPool(Context context){ return fetchAccountFromPool(context, null); }
    static JSONObject fetchAccountFromPool(Context context, String excludeEmail){
        if(context!=null){
            try{
                InputStream is=context.getAssets().open("accounts_pool.json");
                byte[] buffer=new byte[Math.max(131072,is.available())];
                int read=0,count;while(read<buffer.length&&(count=is.read(buffer,read,buffer.length-read))!=-1){read+=count;}


                is.close();
                if(read>0){
                    JSONObject pool=new JSONObject(new String(buffer,0,read,StandardCharsets.UTF_8));
                    JSONArray list=pool.optJSONArray("accounts");
                    if(list!=null&&list.length()>0){
                        int userSeed;
                        try {
                            String devId = android.provider.Settings.Secure.getString(context.getContentResolver(), android.provider.Settings.Secure.ANDROID_ID);
                            userSeed = (devId != null && !devId.isEmpty()) ? Math.abs(devId.hashCode()) : (int)(Math.random()*10000);
                        } catch(Exception ignored){
                            userSeed = (int)(Math.random()*10000);
                        }
                        int index = userSeed % list.length();
                        java.util.List<JSONObject> cand = new java.util.ArrayList<>();
                        for(int k=0; k<list.length(); k++){
                            JSONObject item = list.getJSONObject(k);
                            if(excludeEmail==null || !excludeEmail.equalsIgnoreCase(item.optString("email"))) cand.add(item);
                        }
                        return !cand.isEmpty() ? cand.get(userSeed % cand.size()) : list.getJSONObject(index);
                    }
                }
            }catch(Exception ignored){}
        }

        try{
            JSONObject fallback=new JSONObject();
            fallback.put("email","vpn_d3ccy0in@fldubai.com");
            fallback.put("refresh_token","ee4f532751bea5068a7c7fc8fa18535170e11e07b84a057f4e7223056f87edfd");
            return fallback;
        }catch(Exception ignored){}

        try{
            java.net.URL url=new java.net.URL(DEFAULT_POOL_URL);
            java.net.HttpURLConnection conn=(java.net.HttpURLConnection)url.openConnection();
            conn.setConnectTimeout(2500);
            conn.setReadTimeout(2500);
            if(conn.getResponseCode()==200){
                InputStream in=conn.getInputStream();
                byte[] bytes=new byte[Math.min(1024*1024,in.available()>0?in.available():65536)];
                int total=0,read;
                while((read=in.read(bytes,total,bytes.length-total))>0){
                    total+=read;
                    if(total==bytes.length)break;
                }
                in.close();
                JSONObject pool=new JSONObject(new String(bytes,0,total,StandardCharsets.UTF_8));
                JSONArray list=pool.optJSONArray("accounts");
                if(list!=null&&list.length()>0){
                    int index=(int)(Math.random()*list.length());
                    return list.getJSONObject(index);
                }
            }
        }catch(Exception ignored){}

        return null;
    }

    private AccountSetup() {}
}
