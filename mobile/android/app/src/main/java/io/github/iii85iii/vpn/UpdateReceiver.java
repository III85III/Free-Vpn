package io.github.iii85iii.vpn;

import android.app.ActivityOptions;
import android.content.*;
import android.content.pm.PackageInstaller;
import android.os.Build;

public final class UpdateReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context context,Intent result){
        int status=result.getIntExtra(PackageInstaller.EXTRA_STATUS,PackageInstaller.STATUS_FAILURE);
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
