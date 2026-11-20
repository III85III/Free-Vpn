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
import org.json.JSONObject;

public final class TunnelService extends VpnService {
    static final String STOP="io.github.iii85iii.vpn.STOP";
    static final String START_PROXY="io.github.iii85iii.vpn.START_PROXY";
    static final String STOP_PROXY="io.github.iii85iii.vpn.STOP_PROXY";
    static volatile boolean active;
    static volatile boolean proxyActive;
    static volatile String error="";
    static volatile String startedAt="";
    static final VpnLifecycle lifecycle=new VpnLifecycle();
    private final AtomicBoolean stopping=new AtomicBoolean();
    private boolean observingNetwork;
    private final Object networkLock=new Object();
    private final java.util.Set<Network> networks=java.util.concurrent.ConcurrentHashMap.newKeySet();
    private final Handler handler=new Handler(Looper.getMainLooper());
    private final Runnable reportNetworkChange = () -> {
        if (!stopping.get() && lifecycle.canCancel()) Mobile.networkChanged(!networks.isEmpty());
    };
    private final ConnectivityManager.NetworkCallback networkListener=new ConnectivityManager.NetworkCallback(){
        @Override public void onAvailable(Network network){if (networks.add(network)) scheduleNetworkChange();}
        @Override public void onLost(Network network){if (networks.remove(network)) scheduleNetworkChange();}
    };
    private void scheduleNetworkChange() {
        handler.removeCallbacks(reportNetworkChange);
        handler.postDelayed(reportNetworkChange, 400);
    }
    private int offStreak=0;
    private boolean verified;
    private boolean probing;
    private long nextProbeAt;

    private void probeIfNeeded() {
        if (probing || SystemClock.elapsedRealtime() < nextProbeAt) return;
        probing = true;
        nextProbeAt = SystemClock.elapsedRealtime() + 30000;
        CoreRuntime.IO.execute(() -> {
            try {
                if (!stopping.get() && active) CoreRuntime.request("POST", "/api/test", new JSONObject(), 35000);
            } catch (Exception ignored) {
                // The next status poll reads the engine's result or retries on schedule.
            } finally {
                handler.post(() -> probing = false);
            }
        });
    }

    private final Runnable monitor=new Runnable(){public void run(){
        if(!active || stopping.get()) return;
        offStreak = Mobile.isOn() ? 0 : offStreak + 1;
        final boolean coreStopped = offStreak >= 3;
        CoreRuntime.IO.execute(()->{
            String reason = "";
            boolean signInRequired = false;
            boolean checked = false;
            boolean reachable = false;
            try {
                JSONObject status = CoreRuntime.request("GET", "/api/status", null, 2500);
                reason = MainActivity.connectionProblem(status);
                signInRequired = MainActivity.needsSignIn(status);
                reachable = reason.isEmpty() && ConnectionTruth.verified(active, status, startedAt);
                checked = true;
            } catch(Exception ignored) {}
            final String detail = reason;
            final boolean stop = signInRequired || coreStopped;
            final boolean statusRead = checked;
            final boolean trafficVerified = reachable;
            handler.post(()->{
                if(!active || stopping.get()) return;
                if(stop) {
                    error = detail.isEmpty() ? "The connection stopped. Check your network and retry." : detail;
                    proxyActive = false;
                    stopTunnel();
                } else {
                    if (statusRead) {
                        if (verified != trafficVerified) {
                            verified = trafficVerified;
                            showNotification();
                        }
                        if (!verified) probeIfNeeded();
                    }
                    handler.postDelayed(this, 3000);
                }
            });
        });
    }};
    @Override public int onStartCommand(Intent intent,int flags,int startId){
        if(intent!=null&&STOP.equals(intent.getAction())){getSharedPreferences("vpn_prefs",MODE_PRIVATE).edit().putString("service_mode","off").apply();proxyActive=false;stopTunnel();return START_NOT_STICKY;}
        if(intent!=null&&STOP_PROXY.equals(intent.getAction())){getSharedPreferences("vpn_prefs",MODE_PRIVATE).edit().putString("service_mode","off").apply();stopProxy();return START_NOT_STICKY;}
        if(intent!=null&&START_PROXY.equals(intent.getAction())){getSharedPreferences("vpn_prefs",MODE_PRIVATE).edit().putString("service_mode","proxy").apply();startProxy();return START_STICKY;}
        if(intent==null){
            String mode=getSharedPreferences("vpn_prefs",MODE_PRIVATE).getString("service_mode","off");
            if("proxy".equals(mode)){startProxy();return START_STICKY;}
            if(!"vpn".equals(mode)){return START_NOT_STICKY;}
        }
        if(UpdateActivity.isRequired(this)){error="Update required.";stopSelf();return START_NOT_STICKY;}
        if(stopping.get()||!lifecycle.start())return START_NOT_STICKY;
        getSharedPreferences("vpn_prefs",MODE_PRIVATE).edit().putString("service_mode","vpn").apply();
        offStreak=0;
        nextProbeAt=0;
        verified=false;
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
                android.content.SharedPreferences emPrefs = getSharedPreferences("vpn_prefs", MODE_PRIVATE);
                int mtu = emPrefs.getBoolean("emergency_smart_mtu", false) ? 1280 : 1420;
                Builder builder=new Builder().setSession("ONYX // VPN").setMtu(mtu)
                    .addAddress("172.19.0.1",30).addRoute("0.0.0.0",0);
                if (emPrefs.getBoolean("emergency_secure_dns", false)) {
                    // دی‌ان‌اس AdGuard با مسدودسازی دائمی تبلیغات برای حالت Secure DNS
                    builder.addDnsServer("94.140.14.14").addDnsServer("94.140.15.15").addDnsServer("1.1.1.1");
                } else {
                    builder.addDnsServer("172.19.0.2");
                }
                builder.addAddress("fdfe:dcba:9876::1",126).addRoute("::",0);
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
                    startedAt=java.time.Instant.now().toString();
                    active=true;
                }
                handler.post(()->{if(active&&!stopping.get()){if(synthetic)handler.postDelayed(()->stopTunnel(),20000);else handler.post(monitor);showNotification();}});
            } catch(Exception failure){String detail=failure.getMessage();error="VPN could not start"+(detail==null?". Check your account and retry.":": "+detail.substring(0,Math.min(detail.length(),240)));stopTunnel();}
        });
        return START_STICKY;
    }
    private void showNotification(){
        String channel="vpn";
        NotificationManager manager=getSystemService(NotificationManager.class);
        manager.createNotificationChannel(new NotificationChannel(channel,"VPN connection",NotificationManager.IMPORTANCE_LOW));
        PendingIntent open=PendingIntent.getActivity(this,0,new Intent(this,MainActivity.class),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
        PendingIntent stop=PendingIntent.getService(this,1,new Intent(this,TunnelService.class).setAction(STOP),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
        Notification.Builder notice=new Notification.Builder(this,channel);
        startForeground(85,notice.setSmallIcon(io.github.iii85iii.vpn.R.drawable.ic_notification).setContentTitle("ONYX // VPN")
            .setContentText(verified?"VPN connected":"Checking the VPN connection\u2026").setContentIntent(open).setOngoing(true)
            .addAction(new Notification.Action.Builder(null,"Disconnect",stop).build()).build());
    }
    private void startProxy(){
        proxyActive=true;
        showProxyNotification();
        CoreRuntime.IO.execute(()->{
            try {
                CoreRuntime.ensure(this);
                AccountSetup.ensureAccount(this,null);
                Mobile.setOn(true);
                Mobile.connect();
                handler.post(()->{if(proxyActive)showProxyNotification();});
            } catch(Exception ignored){}
        });
    }
    private void stopProxy(){
        getSharedPreferences("vpn_prefs",MODE_PRIVATE).edit().putString("service_mode","off").apply();
        proxyActive=false;
        if(!active){
            CoreRuntime.IO.execute(()->{try{Mobile.setOn(false);}catch(Exception ignored){}});
            stopForeground(true);
            stopSelf();
        }
    }
    private void showProxyNotification(){
        String channel="proxy";
        NotificationManager manager=getSystemService(NotificationManager.class);
        if(manager!=null){
            manager.createNotificationChannel(new NotificationChannel(channel,"Telegram Proxy",NotificationManager.IMPORTANCE_LOW));
            PendingIntent open=PendingIntent.getActivity(this,0,new Intent(this,MainActivity.class),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
            PendingIntent stop=PendingIntent.getService(this,2,new Intent(this,TunnelService.class).setAction(STOP_PROXY),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
            Notification.Builder notice=new Notification.Builder(this,channel);
            startForeground(86,notice.setSmallIcon(io.github.iii85iii.vpn.R.drawable.ic_notification)
                .setContentTitle("ONYX // VPN • Telegram Proxy")
                .setContentText("پروکسی تلگرام در پس‌زمینه فعال است")
                .setContentIntent(open).setOngoing(true)
                .addAction(new Notification.Action.Builder(null,"خاموش کردن پروکسی",stop).build()).build());
        }
    }
    private void stopTunnel(){
        getSharedPreferences("vpn_prefs",MODE_PRIVATE).edit().putString("service_mode","off").apply();
        if(!stopping.compareAndSet(false,true))return;
        synchronized(lifecycle){lifecycle.stop();active=false;}
        handler.removeCallbacks(monitor);
        handler.removeCallbacks(reportNetworkChange);
        if(proxyActive){
            CoreRuntime.IO.execute(()->{
                Mobile.stopTun();
                stopping.set(false);
                handler.post(this::showProxyNotification);
            });
            return;
        }
        CoreRuntime.IO.execute(()->{Mobile.stopTun();Mobile.setOn(false);handler.post(()->{stopForeground(true);stopSelf();});});
    }
    @Override public void onRevoke(){stopTunnel();super.onRevoke();}
    @Override public void onDestroy(){stopping.set(true);active=false;handler.removeCallbacks(monitor);handler.removeCallbacks(reportNetworkChange);synchronized(networkLock){if(observingNetwork){try{getSystemService(ConnectivityManager.class).unregisterNetworkCallback(networkListener);}catch(IllegalArgumentException ignored){}observingNetwork=false;}}Mobile.stopTun();Mobile.setOn(false);lifecycle.finished();super.onDestroy();}
}
