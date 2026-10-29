package io.github.iii85iii.vpn;

import android.app.*;
import android.content.*;
import android.content.pm.ApplicationInfo;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.VpnService;
import android.os.*;
import android.text.*;
import android.text.method.PasswordTransformationMethod;
import android.view.*;
import android.widget.*;
import org.json.*;
import java.util.*;
import mobile.Mobile;

public final class MainActivity extends Activity {
    private final int BG=Color.rgb(16,19,25),SURFACE=Color.rgb(27,32,40),GOLD=Color.rgb(217,189,131),TEXT=Color.rgb(239,239,234),MUTED=Color.rgb(159,167,180);
    private final Handler main=new Handler(Looper.getMainLooper());
    private LinearLayout content,nav;
    private TextView statusText,detailText,speedText,feedback;
    private Button power;
    private int page;
    private boolean ready,visible,polling,pinging;
    private boolean warming;
    private long nextWarmup;
    private JSONObject status=new JSONObject();
    private JSONArray locations=new JSONArray();
    private Typeface body=Typeface.create("sans-serif",Typeface.NORMAL);
    private SignInFlow signIn;
    private interface Reply {void accept(JSONObject value);}
    private int dp(int value){return Math.round(value*getResources().getDisplayMetrics().density);}
    @Override public void onCreate(Bundle state){
        super.onCreate(state);
        if(!(BuildConfig.DEBUG&&getIntent().getBooleanExtra("qa_screenshots",false)))getWindow().setFlags(WindowManager.LayoutParams.FLAG_SECURE,WindowManager.LayoutParams.FLAG_SECURE);
        try{body=Typeface.createFromAsset(getAssets(),"fonts/Lato-Regular.ttf");}catch(Exception ignored){}
        LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);root.setBackgroundColor(BG);root.setPadding(dp(18),dp(12),dp(18),0);
        TextView brand=label("III85III  /  VPN",21,GOLD);brand.setPadding(0,dp(10),0,dp(16));root.addView(brand);
        HorizontalScrollView navigation=new HorizontalScrollView(this);navigation.setHorizontalScrollBarEnabled(false);
        nav=new LinearLayout(this);navigation.addView(nav);root.addView(navigation);
        ScrollView scroll=new ScrollView(this);scroll.setFillViewport(true);content=new LinearLayout(this);content.setOrientation(LinearLayout.VERTICAL);content.setPadding(0,dp(24),0,dp(24));scroll.addView(content);root.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));
        feedback=label("Starting your local engine…",13,MUTED);feedback.setPadding(0,dp(8),0,dp(10));root.addView(feedback);setContentView(root);
        WindowLayout.apply(this,root);
        showPage(state==null?0:state.getInt("page",0));
        CoreRuntime.IO.execute(()->{try{CoreRuntime.ensure(this);main.post(()->{if(isDestroyed())return;ready=true;feedback.setText("");showPage(page);if(visible)poll();});}catch(Exception failure){main.post(()->{if(!isDestroyed())feedback.setText("The local engine could not start. Close the app and try again.");});}});
    }
    @Override protected void onSaveInstanceState(Bundle state){super.onSaveInstanceState(state);state.putInt("page",page);}
    @Override protected void onStart(){super.onStart();visible=true;if(ready)poll();}
    @Override protected void onStop(){visible=false;main.removeCallbacks(pollAgain);super.onStop();}
    @Override protected void onDestroy(){if(signIn!=null)signIn.cancel();main.removeCallbacksAndMessages(null);super.onDestroy();}
    private TextView label(String value,int size,int color){TextView text=new TextView(this);text.setText(value);text.setTextSize(size);text.setTextColor(color);text.setTypeface(body);text.setLineSpacing(dp(4),1);return text;}
    private GradientDrawable background(int color){GradientDrawable shape=new GradientDrawable();shape.setColor(color);shape.setCornerRadius(dp(15));return shape;}
    private Button button(String title,Runnable action){Button b=new Button(this);b.setText(title);b.setAllCaps(false);b.setTextSize(15);b.setTypeface(body);b.setTextColor(TEXT);b.setBackground(background(SURFACE));LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,dp(54));p.setMargins(0,dp(7),0,dp(7));b.setLayoutParams(p);b.setOnClickListener(v->action.run());return b;}
    private void heading(String title,String subtitle){TextView h=label(title,27,TEXT);content.addView(h);TextView sub=label(subtitle,14,MUTED);sub.setPadding(0,dp(8),0,dp(20));content.addView(sub);}
    private void note(String text){TextView view=label(text,14,MUTED);view.setPadding(0,dp(8),0,dp(12));content.addView(view);}
    private void api(String method,String path,JSONObject body,int timeout,Reply reply){
        if(!ready){feedback.setText("Your local engine is starting.");return;}
        CoreRuntime.IO.execute(()->{try{JSONObject value=CoreRuntime.request(method,path,body,timeout);main.post(()->{if(!isDestroyed())reply.accept(value);});}catch(Exception error){main.post(()->{if(!isDestroyed())feedback.setText(error.getMessage()==null?"The request could not finish. Try again.":error.getMessage());});}});
    }
    private JSONObject object(String key,Object value){JSONObject obj=new JSONObject();try{obj.put(key,value);}catch(JSONException impossible){throw new IllegalStateException(impossible);}return obj;}
    private void showPage(int target){
        page=target;nav.removeAllViews();String[] names={"Home","Locations","App rules","Accounts","Settings","Help"};
        for(int i=0;i<names.length;i++){final int tab=i;Button item=button(names[i],()->showPage(tab));item.setTextColor(tab==page?BG:MUTED);item.setBackground(background(tab==page?GOLD:BG));LinearLayout.LayoutParams params=new LinearLayout.LayoutParams(-2,dp(48));params.setMargins(0,0,dp(6),0);item.setLayoutParams(params);nav.addView(item);}
        content.removeAllViews();statusText=detailText=speedText=null;power=null;
        switch(page){case 0:home();break;case 1:locationPage();break;case 2:appRules();break;case 3:accounts();break;case 4:settings();break;default:help();}
    }
    private void home(){
        heading("Make yourself at home.","Your account. Your connection.");
        statusText=label("Ready when you are",28,GOLD);statusText.setGravity(Gravity.CENTER);statusText.setPadding(0,dp(34),0,dp(16));content.addView(statusText);
        detailText=label("Add your Mozilla account, choose a location and connect.",14,MUTED);detailText.setGravity(Gravity.CENTER);content.addView(detailText);
        power=button("Connect",this::toggleConnection);power.setTextColor(BG);power.setBackground(background(GOLD));content.addView(power);
        content.addView(button("Choose a location",()->showPage(1)));
        speedText=label("↓ 0 B/s      ↑ 0 B/s",16,TEXT);speedText.setGravity(Gravity.CENTER);speedText.setPadding(0,dp(24),0,dp(20));content.addView(speedText);
        note("No personal VPS required. Mozilla account eligibility and normal service quota apply.");updateHome();
    }
    private void toggleConnection(){
        if(TunnelService.lifecycle.canCancel()){startService(new Intent(this,TunnelService.class).setAction(TunnelService.STOP));return;}
        if(TunnelService.lifecycle.phase()==VpnLifecycle.Phase.STOPPING){feedback.setText("Finishing the previous connection. Try again in a moment.");return;}
        api("GET","/api/accounts",null,5000,reply->{
            JSONArray rows=reply.optJSONArray("accounts");if(rows==null||rows.length()==0){showPage(3);feedback.setText("Add your Mozilla account to get started.");return;}
            explainConnection();
        });
    }
    private void explainConnection(){
        if(getPreferences(MODE_PRIVATE).getInt("vpn_disclosure",0)==1){requestVpnPermission();return;}
        new AlertDialog.Builder(this).setTitle("Before you connect")
            .setMessage("III85III VPN uses Android's VPN interface to route supported internet traffic through Mozilla's network service. Your Mozilla account is used for sign-in and service quota checks. The service receives information needed to carry your connection, including network addresses. Selected excluded apps stay direct. Local connection diagnostics may contain network details. This app is not a verified kill switch.")
            .setNegativeButton("Not now",(dialog,which)->feedback.setText("VPN has not been started."))
            .setNeutralButton("Privacy details",(dialog,which)->openDocument("privacy"))
            .setPositiveButton("Agree and continue",(dialog,which)->{getPreferences(MODE_PRIVATE).edit().putInt("vpn_disclosure",1).apply();requestVpnPermission();}).show();
    }
    private void requestVpnPermission(){Intent permission=VpnService.prepare(this);if(permission!=null)startActivityForResult(permission,85);else startTunnel();}
    private void openDocument(String name){startActivity(new Intent(this,InformationActivity.class).putExtra("document",name));}
    private void startTunnel(){
        if(Build.VERSION.SDK_INT>=33&&checkSelfPermission("android.permission.POST_NOTIFICATIONS")!=android.content.pm.PackageManager.PERMISSION_GRANTED)requestPermissions(new String[]{"android.permission.POST_NOTIFICATIONS"},86);
        Intent intent=new Intent(this,TunnelService.class);startForegroundService(intent);feedback.setText("Starting the VPN connection…");
    }
    @Override protected void onActivityResult(int request,int result,Intent value){super.onActivityResult(request,result,value);if(request==85){if(result==RESULT_OK)startTunnel();else feedback.setText("VPN permission was not granted. Your connection is unchanged.");}}
    private final Runnable pollAgain=this::poll;
    private void poll(){
        if(!visible||!ready||polling)return;polling=true;
        CoreRuntime.IO.execute(()->{try{JSONObject value=CoreRuntime.request("GET","/api/status",null,4000);main.post(()->{if(!isDestroyed()){status=value;updateHome();warmConnection();}});}catch(Exception ignored){main.post(()->{if(!isDestroyed()){status=new JSONObject();updateHome();feedback.setText("Connection status is unavailable. Check the VPN notification or try again.");}});}finally{main.post(()->{polling=false;if(visible&&!isDestroyed())main.postDelayed(pollAgain,2000);});}});
    }
    private boolean verified(){JSONObject state=status.optJSONObject("state"),test=status.optJSONObject("last_test");return ConnectionTruth.verified(TunnelService.active,state==null?"off":state.optString("phase"),test!=null&&test.optBoolean("ok"),test==null?"":test.optString("at"),state==null?"":state.optString("since"),status.optString("network_changed_at"));}
    private void warmConnection(){if(!TunnelService.active||verified()||warming||SystemClock.elapsedRealtime()<nextWarmup)return;warming=true;nextWarmup=SystemClock.elapsedRealtime()+30000;CoreRuntime.IO.execute(()->{try{CoreRuntime.request("POST","/api/test",new JSONObject(),20000);}catch(Exception ignored){}finally{main.post(()->warming=false);}});}
    private void updateHome(){
        if(page!=0||statusText==null)return;
        JSONObject state=status.optJSONObject("state"),test=status.optJSONObject("last_test");
        String phase=state==null?"off":state.optString("phase","off");
        boolean verified=verified();
        VpnLifecycle.Phase transition=TunnelService.lifecycle.phase();
        statusText.setText(verified?"You're connected.":TunnelService.active||transition==VpnLifecycle.Phase.STARTING?"Getting you connected…":transition==VpnLifecycle.Phase.STOPPING?"Disconnecting…":"Ready when you are.");
        detailText.setText(verified?"Your connection is ready.":TunnelService.error.isEmpty()?"Choose a location. Connect whenever you're ready.":TunnelService.error);
        power.setText(transition==VpnLifecycle.Phase.STARTING?"Cancel connection":transition==VpnLifecycle.Phase.STOPPING?"Disconnecting…":TunnelService.active?"Disconnect":"Connect");
        power.setEnabled(transition!=VpnLifecycle.Phase.STOPPING);
        try{String[] rate=Mobile.speed().split(",");speedText.setText("↓ "+bytes(Double.parseDouble(rate[0]))+"/s      ↑ "+bytes(Double.parseDouble(rate[1]))+"/s");}catch(Exception ignored){}
    }
    private String bytes(double amount){String[] unit={"B","KB","MB","GB"};int n=0;while(amount>=1000&&n<3){amount/=1000;n++;}return String.format(Locale.US,n==0?"%.0f %s":"%.1f %s",amount,unit[n]);}
    private void locationPage(){
        heading("Somewhere that suits you.","Measure response time, then choose your location.");
        EditText search=new EditText(this);search.setSingleLine(true);search.setHint("Search country or code");search.setTextColor(TEXT);search.setHintTextColor(MUTED);content.addView(search);
        Button measure=button(pinging?"Measuring…":"Measure latency",()->{
            if(pinging)return;pinging=true;feedback.setText("Measuring locations. You can still disconnect from Home.");
            CoreRuntime.IO.execute(()->{try{CoreRuntime.request("POST","/api/locations/ping",object("codes",new JSONArray()),60000);}catch(Exception error){main.post(()->feedback.setText("Latency measurement could not finish. You can retry."));}finally{main.post(()->{pinging=false;if(page==1)showPage(1);});}});
        });measure.setEnabled(!pinging);content.addView(measure);
        LinearLayout list=new LinearLayout(this);list.setOrientation(LinearLayout.VERTICAL);content.addView(list);
        Runnable render=()->{list.removeAllViews();String q=search.getText().toString().trim().toLowerCase(Locale.ROOT);List<JSONObject> values=new ArrayList<>();for(int i=0;i<locations.length();i++)values.add(locations.optJSONObject(i));values.sort((a,b)->{int x=a.optInt("ping_ms",0),y=b.optInt("ping_ms",0);return Integer.compare(x>0?x:Integer.MAX_VALUE,y>0?y:Integer.MAX_VALUE);});
            for(JSONObject row:values){String code=row.optString("code"),name=row.optString("name",code);if(!(name+" "+code).toLowerCase(Locale.ROOT).contains(q))continue;int ping=row.optInt("ping_ms",0);list.addView(button(name+(ping>0?"  ·  "+ping+" ms":""),()->api("POST","/api/location",object("country",code),10000,reply->{feedback.setText("Location selected: "+name);showPage(0);})));}
            if(list.getChildCount()==0){TextView empty=label("No matching locations yet.",14,MUTED);list.addView(empty);}
        };
        search.addTextChangedListener(new TextWatcher(){public void beforeTextChanged(CharSequence s,int start,int count,int after){}public void onTextChanged(CharSequence s,int start,int before,int count){render.run();}public void afterTextChanged(Editable value){}});
        content.addView(button("Recommended location",()->api("POST","/api/location",object("country",""),10000,reply->{showPage(0);})),2);
        api("GET","/api/locations",null,15000,reply->{JSONArray rows=reply.optJSONArray("locations");locations=rows==null?new JSONArray():rows;if(page==1)render.run();});render.run();
    }
    private void accounts(){
        heading("Your own accounts.","Use your Mozilla password, not your Google password.");
        content.addView(button("Add an account",()->{if(signIn==null||signIn.closed)signIn=new SignInFlow();}));
        boolean hidden=getPreferences(MODE_PRIVATE).getBoolean("hide_email",true);
        content.addView(button(hidden?"Show email addresses":"Hide email addresses",()->{getPreferences(MODE_PRIVATE).edit().putBoolean("hide_email",!hidden).apply();showPage(3);}));
        LinearLayout rows=new LinearLayout(this);rows.setOrientation(LinearLayout.VERTICAL);content.addView(rows);
        api("GET","/api/accounts",null,7000,reply->{if(page!=3)return;JSONArray data=reply.optJSONArray("accounts");if(data==null||data.length()==0){note("No accounts yet. Your account session stays in this app's private storage.");return;}
            for(int i=0;i<data.length();i++){JSONObject account=data.optJSONObject(i);String id=account.optString("id"),email=account.optString("email");JSONObject usage=account.optJSONObject("usage");String quota=usage==null?"Quota not checked":usage.optBoolean("unlimited")?"Unlimited":bytes(usage.optDouble("remaining_bytes",0))+" remaining";String reset=usage==null?"":usage.optString("reset_at");
                rows.addView(label((hidden?"••••••••":email)+"\n"+account.optString("status")+" · "+quota+(reset.isEmpty()?"":"\nRenews: "+reset),15,TEXT));
                rows.addView(button("Refresh balance",()->api("POST","/api/accounts/"+id+"/refresh",new JSONObject(),15000,r->{if(page==3)showPage(3);})));
                rows.addView(button("Remove account",()->new AlertDialog.Builder(this).setTitle("Remove this account?").setMessage("This removes its stored session from this device.").setNegativeButton("Cancel",null).setPositiveButton("Remove",(d,w)->api("DELETE","/api/accounts/"+id,null,7000,r->{if(page==3)showPage(3);})).show()));
            }
        });
    }
    private void appRules(){
        heading("Your apps, your choice.","Selected applications connect directly, without the VPN. Apply choices before connecting.");
        Set<String> selected=new HashSet<>(getSharedPreferences("interface",MODE_PRIVATE).getStringSet("direct_apps",Collections.emptySet()));
        content.addView(button("Save choices",()->{getSharedPreferences("interface",MODE_PRIVATE).edit().putStringSet("direct_apps",selected).apply();feedback.setText(TunnelService.active?"Saved. Disconnect and reconnect to apply these choices.":"App choices saved.");}));
        LinearLayout apps=new LinearLayout(this);apps.setOrientation(LinearLayout.VERTICAL);content.addView(apps);
        CoreRuntime.IO.execute(()->{
            Intent launcher=new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER);
            List<android.content.pm.ResolveInfo> entries=getPackageManager().queryIntentActivities(launcher,0);entries.sort((a,b)->a.loadLabel(getPackageManager()).toString().compareToIgnoreCase(b.loadLabel(getPackageManager()).toString()));
            main.post(()->{if(isDestroyed()||page!=2)return;Set<String> seen=new HashSet<>();for(android.content.pm.ResolveInfo entry:entries){String pkg=entry.activityInfo.packageName;if(pkg.equals(getPackageName())||!seen.add(pkg))continue;CheckBox check=new CheckBox(this);check.setText(entry.loadLabel(getPackageManager()));check.setTextColor(TEXT);check.setTypeface(body);check.setChecked(selected.contains(pkg));check.setOnCheckedChangeListener((b,value)->{if(value)selected.add(pkg);else selected.remove(pkg);});apps.addView(check);}if(apps.getChildCount()==0)apps.addView(label("No visible launcher apps found.",14,MUTED));});
        });
    }
    private void settings(){
        heading("Make it yours.","III85III VPN "+BuildConfig.VERSION_NAME);
        content.addView(button("Privacy and connection information",()->openDocument("privacy")));
        content.addView(button("Open-source licenses",()->openDocument("licenses")));
        content.addView(button("Updates on GitHub",()->startActivity(new Intent(Intent.ACTION_VIEW,android.net.Uri.parse("https://github.com/III85III/Free-Vpn/releases/tag/android-v2.3.0")))));
        note("Selected excluded apps connect directly. Your choices apply when you reconnect.");
        note("Account sessions stay in this app's private storage. Screenshots are blocked in the distributed app and account backups are disabled.");
        note("Disconnect from the app, its notification or Android VPN settings. Manual disconnect is respected; the app does not automatically reconnect afterwards.");
        note("Not a kill switch. Traffic can go direct after disconnect. Mozilla eligibility, regions and quotas apply.");
    }
    private void help(){heading("A little connection help.","Check your account, network and selected location.");content.addView(button("Check connection",()->api("POST","/api/test",new JSONObject(),20000,reply->feedback.setText(reply.optBoolean("ok")?"Connection verified.":"Connection could not be verified. Check your account and network."))));content.addView(button("Measure server latency",()->showPage(1)));note("Connection problems: check that the same account can sign in to Mozilla in a browser. Account-specific errors or service challenges are not guaranteed to be resolved by this app.");note("Telegram-bot-only routing and arbitrary UDP are not provided. App rules are exclusions, not a promise that only selected apps use the VPN.");note("This Android build needs real-device login, tunnel and network-handover testing before store publication.");}

    private final class SignInFlow {
        boolean closed,pending;String session="";AlertDialog dialog;EditText password;
        final Runnable pollFlow=()->fetch();
        SignInFlow(){
            LinearLayout fields=new LinearLayout(MainActivity.this);fields.setPadding(dp(24),dp(8),dp(24),dp(8));fields.setOrientation(LinearLayout.VERTICAL);
            EditText email=new EditText(MainActivity.this);email.setHint("Mozilla email");email.setSingleLine(true);email.setInputType(android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS);fields.addView(email);
            password=new EditText(MainActivity.this);password.setHint("Mozilla password");password.setSingleLine(true);password.setInputType(129);password.setTransformationMethod(PasswordTransformationMethod.getInstance());fields.addView(password);
            dialog=new AlertDialog.Builder(MainActivity.this).setTitle("Welcome to III85III").setMessage("Sign in with your own Mozilla account.").setView(fields).setNegativeButton("Cancel",(d,w)->cancel()).setPositiveButton("Sign in",null).create();dialog.setOnCancelListener(d->cancel());dialog.setOnShowListener(d->dialog.getButton(-1).setOnClickListener(v->{
                if(pending||closed)return;if(email.getText().toString().trim().isEmpty()||password.length()==0){feedback.setText("Enter your Mozilla email and password.");return;}
                JSONObject body=object("email",email.getText().toString().trim());try{body.put("password",password.getText().toString());}catch(JSONException ignored){}password.getText().clear();pending=true;dialog.getButton(-1).setEnabled(false);
                CoreRuntime.IO.execute(()->{try{JSONObject reply=CoreRuntime.request("POST","/api/signin",body,60000);String id=reply.optString("id");main.post(()->{pending=false;session=id;if(closed){cancelSession(id);return;}dialog.dismiss();verification();apply(reply);});}catch(Exception error){main.post(()->{pending=false;if(!closed){feedback.setText(error.getMessage());dialog.getButton(-1).setEnabled(true);}});}});
            }));dialog.show();
        }
        void verification(){dialog=new AlertDialog.Builder(MainActivity.this).setTitle("Checking your sign-in").setMessage("Waiting for Mozilla…").setNegativeButton("Cancel",(d,w)->cancel()).create();dialog.setOnCancelListener(d->cancel());dialog.show();}
        void apply(JSONObject state){
            if(closed)return;String step=state.optString("step");
            if(step.equals("done")){closed=true;main.removeCallbacks(pollFlow);dialog.dismiss();feedback.setText("Account added.");if(page==3)showPage(3);return;}
            if(step.equals("error")||step.equals("failed")||step.equals("cancelled")){feedback.setText(state.optString("error",state.optString("message","Sign-in could not finish.")));cancel();return;}
            boolean code=step.equals("need_email_code")||step.equals("need_totp")||step.equals("need_unblock_code")||step.equals("need_captcha");
            if(code){
                dialog.dismiss();LinearLayout fields=new LinearLayout(MainActivity.this);fields.setOrientation(LinearLayout.VERTICAL);fields.setPadding(dp(24),dp(8),dp(24),dp(8));
                String captcha=state.optString("captcha");if(step.equals("need_captcha")&&captcha.startsWith("data:image/")&&captcha.length()<1500000){try{byte[] image=android.util.Base64.decode(captcha.substring(captcha.indexOf(',')+1),android.util.Base64.DEFAULT);ImageView view=new ImageView(MainActivity.this);view.setImageBitmap(android.graphics.BitmapFactory.decodeByteArray(image,0,image.length));fields.addView(view);}catch(Exception ignored){}}
                EditText input=new EditText(MainActivity.this);input.setSingleLine(true);input.setHint(step.equals("need_captcha")?"Verification characters":"Verification code");fields.addView(input);
                dialog=new AlertDialog.Builder(MainActivity.this).setTitle(step.equals("need_totp")?"Enter your security code":step.equals("need_captcha")?"Confirm your sign-in":"Check your email").setMessage(state.optString("message","Enter the code requested by Mozilla.")).setView(fields).setNegativeButton("Cancel",(d,w)->cancel()).setPositiveButton("Verify",null).create();dialog.setOnCancelListener(d->cancel());dialog.setOnShowListener(d->dialog.getButton(-1).setOnClickListener(v->{if(pending||input.length()==0)return;pending=true;dialog.getButton(-1).setEnabled(false);String value=input.getText().toString();input.getText().clear();CoreRuntime.IO.execute(()->{try{JSONObject reply=CoreRuntime.request("POST","/api/signin/"+session,object("value",value),30000);main.post(()->{pending=false;apply(reply);});}catch(Exception error){main.post(()->{pending=false;if(!closed){feedback.setText(error.getMessage());dialog.getButton(-1).setEnabled(true);}});}});}));dialog.show();
            }else{dialog.setMessage(state.optString("message","Waiting for Mozilla…"));main.removeCallbacks(pollFlow);main.postDelayed(pollFlow,1500);}
        }
        void fetch(){if(closed||pending)return;pending=true;CoreRuntime.IO.execute(()->{try{JSONObject state=CoreRuntime.request("GET","/api/signin/"+session,null,5000);main.post(()->{pending=false;apply(state);});}catch(Exception error){main.post(()->{pending=false;if(!closed){feedback.setText("Sign-in status could not refresh. You can cancel and retry.");main.postDelayed(pollFlow,2500);}});}});}
        void cancelSession(String id){if(id.isEmpty())return;CoreRuntime.IO.execute(()->{try{CoreRuntime.request("DELETE","/api/signin/"+id,null,3000);}catch(Exception ignored){}});}
        void cancel(){if(closed)return;closed=true;main.removeCallbacks(pollFlow);if(password!=null)password.getText().clear();if(dialog!=null)dialog.dismiss();cancelSession(session);}
    }
}
