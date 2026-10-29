package io.github.iii85iii.vpn;

import android.app.*;
import android.content.*;
import android.net.VpnService;
import android.os.*;
import java.util.concurrent.atomic.AtomicBoolean;
import mobile.Mobile;

public final class TunnelService extends VpnService {
    static final String STOP="io.github.iii85iii.vpn.STOP";
    static volatile boolean active;
    static volatile String error="";
    private final AtomicBoolean stopping=new AtomicBoolean();
    private final AtomicBoolean starting=new AtomicBoolean();
    private final Handler handler=new Handler(Looper.getMainLooper());
    private final Runnable monitor=new Runnable(){public void run(){
        if(active&&!Mobile.isOn()){error="The connection stopped. Open the app to check your account or network.";stopTunnel();}
        else if(active)handler.postDelayed(this,1500);
    }};
    @Override public int onStartCommand(Intent intent,int flags,int startId){
        if(intent!=null&&STOP.equals(intent.getAction())){stopTunnel();return START_NOT_STICKY;}
        if(active||stopping.get()||!starting.compareAndSet(false,true))return START_NOT_STICKY;
        error="";showNotification();
        CoreRuntime.IO.execute(()->{
            try {
                CoreRuntime.ensure(this);
                if(stopping.get())return;
                Mobile.connect();
                Builder builder=new Builder().setSession("III85III VPN").setMtu(1500)
                    .addAddress("172.19.0.1",30).addRoute("0.0.0.0",0).addDnsServer("172.19.0.2")
                    .addAddress("fdfe:dcba:9876::1",126).addRoute("::",0);
                builder.addDisallowedApplication(getPackageName());
                for(String app:getSharedPreferences("interface",MODE_PRIVATE).getStringSet("direct_apps",java.util.Collections.emptySet())){
                    try{builder.addDisallowedApplication(app);}catch(android.content.pm.PackageManager.NameNotFoundException ignored){}
                }
                try(ParcelFileDescriptor device=builder.establish()){
                    if(device==null)throw new IllegalStateException("VPN permission was not granted");
                    int fd=device.detachFd();
                    Mobile.startTun(fd);
                }
                if(stopping.get()){Mobile.stopTun();Mobile.setOn(false);return;}
                active=true;handler.post(monitor);
            } catch(Exception failure){error="Connection could not start. Check your account and try again.";stopTunnel();}
        });
        return START_NOT_STICKY;
    }
    private void showNotification(){
        String channel="vpn";
        NotificationManager manager=getSystemService(NotificationManager.class);
        manager.createNotificationChannel(new NotificationChannel(channel,"VPN connection",NotificationManager.IMPORTANCE_LOW));
        PendingIntent open=PendingIntent.getActivity(this,0,new Intent(this,MainActivity.class),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
        PendingIntent stop=PendingIntent.getService(this,1,new Intent(this,TunnelService.class).setAction(STOP),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
        Notification.Builder notice=new Notification.Builder(this,channel);
        startForeground(85,notice.setSmallIcon(io.github.iii85iii.vpn.R.drawable.ic_notification).setContentTitle("III85III VPN")
            .setContentText("VPN connection active or starting").setContentIntent(open).setOngoing(true)
            .addAction(new Notification.Action.Builder(null,"Disconnect",stop).build()).build());
    }
    private void stopTunnel(){
        if(!stopping.compareAndSet(false,true))return;
        active=false;handler.removeCallbacks(monitor);
        CoreRuntime.IO.execute(()->{Mobile.stopTun();Mobile.setOn(false);handler.post(()->{stopForeground(true);stopSelf();});});
    }
    @Override public void onRevoke(){stopTunnel();super.onRevoke();}
    @Override public void onDestroy(){active=false;handler.removeCallbacks(monitor);Mobile.stopTun();Mobile.setOn(false);super.onDestroy();}
}
