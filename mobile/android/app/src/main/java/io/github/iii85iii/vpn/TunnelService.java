package io.github.iii85iii.vpn;

import android.app.*;
import android.content.*;
import android.net.VpnService;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.net.NetworkRequest;
import android.os.*;
import java.util.concurrent.atomic.AtomicBoolean;
import mobile.Mobile;

public final class TunnelService extends VpnService {
    static final String STOP="io.github.iii85iii.vpn.STOP";
    static volatile boolean active;
    static volatile String error="";
    static final VpnLifecycle lifecycle=new VpnLifecycle();
    private final AtomicBoolean stopping=new AtomicBoolean();
    private boolean observingNetwork;
    private final Object networkLock=new Object();
    private final java.util.Set<Network> networks=java.util.concurrent.ConcurrentHashMap.newKeySet();
    private final ConnectivityManager.NetworkCallback networkListener=new ConnectivityManager.NetworkCallback(){
        @Override public void onAvailable(Network network){networks.add(network);if(lifecycle.canCancel())Mobile.networkChanged(true);}
        @Override public void onLost(Network network){networks.remove(network);if(lifecycle.canCancel())Mobile.networkChanged(!networks.isEmpty());}
    };
    private final Handler handler=new Handler(Looper.getMainLooper());
    private final Runnable monitor=new Runnable(){public void run(){
        if(active&&!Mobile.isOn()){CoreRuntime.IO.execute(()->{String reason="";try{reason=MainActivity.connectionProblem(CoreRuntime.request("GET","/api/status",null,3000));}catch(Exception ignored){}error=reason.isEmpty()?"The connection stopped. Check your account or network.":reason;stopTunnel();});}
        else if(active)handler.postDelayed(this,1500);
    }};
    @Override public int onStartCommand(Intent intent,int flags,int startId){
        if(intent!=null&&STOP.equals(intent.getAction())){stopTunnel();return START_NOT_STICKY;}
        if(UpdateActivity.isRequired(this)){error="Update required.";stopSelf();return START_NOT_STICKY;}
        if(stopping.get()||!lifecycle.start())return START_NOT_STICKY;
        final boolean synthetic=BuildConfig.DEBUG&&intent!=null&&intent.getBooleanExtra("qa_tun",false);
        error="";showNotification();
        CoreRuntime.IO.execute(()->{
            try {
                CoreRuntime.ensure(this);
                if(stopping.get())return;
                synchronized(networkLock){
                    if(stopping.get())return;
                    getSystemService(ConnectivityManager.class).registerNetworkCallback(new NetworkRequest.Builder().addCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET).addCapability(NetworkCapabilities.NET_CAPABILITY_NOT_VPN).build(),networkListener);
                    observingNetwork=true;
                }
                if(!synthetic)Mobile.connect();
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
                synchronized(lifecycle){
                    if(stopping.get()||!lifecycle.activate()){Mobile.stopTun();Mobile.setOn(false);return;}
                    active=true;
                }
                handler.post(()->{if(active&&!stopping.get()){if(synthetic)handler.postDelayed(()->stopTunnel(),20000);else handler.post(monitor);showNotification();}});
            } catch(Exception failure){String detail=failure.getMessage();error="VPN could not start"+(detail==null?". Check your account and retry.":": "+detail.substring(0,Math.min(detail.length(),240)));stopTunnel();}
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
            .setContentText(active?"VPN is active · tap to open":"Starting VPN · tap Disconnect to cancel").setContentIntent(open).setOngoing(true)
            .addAction(new Notification.Action.Builder(null,"Disconnect",stop).build()).build());
    }
    private void stopTunnel(){
        if(!stopping.compareAndSet(false,true))return;
        synchronized(lifecycle){lifecycle.stop();active=false;}
        handler.removeCallbacks(monitor);
        CoreRuntime.IO.execute(()->{Mobile.stopTun();Mobile.setOn(false);handler.post(()->{stopForeground(true);stopSelf();});});
    }
    @Override public void onRevoke(){stopTunnel();super.onRevoke();}
    @Override public void onDestroy(){stopping.set(true);active=false;handler.removeCallbacks(monitor);synchronized(networkLock){if(observingNetwork){try{getSystemService(ConnectivityManager.class).unregisterNetworkCallback(networkListener);}catch(IllegalArgumentException ignored){}observingNetwork=false;}}Mobile.stopTun();Mobile.setOn(false);lifecycle.finished();super.onDestroy();}
}
