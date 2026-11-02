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
    static final String DEFAULT_POOL_URL="https://raw.githubusercontent.com/III85III/III85III-VPN/main/dist/provision/pool.json";

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
                Mobile.setOn(true);
                Mobile.connect();
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
                                if(!em.isEmpty())existingEmails.add(em);
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

    static void ensureAccount(Context context, ProvisionCallback callback){
        CoreRuntime.IO.execute(()->{
            try{
                JSONObject reply=CoreRuntime.request("GET","/api/accounts",null,5000);
                JSONArray accounts=reply!=null?reply.optJSONArray("accounts"):null;
                if(accounts!=null&&accounts.length()>0){
                    if(callback!=null)callback.onSuccess();
                    return;
                }
                addFreshAccount(context, callback);
            }catch(Exception e){
                addFreshAccount(context, callback);
            }
        });
    }

    static void renewAccount(Context context, String oldEmail, String oldId, ProvisionCallback callback){
        CoreRuntime.IO.execute(()->{
            try{
                if(oldEmail!=null && !oldEmail.isEmpty() && context!=null){
                    try{
                        android.content.SharedPreferences prefs=context.getSharedPreferences("vpn_prefs",Context.MODE_PRIVATE);
                        Set<String> ex=new HashSet<>(prefs.getStringSet("exhausted_accounts",Collections.emptySet()));
                        ex.add(oldEmail.toLowerCase(Locale.US));
                        prefs.edit().putStringSet("exhausted_accounts",ex).apply();
                    }catch(Exception ignored){}
                }
                if(oldId!=null && !oldId.isEmpty()){
                    try{CoreRuntime.request("DELETE","/api/accounts/"+oldId,null,5000);}catch(Exception ignored){}
                }
                addFreshAccount(context, callback);
            }catch(Exception e){
                if(callback!=null)callback.onFailure(e.getMessage()!=null?e.getMessage():"Network error");
            }
        });
    }

    static JSONObject fetchAccountFromPool(Context context){ return fetchAccountFromPool(context, (Collection<String>)null); }
    static JSONObject fetchAccountFromPool(Context context, String singleExclude){
        Set<String> s=new HashSet<>();
        if(singleExclude!=null&&!singleExclude.isEmpty())s.add(singleExclude.toLowerCase(Locale.US));
        return fetchAccountFromPool(context, s);
    }
    static JSONObject fetchAccountFromPool(Context context, Collection<String> excludeEmails){
        Set<String> exLower=new HashSet<>();
        if(excludeEmails!=null){
            for(String e:excludeEmails){
                if(e!=null)exLower.add(e.trim().toLowerCase(Locale.US));
            }
        }
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
                        Set<String> exSet = Collections.emptySet();
                        try{
                            android.content.SharedPreferences prefs=context.getSharedPreferences("vpn_prefs",Context.MODE_PRIVATE);
                            exSet=prefs.getStringSet("exhausted_accounts",Collections.emptySet());
                            if(exSet!=null && exSet.size()>=list.length()){
                                prefs.edit().remove("exhausted_accounts").apply();
                                exSet=Collections.emptySet();
                            }
                        }catch(Exception ignored){}
                        List<JSONObject> cand = new ArrayList<>();
                        for(int k=0; k<list.length(); k++){
                            JSONObject item = list.getJSONObject(k);
                            String em = item.optString("email","").trim().toLowerCase(Locale.US);
                            if(!exLower.contains(em) && (exSet==null || !exSet.contains(em))) {
                                cand.add(item);
                            }
                        }
                        if(cand.isEmpty()){
                            for(int k=0; k<list.length(); k++){
                                JSONObject item = list.getJSONObject(k);
                                String em = item.optString("email","").trim().toLowerCase(Locale.US);
                                if(!exLower.contains(em)) cand.add(item);
                            }
                        }
                        if(!cand.isEmpty()){
                            return cand.get(userSeed % cand.size());
                        }
                        return list.getJSONObject(userSeed % list.length());
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

        return null;
    }

    private AccountSetup() {}
}
