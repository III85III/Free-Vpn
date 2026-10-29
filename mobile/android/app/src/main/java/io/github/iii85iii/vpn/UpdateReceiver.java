package io.github.iii85iii.vpn;

import android.content.*;
import android.content.pm.PackageInstaller;

public final class UpdateReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context context,Intent result){
        if(result.getIntExtra(PackageInstaller.EXTRA_STATUS,PackageInstaller.STATUS_FAILURE)==PackageInstaller.STATUS_PENDING_USER_ACTION){
            Intent confirmation=result.getParcelableExtra(Intent.EXTRA_INTENT);
            if(confirmation!=null){confirmation.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);context.startActivity(confirmation);}
        }else if(result.getIntExtra(PackageInstaller.EXTRA_STATUS,PackageInstaller.STATUS_FAILURE)!=PackageInstaller.STATUS_SUCCESS){
            android.widget.Toast.makeText(context,"The update was not installed. Your current version is unchanged; you can retry from Settings.",android.widget.Toast.LENGTH_LONG).show();
        }
    }
}
