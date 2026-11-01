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
                JSONArray accounts=reply.optJSONArray("accounts");
                if(accounts!=null&&accounts.length()>0){
                    callback.onSuccess();
                    return;
                }

                JSONObject accountToImport=fetchAccountFromPool(context);
                if(accountToImport==null){
                    callback.onFailure("No accounts available in pool.");
                    return;
                }

                JSONObject importReply=CoreRuntime.request("POST","/api/accounts/import",accountToImport,20000);
                if(importReply.optBoolean("ok",false)||importReply.has("account")){
                    callback.onSuccess();
                }else{
                    callback.onFailure(importReply.optString("error","Account import failed."));
                }
            }catch(Exception e){
                callback.onFailure(e.getMessage()!=null?e.getMessage():"Network error");
            }
        });
    }

    private static JSONObject fetchAccountFromPool(Context context){
        try{
            java.net.URL url=new java.net.URL(DEFAULT_POOL_URL);
            java.net.HttpURLConnection conn=(java.net.HttpURLConnection)url.openConnection();
            conn.setConnectTimeout(3500);
            conn.setReadTimeout(3500);
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

        try{
            InputStream is=context.getAssets().open("accounts_pool.json");
            byte[] buffer=new byte[is.available()];
            int read=is.read(buffer);
            is.close();
            if(read>0){
                JSONObject pool=new JSONObject(new String(buffer,0,read,StandardCharsets.UTF_8));
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
