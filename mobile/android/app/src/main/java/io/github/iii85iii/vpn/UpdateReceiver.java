package io.github.iii85iii.vpn;

import android.app.ActivityOptions;
import android.content.*;
import android.content.pm.PackageInstaller;
import android.os.Build;

public final class UpdateReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context context,Intent result){
        if(result!=null){
            String action=result.getAction();
            if(Intent.ACTION_BOOT_COMPLETED.equals(action)
                || Intent.ACTION_MY_PACKAGE_REPLACED.equals(action)
                || "android.intent.action.QUICKBOOT_POWERON".equals(action)
                || "com.htc.intent.action.QUICKBOOT_POWERON".equals(action)){
                try{
                    Intent service=new Intent(context,TunnelService.class).setAction(TunnelService.START_PROXY);
                    if(Build.VERSION.SDK_INT>=26){context.startForegroundService(service);}else{context.startService(service);}
                }catch(Exception ignored){}
                return;
            }
        }
        int status=result!=null?result.getIntExtra(PackageInstaller.EXTRA_STATUS,PackageInstaller.STATUS_FAILURE):PackageInstaller.STATUS_FAILURE;
        if(status==PackageInstaller.STATUS_PENDING_USER_ACTION){
            Intent confirmation=Build.VERSION.SDK_INT>=33
                ? result.getParcelableExtra(Intent.EXTRA_INTENT,Intent.class)
                : (Intent)result.getParcelableExtra(Intent.EXTRA_INTENT);
            if(confirmation!=null){
                confirmation.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                try{
                    if(Build.VERSION.SDK_INT>=34){
                        ActivityOptions options=ActivityOptions.makeBasic();
                        options.setPendingIntentBackgroundActivityStartMode(ActivityOptions.MODE_BACKGROUND_ACTIVITY_START_ALLOWED);
                        context.startActivity(confirmation,options.toBundle());
                    }else{
                        context.startActivity(confirmation);
                    }
                }catch(Exception error){
                    try{context.startActivity(confirmation);}catch(Exception ignored){}
                }
            }
        }else if(status!=PackageInstaller.STATUS_SUCCESS){
            android.widget.Toast.makeText(context,"The update was not installed. Your current version is unchanged; you can retry from Settings.",android.widget.Toast.LENGTH_LONG).show();
        }
    }
}
