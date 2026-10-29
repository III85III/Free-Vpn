package io.github.iii85iii.vpn;

import android.app.*;
import android.content.*;
import android.content.pm.*;
import android.net.Uri;
import android.os.*;
import android.provider.Settings;
import android.widget.*;
import org.json.JSONObject;
import java.io.*;
import java.net.*;
import java.security.MessageDigest;
import java.util.*;

public final class UpdateActivity extends Activity {
    private TextView status;private ProgressBar progress;private Button action;
    private Info available;private File downloaded;private boolean busy;
    static final class Info {String version,url,hash;long size,code;}
    @Override public void onCreate(Bundle state){
        super.onCreate(state);LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);root.setBackgroundColor(0xff101319);int pad=(int)(24*getResources().getDisplayMetrics().density);root.setPadding(pad,pad,pad,pad);
        Button back=new Button(this);back.setText("Back to settings");back.setAllCaps(false);back.setOnClickListener(v->finish());root.addView(back);
        status=new TextView(this);status.setTextColor(0xffefefea);status.setTextSize(18);status.setPadding(0,pad,0,pad);root.addView(status);
        progress=new ProgressBar(this,null,android.R.attr.progressBarStyleHorizontal);progress.setMax(100);root.addView(progress);
        action=new Button(this);action.setAllCaps(false);action.setText("Check for updates");action.setOnClickListener(v->{if(downloaded!=null)install();else if(available!=null)download();else check();});root.addView(action);
        TextView note=new TextView(this);note.setTextColor(0xff9fa7b4);note.setText("Updates come from the official III85III GitHub repository. Downloads are checked before Android asks you to install. Your local accounts stay on this device. The VPN disconnects while the app updates.");note.setPadding(0,pad,0,0);root.addView(note);setContentView(root);WindowLayout.apply(this,root);
        if(BuildConfig.DEBUG&&getIntent().getBooleanExtra("qa_update",false)){
            action.setEnabled(false);status.setText("Preparing synthetic update test…");CoreRuntime.IO.execute(()->{try{File file=new File(getCacheDir(),"qa-upgrade.apk");PackageInfo candidate=getPackageManager().getPackageArchiveInfo(file.getAbsolutePath(),0);if(candidate==null)throw new IOException("Missing test APK");Info info=new Info();info.code=Build.VERSION.SDK_INT>=28?candidate.getLongVersionCode():candidate.versionCode;info.version=candidate.versionName;info.size=file.length();info.hash=digest(file);verify(file,info);available=info;downloaded=file;runOnUiThread(()->{if(!isDestroyed()){status.setText("Synthetic update verified.");action.setText("Install update");action.setEnabled(true);}});}catch(Exception error){runOnUiThread(()->status.setText("Synthetic update rejected."));}});
        }else check();
    }
    private void check(){if(busy)return;busy=true;action.setEnabled(false);status.setText("Checking for updates…");progress.setIndeterminate(true);
        CoreRuntime.IO.execute(()->{try{Info update=latest();runOnUiThread(()->{if(isDestroyed())return;available=update.code>BuildConfig.VERSION_CODE?update:null;status.setText(available==null?"You're using the latest version.":"III85III VPN "+update.version+" is available.");action.setText(available==null?"Check again":"Download update");finishWork();});}catch(Exception error){runOnUiThread(()->{if(!isDestroyed()){status.setText("Updates are unavailable right now. Check your connection and try again.");finishWork();}});}});
    }
    private void finishWork(){busy=false;progress.setIndeterminate(false);action.setEnabled(true);}
    static HttpURLConnection open(String address)throws Exception{
        URI uri=new URI(address);
        Proxy route=Proxy.NO_PROXY;
        try{if(mobile.Mobile.isOn()){String endpoint=mobile.Mobile.proxyAddress();URI local=new URI("http://"+endpoint);if("127.0.0.1".equals(local.getHost())&&local.getPort()>0)route=new Proxy(Proxy.Type.HTTP,new InetSocketAddress("127.0.0.1",local.getPort()));}}catch(Exception ignored){}
        for(int attempt=0;attempt<6;attempt++){
            if(!UpdatePolicy.trustedRedirect(uri))throw new IOException("Untrusted update host");
            HttpURLConnection request=(HttpURLConnection)uri.toURL().openConnection(route);request.setInstanceFollowRedirects(false);request.setConnectTimeout(15000);request.setReadTimeout(30000);request.setRequestProperty("User-Agent","III85III-VPN/"+BuildConfig.VERSION_NAME);
            int code=request.getResponseCode();if(code>=300&&code<400){String target=request.getHeaderField("Location");request.disconnect();if(target==null)throw new IOException("Missing update location");uri=uri.resolve(target);continue;}
            if(code!=200){request.disconnect();throw new IOException("Update unavailable");}return request;
        }throw new IOException("Too many update redirects");
    }
    static Info latest()throws Exception{
        HttpURLConnection request=open(UpdatePolicy.FEED);
        try(InputStream stream=request.getInputStream();ByteArrayOutputStream bytes=new ByteArrayOutputStream()){
            byte[] buffer=new byte[4096];int count;while((count=stream.read(buffer))!=-1){if(bytes.size()+count>32768)throw new IOException("Update metadata too large");bytes.write(buffer,0,count);}
            JSONObject document=new JSONObject(bytes.toString("UTF-8"));if(document.optInt("schema")!=1)throw new IOException("Unknown update format");JSONObject item=document.getJSONObject("android");Info info=new Info();info.version=item.getString("version");info.url=item.getString("url");info.hash=item.getString("sha256");info.size=item.getLong("size");info.code=item.getLong("version_code");UpdatePolicy.validate(info.url,info.hash,info.size,info.code);return info;
        }finally{request.disconnect();}
    }
    static void notifyAvailable(Activity owner){
        android.content.SharedPreferences prefs=owner.getSharedPreferences("interface",MODE_PRIVATE);long now=System.currentTimeMillis();if(now-prefs.getLong("update_checked",0)<6*60*60*1000L)return;prefs.edit().putLong("update_checked",now).apply();
        CoreRuntime.IO.execute(()->{try{Info update=latest();if(update.code>BuildConfig.VERSION_CODE)owner.runOnUiThread(()->{if(owner.isDestroyed()||owner.isFinishing()||!owner.hasWindowFocus())return;new AlertDialog.Builder(owner).setTitle("Update available").setMessage("III85III VPN "+update.version+" is ready. Download and install it from this app.").setNegativeButton("Later",null).setPositiveButton("View update",(d,w)->owner.startActivity(new Intent(owner,UpdateActivity.class))).show();});}catch(Exception ignored){}});
    }
    private void download(){if(busy||available==null)return;busy=true;action.setEnabled(false);status.setText("Downloading update…");Info update=available;
        CoreRuntime.IO.execute(()->{File partial=new File(getCacheDir(),"update.part");try{
            HttpURLConnection request=open(update.url);
            try(InputStream stream=request.getInputStream();FileOutputStream output=new FileOutputStream(partial)){
                long total=0;byte[] buffer=new byte[65536];int count;while((count=stream.read(buffer))!=-1){total+=count;if(total>update.size)throw new IOException("Update exceeds expected size");output.write(buffer,0,count);int percent=(int)(total*100/update.size);runOnUiThread(()->progress.setProgress(percent));}if(total!=update.size)throw new IOException("Incomplete update");output.getFD().sync();
            }finally{request.disconnect();}
            verify(partial,update);File ready=new File(getCacheDir(),"III85III-VPN.apk");if(ready.exists()&&!ready.delete())throw new IOException("Previous update is busy");if(!partial.renameTo(ready))throw new IOException("Cannot prepare update");downloaded=ready;
            runOnUiThread(()->{if(!isDestroyed()){status.setText("Update verified. Android will ask you to confirm installation.");action.setText("Install update");finishWork();}});
        }catch(Exception error){partial.delete();runOnUiThread(()->{if(!isDestroyed()){status.setText("The update could not be downloaded or verified. Nothing was installed. Please retry.");finishWork();}});}});
    }
    static String digest(File file)throws Exception{MessageDigest hash=MessageDigest.getInstance("SHA-256");try(InputStream input=new FileInputStream(file)){byte[] buffer=new byte[65536];int n;while((n=input.read(buffer))!=-1)hash.update(buffer,0,n);}StringBuilder result=new StringBuilder();for(byte value:hash.digest())result.append(String.format(Locale.ROOT,"%02x",value&255));return result.toString();}
    private void verify(File file,Info update)throws Exception{
        if(file.length()!=update.size||!digest(file).equalsIgnoreCase(update.hash))throw new IOException("Update checksum mismatch");
        int flags=Build.VERSION.SDK_INT>=28?PackageManager.GET_SIGNING_CERTIFICATES:PackageManager.GET_SIGNATURES;
        PackageInfo candidate=getPackageManager().getPackageArchiveInfo(file.getAbsolutePath(),flags),current=getPackageManager().getPackageInfo(getPackageName(),flags);
        if(candidate==null||!getPackageName().equals(candidate.packageName)||(Build.VERSION.SDK_INT>=28?candidate.getLongVersionCode():candidate.versionCode)!=update.code||update.code<=BuildConfig.VERSION_CODE)throw new IOException("Unexpected package or version");
        android.content.pm.Signature[] expected=Build.VERSION.SDK_INT>=28?current.signingInfo.getApkContentsSigners():current.signatures,actual=Build.VERSION.SDK_INT>=28?candidate.signingInfo.getApkContentsSigners():candidate.signatures;
        if(expected==null||actual==null||!new HashSet<>(Arrays.asList(expected)).equals(new HashSet<>(Arrays.asList(actual))))throw new IOException("Update signing identity changed");
    }
    private void install(){
        if(busy||downloaded==null)return;
        if(!getPackageManager().canRequestPackageInstalls()){status.setText("Allow updates from III85III VPN, then return here and tap Install update.");startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,Uri.parse("package:"+getPackageName())));return;}
        busy=true;action.setEnabled(false);CoreRuntime.IO.execute(()->{int id=-1;PackageInstaller installer=getPackageManager().getPackageInstaller();try{
            verify(downloaded,available);PackageInstaller.SessionParams params=new PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL);params.setAppPackageName(getPackageName());params.setSize(downloaded.length());id=installer.createSession(params);
            try(PackageInstaller.Session session=installer.openSession(id)){
                try(InputStream input=new FileInputStream(downloaded);OutputStream output=session.openWrite("base.apk",0,downloaded.length())){byte[] buffer=new byte[65536];int count;while((count=input.read(buffer))!=-1)output.write(buffer,0,count);session.fsync(output);}
                runOnUiThread(()->startService(new Intent(this,TunnelService.class).setAction(TunnelService.STOP)));
                Intent callback=new Intent(this,UpdateReceiver.class);int flags=PendingIntent.FLAG_UPDATE_CURRENT|(Build.VERSION.SDK_INT>=31?PendingIntent.FLAG_MUTABLE:0);PendingIntent pending=PendingIntent.getBroadcast(this,86,callback,flags);session.commit(pending.getIntentSender());
            }
            runOnUiThread(()->{if(!isDestroyed()){status.setText("Confirm the update in Android's installer.");finishWork();}});
        }catch(Exception error){if(id!=-1)installer.abandonSession(id);runOnUiThread(()->{if(!isDestroyed()){status.setText("Installation could not start. Nothing was changed. You can retry.");finishWork();}});}});
    }
}
