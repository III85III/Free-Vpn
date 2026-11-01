package io.github.iii85iii.vpn;

import android.app.*;
import android.content.*;
import android.content.pm.*;
import android.database.Cursor;
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
    static boolean isRequired(Context context){ return false; }
    static void openRequired(Activity owner){}
    private static void require(Activity owner,Info info){}
    @Override public void onBackPressed(){ finish(); }
    private Button back;
    private TextView status;private ProgressBar progress;private Button action;
    private Info available;private File downloaded;private boolean busy;
    private static final java.util.concurrent.atomic.AtomicBoolean downloading=new java.util.concurrent.atomic.AtomicBoolean();
    static final class Info {String version,url,hash;long size,code;}
    @Override public void onCreate(Bundle state){
        super.onCreate(state);LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);root.setBackgroundColor(0xff101319);int pad=(int)(24*getResources().getDisplayMetrics().density);root.setPadding(pad,pad,pad,pad);
        if(Build.VERSION.SDK_INT>=33)getOnBackInvokedDispatcher().registerOnBackInvokedCallback(android.window.OnBackInvokedDispatcher.PRIORITY_DEFAULT,()->finish());
        back=new Button(this);back.setText("✕ بستن و ادامه کار با نسخه فعلی");back.setAllCaps(false);back.setOnClickListener(v->finish());root.addView(back);
        status=new TextView(this);status.setTextColor(0xffefefea);status.setTextSize(18);status.setPadding(0,pad,0,pad);root.addView(status);
        progress=new ProgressBar(this,null,android.R.attr.progressBarStyleHorizontal);progress.setMax(100);root.addView(progress);
        action=new Button(this);action.setAllCaps(false);action.setText("بررسی آپدیت");action.setOnClickListener(v->{if(downloaded!=null)install();else if(available!=null)download();else check();});root.addView(action);
        Button directWebBtn=new Button(this);
        directWebBtn.setText("🌐 دانلود مستقیم فایل نصبی با مرورگر");
        directWebBtn.setAllCaps(false);
        directWebBtn.setOnClickListener(v->{
            if(available!=null&&available.url!=null){
                try{startActivity(new Intent(Intent.ACTION_VIEW,Uri.parse(available.url)));}catch(Exception ignored){}
            }else{
                try{startActivity(new Intent(Intent.ACTION_VIEW,Uri.parse("https://github.com/III85III/Free-Vpn/releases/latest")));}catch(Exception ignored){}
            }
        });
        root.addView(directWebBtn);
        try{android.graphics.Typeface font=android.graphics.Typeface.createFromAsset(getAssets(),"fonts/Lato-Regular.ttf");status.setTypeface(font);action.setTypeface(font);back.setTypeface(font);directWebBtn.setTypeface(font);}catch(RuntimeException ignored){}
        android.graphics.drawable.GradientDrawable button=new android.graphics.drawable.GradientDrawable();button.setColor(0xffd9bd83);button.setCornerRadius(pad/2f);action.setBackground(button);action.setTextColor(0xff101319);action.setMinHeight((int)(54*getResources().getDisplayMetrics().density));progress.setProgressTintList(android.content.res.ColorStateList.valueOf(0xffd9bd83));
        TextView note=new TextView(this);note.setTextColor(0xff9fa7b4);note.setText("آپدیت‌ها کاملاً اختیاری هستند و می‌توانید بدون آپدیت به استفاده از فیلترشکن ادامه دهید.");note.setPadding(0,pad,0,0);root.addView(note);setContentView(root);WindowLayout.apply(this,root);
        check();
    }
    private void check(){if(busy)return;busy=true;action.setEnabled(false);status.setText("Checking for updates…");progress.setIndeterminate(true);
        CoreRuntime.IO.execute(()->{try{Info update=latest();File cached=new File(getCacheDir(),"III85III-VPN.apk");boolean valid=false;if(update.code>BuildConfig.VERSION_CODE&&cached.isFile()){try{verifyBytes(cached,update);valid=true;}catch(Exception ignored){}}final boolean ready=valid;runOnUiThread(()->{if(isDestroyed())return;available=update.code>BuildConfig.VERSION_CODE?update:null;downloaded=ready?cached:null;status.setText(available==null?"شما از جدیدترین نسخه استفاده می‌کنید.":ready?"آپدیت دانلود شده و آماده نصب است.":"نسخه جدید "+update.version+" منتشر شده است.");action.setText(available==null?"بررسی مجدد":ready?"نصب آپدیت":"دانلود و نصب آپدیت");finishWork();});}catch(Exception error){runOnUiThread(()->{if(!isDestroyed()){status.setText("خطا در بررسی آپدیت. وضعیت اتصال را چک کنید.");finishWork();}});}});
    }
    private void finishWork(){busy=false;progress.setIndeterminate(false);action.setEnabled(true);}
    static HttpURLConnection open(String address)throws Exception{
        return open(address,0);
    }
    static HttpURLConnection open(String address,long offset)throws Exception{
        URI uri=new URI(address);
        Proxy route=Proxy.NO_PROXY;
        try{if(mobile.Mobile.isOn()){String endpoint=mobile.Mobile.proxyAddress();URI local=new URI("http://"+endpoint);if("127.0.0.1".equals(local.getHost())&&local.getPort()>0)route=new Proxy(Proxy.Type.HTTP,new InetSocketAddress("127.0.0.1",local.getPort()));}}catch(Exception ignored){}
        for(int attempt=0;attempt<6;attempt++){
            if(!UpdatePolicy.trustedRedirect(uri))throw new IOException("Untrusted update host");
            HttpURLConnection request=(HttpURLConnection)uri.toURL().openConnection(route);request.setUseCaches(false);request.setInstanceFollowRedirects(false);request.setConnectTimeout(15000);request.setReadTimeout(30000);request.setRequestProperty("User-Agent","III85III-VPN/"+BuildConfig.VERSION_NAME);request.setRequestProperty("Accept-Encoding","identity");if(offset>0)request.setRequestProperty("Range","bytes="+offset+"-");
            int code=request.getResponseCode();if(code>=300&&code<400){String target=request.getHeaderField("Location");request.disconnect();if(target==null)throw new IOException("Missing update location");uri=uri.resolve(target);continue;}
            if(code!=200&&!(offset>0&&code==206)){request.disconnect();throw new IOException("Update unavailable");}return request;
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
    }
    private void download(){if(busy||available==null)return;long required=available.size*2+10L*1024*1024;if(getCacheDir().getUsableSpace()<required){status.setText("فضای ذخیره‌سازی کافی نیست.");return;}if(!downloading.compareAndSet(false,true)){status.setText("در حال دانلود...");return;}busy=true;action.setEnabled(false);status.setText("در حال دانلود آپدیت...");Info update=available;
        CoreRuntime.IO.execute(()->{File partial=new File(getCacheDir(),"update.part");try{
            UpdateTransfer.download(partial,update.size,offset->open(update.url,offset),percent->runOnUiThread(()->progress.setProgress(percent)));
            downloaded=prepareUpdate(partial,update);
            runOnUiThread(()->{if(!isDestroyed()){progress.setProgress(100);status.setText("دانلود کامل شد! در حال اجرای نصاب...");action.setText("نصب آپدیت");finishWork();install();}});
        }catch(Exception error){partial.delete();final String message=downloadError(error);runOnUiThread(()->{if(!isDestroyed()){status.setText(message);finishWork();}});}finally{downloading.set(false);}});
    }
    private File prepareUpdate(File partial,Info update)throws Exception{
        verifyBytes(partial,update);
        File ready=new File(getCacheDir(),"III85III-VPN.apk");
        if(ready.exists()&&!ready.delete()){}
        if(!partial.renameTo(ready)){
            try(InputStream in=new FileInputStream(partial);OutputStream out=new FileOutputStream(ready)){
                byte[] b=new byte[65536];int l;while((l=in.read(b))!=-1)out.write(b,0,l);
            }
            partial.delete();
        }
        return ready;
    }
    private static final class UpdateFailure extends IOException {
        final String code;
        UpdateFailure(String code,String message){super(message);this.code=code;}
    }
    static String downloadError(Exception error){
        if(error instanceof UpdateFailure)return error.getMessage()+" ["+((UpdateFailure)error).code+"] Nothing was installed.";
        return "Download interrupted. Check your connection and retry. [NETWORK] Nothing was installed.";
    }
    private static void verifyBytes(File file,Info update)throws Exception{
        if(file.length()!=update.size)throw new UpdateFailure("SIZE","The download is incomplete. Please retry.");
        if(!digest(file).equalsIgnoreCase(update.hash))throw new UpdateFailure("HASH","The downloaded file doesn't match this update. Check for updates and retry.");
    }
    static String digest(File file)throws Exception{MessageDigest hash=MessageDigest.getInstance("SHA-256");try(InputStream input=new FileInputStream(file)){byte[] buffer=new byte[65536];int n;while((n=input.read(buffer))!=-1)hash.update(buffer,0,n);}StringBuilder result=new StringBuilder();for(byte value:hash.digest())result.append(String.format(Locale.ROOT,"%02x",value&255));return result.toString();}
    private void install(){
        if(downloaded==null||!downloaded.exists()){
            if(available!=null)download();
            return;
        }
        busy=true;action.setEnabled(false);
        CoreRuntime.IO.execute(()->{
            try {
                verifyBytes(downloaded, available);
                runOnUiThread(()->{
                    finishWork();
                    launchInstall();
                });
            } catch(Exception error){
                runOnUiThread(()->{
                    status.setText("فایل ناقص بود. مجدداً دانلود می‌شود...");
                    finishWork();
                    download();
                });
            }
        });
    }

    private void launchInstall(){
        try {
            startService(new Intent(this, TunnelService.class).setAction(TunnelService.STOP));
        } catch(Exception ignored){}

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            if (!getPackageManager().canRequestPackageInstalls()) {
                try {
                    Intent allowIntent = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + getPackageName()));
                    startActivityForResult(allowIntent, 999);
                    Toast.makeText(this, "لطفاً دسترسی نصب برنامه را تایید کنید", Toast.LENGTH_LONG).show();
                    return;
                } catch(Exception ignored) {}
            }
        }

        try {
            Uri contentUri = Uri.parse("content://" + getPackageName() + ".updateprovider/update.apk");
            Intent intent = new Intent(Intent.ACTION_VIEW);
            intent.setDataAndType(contentUri, "application/vnd.android.package-archive");
            intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP);
            startActivity(intent);
        } catch(Exception e) {
            try {
                Uri contentUri = Uri.parse("content://" + getPackageName() + ".updateprovider/update.apk");
                Intent intent = new Intent(Intent.ACTION_INSTALL_PACKAGE);
                intent.setDataAndType(contentUri, "application/vnd.android.package-archive");
                intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
                startActivity(intent);
            } catch(Exception fallbackError) {
                if(available != null && available.url != null){
                    try{startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(available.url)));}catch(Exception ignored){}
                }
            }
        }
    }

    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == 999) {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && getPackageManager().canRequestPackageInstalls()) {
                launchInstall();
            }
        }
    }

    public static final class Provider extends ContentProvider {
        @Override public boolean onCreate() { return true; }
        @Override public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
            File file = new File(getContext().getCacheDir(), "III85III-VPN.apk");
            if (file.exists()) {
                return ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY);
            }
            throw new FileNotFoundException("Update APK not found");
        }
        @Override public String getType(Uri uri) { return "application/vnd.android.package-archive"; }
        @Override public Cursor query(Uri uri, String[] projection, String selection, String[] selectionArgs, String sortOrder) {
            File file = new File(getContext().getCacheDir(), "III85III-VPN.apk");
            if (!file.exists()) return null;
            android.database.MatrixCursor cursor = new android.database.MatrixCursor(new String[]{
                android.provider.OpenableColumns.DISPLAY_NAME,
                android.provider.OpenableColumns.SIZE
            }, 1);
            cursor.addRow(new Object[]{file.getName(), file.length()});
            return cursor;
        }
        @Override public Uri insert(Uri uri, ContentValues values) { return null; }
        @Override public int delete(Uri uri, String selection, String[] selectionArgs) { return 0; }
        @Override public int update(Uri uri, ContentValues values, String selection, String[] selectionArgs) { return 0; }
    }
}
