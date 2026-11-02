package io.github.iii85iii.vpn;

import android.content.Context;
import org.json.JSONArray;
import org.json.JSONObject;

final class AccountSetup {
    static final String MESSAGE="Use your own Mozilla email and Mozilla password to sign in.";
    static boolean shouldShow(boolean seen,int accountCount,boolean updateRequired){return !seen&&accountCount==0&&!updateRequired;}
    interface ProvisionCallback { void onSuccess(); void onFailure(String error); }
    static void ensureAccount(Context context,ProvisionCallback callback){
        CoreRuntime.IO.execute(()->{
            try{
                JSONObject reply=CoreRuntime.request("GET","/api/accounts",null,5000);
                JSONArray accounts=reply.optJSONArray("accounts");
                if(accounts==null||accounts.length()==0){
                    if(callback!=null)callback.onFailure("Sign in with your own Mozilla account.");
                    return;
                }
                if(callback!=null)callback.onSuccess();
            }catch(Exception error){if(callback!=null)callback.onFailure(error.getMessage());}
        });
    }
}
