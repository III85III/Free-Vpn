package io.github.iii85iii.vpn;

import android.animation.ObjectAnimator;
import android.animation.ValueAnimator;
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
import android.view.animation.AccelerateDecelerateInterpolator;
import android.widget.*;
import org.json.*;
import java.util.*;
import mobile.Mobile;

public final class MainActivity extends Activity {
    private final int BG=Color.rgb(16,19,25),SURFACE=Color.rgb(27,32,40),GOLD=Color.rgb(217,189,131),TEXT=Color.rgb(239,239,234),MUTED=Color.rgb(159,167,180),GREEN=Color.rgb(46,213,115),AMBER=Color.rgb(255,165,2);
    private final Handler main=new Handler(Looper.getMainLooper());
    private LinearLayout content,nav;
    private TextView statusText,detailText,speedText,feedback,server;
    private Button power;
    private ImageView heroInfinity;
    private View statusDot;
    private LinearLayout updateBanner;
    private ObjectAnimator pulseAnim;
    private int page;
    private boolean ready,visible,polling,pinging;
    private boolean warming;
    private boolean checkingAccount;
    private boolean checkingAccountIntro,accountIntroShowing;
    private boolean checkingUpdate;
    private int connectAttempt;
    private long nextWarmup;
    private UpdateActivity.Info latestAvailable;
    private final Runnable updateAgain=new Runnable(){public void run(){if(isDestroyed()||!visible)return;checkForUpdates(false);main.postDelayed(this,300000);}};
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

        LinearLayout header=new LinearLayout(this);header.setGravity(Gravity.CENTER_VERTICAL);
        TextView brand=label("III85III",20,GOLD);brand.setTypeface(body,Typeface.BOLD);brand.setLetterSpacing(0.06f);
        TextView vpnBadge=label("VPN",11,BG);vpnBadge.setTypeface(body,Typeface.BOLD);vpnBadge.setBackground(cardBackground(GOLD,6));vpnBadge.setPadding(dp(7),dp(3),dp(7),dp(3));
        LinearLayout brandBox=new LinearLayout(this);brandBox.setGravity(Gravity.CENTER_VERTICAL);brandBox.setPadding(0,dp(6),0,dp(12));
        brandBox.addView(brand);
        LinearLayout.LayoutParams vpnLp=new LinearLayout.LayoutParams(-2,-2);vpnLp.setMargins(dp(10),0,0,0);brandBox.addView(vpnBadge,vpnLp);
        header.addView(brandBox,new LinearLayout.LayoutParams(0,-2,1));

        TextView contact=label("Support ↗",13,GOLD);contact.setPadding(dp(12),dp(6),0,dp(12));contact.setContentDescription("Telegram support");contact.setOnClickListener(v->{try{startActivity(new Intent(Intent.ACTION_VIEW,android.net.Uri.parse("https://t.me/cabicy")));}catch(ActivityNotFoundException error){showError("Open t.me/cabicy in your browser.");}});header.addView(contact);root.addView(header);
        nav=new LinearLayout(this);
        ScrollView scroll=new ScrollView(this);scroll.setFillViewport(true);content=new LinearLayout(this);content.setOrientation(LinearLayout.VERTICAL);content.setPadding(0,dp(10),0,dp(16));scroll.addView(content);root.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));
        feedback=label("Starting your local engine…",13,MUTED);feedback.setPadding(0,dp(6),0,dp(8));root.addView(feedback);root.addView(nav);setContentView(root);
        WindowLayout.apply(this,root);
        showPage(state==null?0:state.getInt("page",0));
        startTunnelCheck(getIntent());
        CoreRuntime.IO.execute(()->{try{CoreRuntime.ensure(this);main.post(()->{if(isDestroyed())return;ready=true;feedback.setText("");showPage(page);if(visible){poll();showAccountIntro();checkForUpdates(false);}});}catch(Exception failure){main.post(()->{if(!isDestroyed())feedback.setText("The local engine could not start. Close the app and try again.");});}});
    }
    @Override protected void onSaveInstanceState(Bundle state){super.onSaveInstanceState(state);state.putInt("page",page);}
    @Override protected void onNewIntent(Intent intent){super.onNewIntent(intent);startTunnelCheck(intent);}
    private void startTunnelCheck(Intent intent){if(BuildConfig.DEBUG&&intent!=null){if(intent.getBooleanExtra("qa_tun",false)){Intent permission=VpnService.prepare(this);if(permission==null)startForegroundService(new Intent(this,TunnelService.class).putExtra("qa_tun",true));else startActivityForResult(permission,185);}if(intent.getBooleanExtra("qa_update",false))startActivity(new Intent(this,UpdateActivity.class).putExtra("qa_update",true));}}
    @Override protected void onStart(){super.onStart();if(UpdateActivity.isRequired(this)){visible=false;UpdateActivity.openRequired(this);return;}visible=true;if(ready){poll();showAccountIntro();}main.postDelayed(updateAgain,1500);}
    @Override protected void onStop(){visible=false;stopInfinityBreathing();main.removeCallbacks(pollAgain);main.removeCallbacks(updateAgain);super.onStop();}
    @Override protected void onDestroy(){if(signIn!=null)signIn.cancel();stopInfinityBreathing();main.removeCallbacksAndMessages(null);super.onDestroy();}
    private TextView label(String value,int size,int color){TextView text=new TextView(this);text.setText(value);text.setTextSize(size);text.setTextColor(color);text.setTypeface(body);text.setLineSpacing(dp(4),1);return text;}
    private GradientDrawable background(int color){GradientDrawable shape=new GradientDrawable();shape.setColor(color);shape.setCornerRadius(dp(16));return shape;}
    private GradientDrawable cardBackground(int color,int radiusDp){GradientDrawable shape=new GradientDrawable();shape.setColor(color);shape.setCornerRadius(dp(radiusDp));return shape;}
    private GradientDrawable pillBackground(int strokeColor,int fillColor,int radiusDp,int strokeWidthDp){GradientDrawable shape=new GradientDrawable();shape.setColor(fillColor);shape.setCornerRadius(dp(radiusDp));if(strokeWidthDp>0)shape.setStroke(dp(strokeWidthDp),strokeColor);return shape;}
    private Button button(String title,Runnable action){Button b=new Button(this);b.setText(title);b.setAllCaps(false);b.setTextSize(15);b.setTypeface(body);b.setTextColor(TEXT);b.setBackground(new android.graphics.drawable.RippleDrawable(android.content.res.ColorStateList.valueOf(0x33D9BD83),background(SURFACE),background(Color.WHITE)));LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,dp(52));p.setMargins(0,dp(6),0,dp(6));b.setLayoutParams(p);b.setOnClickListener(v->action.run());return b;}
    private void startInfinityBreathing(){
        if(heroInfinity==null)return;
        if(pulseAnim!=null&&pulseAnim.isRunning())return;
        pulseAnim=ObjectAnimator.ofFloat(heroInfinity,"scaleX",1f,1.045f,1f);
        pulseAnim.setDuration(2400);
        pulseAnim.setRepeatCount(ValueAnimator.INFINITE);
        pulseAnim.setInterpolator(new AccelerateDecelerateInterpolator());
        pulseAnim.addUpdateListener(anim->{if(heroInfinity!=null)heroInfinity.setScaleY((Float)anim.getAnimatedValue());});
        pulseAnim.start();
    }
    private void stopInfinityBreathing(){
        if(pulseAnim!=null){pulseAnim.cancel();pulseAnim=null;}
        if(heroInfinity!=null){heroInfinity.setScaleX(1f);heroInfinity.setScaleY(1f);}
    }
    private void updateUpdateBanner(){
        if(updateBanner==null)return;
        updateBanner.removeAllViews();
        if(latestAvailable==null||latestAvailable.code<=BuildConfig.VERSION_CODE){updateBanner.setVisibility(View.GONE);return;}
        updateBanner.setVisibility(View.VISIBLE);
        updateBanner.setBackground(pillBackground(GOLD,0x22D9BD83,14,1));
        updateBanner.setPadding(dp(16),dp(10),dp(16),dp(10));
        TextView msg=label("⚡ Update available: v"+latestAvailable.version,13,GOLD);
        msg.setTypeface(body,Typeface.BOLD);
        updateBanner.addView(msg,new LinearLayout.LayoutParams(0,-2,1));
        TextView act=label("Install Now ›",13,TEXT);
        act.setTypeface(body,Typeface.BOLD);
        updateBanner.addView(act);
        updateBanner.setOnClickListener(v->startActivity(new Intent(this,UpdateActivity.class)));
    }
    private void checkForUpdates(boolean manual){
        if(checkingUpdate)return;
        checkingUpdate=true;
        if(manual)feedback.setText("Checking for updates…");
        CoreRuntime.IO.execute(()->{
            try{
                UpdateActivity.Info info=UpdateActivity.latest();
                main.post(()->{
                    checkingUpdate=false;
                    if(isDestroyed())return;
                    if(info.code>BuildConfig.VERSION_CODE){
                        latestAvailable=info;
                        updateUpdateBanner();
                        if(manual){
                            new AlertDialog.Builder(this)
                                .setTitle("Update Available")
                                .setMessage("III85III VPN v"+info.version+" is available.\nWould you like to install it now?")
                                .setPositiveButton("Update Now",(d,w)->startActivity(new Intent(this,UpdateActivity.class)))
                                .setNegativeButton("Later",null)
                                .show();
                        }
                    }else{
                        if(manual)feedback.setText("You are on the latest version (v"+BuildConfig.VERSION_NAME+").");
                    }
                });
            }catch(Exception err){
                main.post(()->{
                    checkingUpdate=false;
                    if(manual&&!isDestroyed())showError("Unable to check for updates: "+err.getMessage());
                });
            }
        });
    }
    private void showError(String detail){
        if(isDestroyed()||isFinishing())return;
        String message=detail==null?"":detail;
        String summary=message.toLowerCase(Locale.ROOT).contains("account")||message.toLowerCase(Locale.ROOT).contains("sign-in")?"Your account needs attention. Check your sign-in and try again.":"We couldn't complete that. Check your connection and try again.";
        feedback.setText(summary+"  ·  Tap for details");
        Runnable display=()->{
            LinearLayout panel=new LinearLayout(this);panel.setOrientation(LinearLayout.VERTICAL);panel.setPadding(dp(24),dp(12),dp(24),dp(8));
            panel.addView(label(summary,16,TEXT));
            TextView technical=label(message,13,MUTED);technical.setTextIsSelectable(true);technical.setVisibility(View.GONE);
            ScrollView scroll=new ScrollView(this);scroll.addView(technical);scroll.setVisibility(View.GONE);
            panel.addView(button("Technical details",()->{scroll.setVisibility(scroll.getVisibility()==View.GONE?View.VISIBLE:View.GONE);technical.setVisibility(View.VISIBLE);}));panel.addView(scroll,new LinearLayout.LayoutParams(-1,dp(140)));
            new AlertDialog.Builder(this).setTitle("Connection problem").setView(panel).setPositiveButton("Got it",null).show();
        };
        feedback.setOnClickListener(v->display.run());display.run();
    }
    private void heading(String title,String subtitle){TextView h=label(title,24,TEXT);h.setTypeface(body,Typeface.BOLD);h.setPadding(0,0,0,dp(8));content.addView(h);if(subtitle.isEmpty())return;TextView sub=label(subtitle,13,MUTED);sub.setPadding(0,dp(4),0,dp(16));content.addView(sub);}
    private void note(String text){if(text.isEmpty())return;TextView view=label(text,13,MUTED);view.setPadding(0,dp(6),0,dp(10));content.addView(view);}
    private void api(String method,String path,JSONObject body,int timeout,Reply reply){
        if(!ready){feedback.setText("Your local engine is starting.");return;}
        feedback.setText("Working…");
        CoreRuntime.IO.execute(()->{try{JSONObject value=CoreRuntime.request(method,path,body,timeout);main.post(()->{if(!isDestroyed()){feedback.setText("");feedback.setOnClickListener(null);reply.accept(value);}});}catch(Exception error){main.post(()->{if(!isDestroyed())showError(error.getMessage());});}});
    }
    private JSONObject object(String key,Object value){JSONObject obj=new JSONObject();try{obj.put(key,value);}catch(JSONException impossible){throw new IllegalStateException(impossible);}return obj;}
    private void showPage(int target){
        page=target;nav.removeAllViews();String[] names={"VPN","Servers","Settings"};int[] tabs={0,1,4};
        for(int i=0;i<names.length;i++){
            final int tab=tabs[i];boolean selected=tab==page||(tab==4&&page>=2);
            Button item=button(names[i],()->showPage(tab));
            item.setTextColor(selected?BG:MUTED);
            item.setTypeface(body,selected?Typeface.BOLD:Typeface.NORMAL);
            item.setBackground(background(selected?GOLD:BG));
            LinearLayout.LayoutParams params=new LinearLayout.LayoutParams(0,dp(48),1);
            params.setMargins(dp(3),dp(4),dp(3),dp(4));
            item.setLayoutParams(params);
            nav.addView(item);
        }
        content.removeAllViews();statusText=detailText=speedText=server=null;power=null;heroInfinity=null;statusDot=null;updateBanner=null;
        switch(page){case 0:home();break;case 1:locationPage();break;case 2:appRules();break;case 3:accounts();break;case 4:settings();break;default:help();}
    }
    private void home(){
        updateBanner=new LinearLayout(this);
        updateBanner.setOrientation(LinearLayout.HORIZONTAL);
        updateBanner.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout.LayoutParams ubParams=new LinearLayout.LayoutParams(-1,-2);
        ubParams.setMargins(0,0,0,dp(14));
        updateBanner.setLayoutParams(ubParams);
        content.addView(updateBanner);
        updateUpdateBanner();

        heroInfinity=new ImageView(this);
        heroInfinity.setImageResource(R.drawable.infinity_loop);
        heroInfinity.setScaleType(ImageView.ScaleType.FIT_CENTER);
        heroInfinity.setAdjustViewBounds(true);
        LinearLayout.LayoutParams imgParams=new LinearLayout.LayoutParams(dp(220),dp(130));
        imgParams.gravity=Gravity.CENTER_HORIZONTAL;
        imgParams.setMargins(0,dp(6),0,dp(18));
        heroInfinity.setLayoutParams(imgParams);
        content.addView(heroInfinity);

        LinearLayout statusCapsule=new LinearLayout(this);
        statusCapsule.setOrientation(LinearLayout.HORIZONTAL);
        statusCapsule.setGravity(Gravity.CENTER);
        statusCapsule.setPadding(dp(18),dp(7),dp(18),dp(7));
        statusCapsule.setBackground(pillBackground(0x339FA7B4,0x1A1B2028,20,1));
        LinearLayout.LayoutParams capParams=new LinearLayout.LayoutParams(-2,-2);
        capParams.gravity=Gravity.CENTER_HORIZONTAL;
        capParams.setMargins(0,0,0,dp(12));
        statusCapsule.setLayoutParams(capParams);

        statusDot=new View(this);
        GradientDrawable dotBg=new GradientDrawable();
        dotBg.setShape(GradientDrawable.OVAL);
        dotBg.setColor(MUTED);
        statusDot.setBackground(dotBg);
        LinearLayout.LayoutParams dotParams=new LinearLayout.LayoutParams(dp(8),dp(8));
        dotParams.setMargins(0,0,dp(8),0);
        statusDot.setLayoutParams(dotParams);
        statusCapsule.addView(statusDot);

        statusText=label("DISCONNECTED",13,MUTED);
        statusText.setTypeface(body,Typeface.BOLD);
        statusText.setLetterSpacing(0.06f);
        statusCapsule.addView(statusText);
        content.addView(statusCapsule);

        detailText=label("",12,MUTED);
        detailText.setGravity(Gravity.CENTER);
        detailText.setPadding(0,0,0,dp(12));
        detailText.setVisibility(View.GONE);
        content.addView(detailText);

        power=new Button(this);
        power.setText("CONNECT");
        power.setAllCaps(false);
        power.setTextSize(16);
        power.setTypeface(body,Typeface.BOLD);
        power.setTextColor(BG);
        power.setBackground(background(GOLD));
        LinearLayout.LayoutParams pwrParams=new LinearLayout.LayoutParams(-1,dp(56));
        pwrParams.setMargins(0,dp(4),0,dp(14));
        power.setLayoutParams(pwrParams);
        power.setOnClickListener(v->toggleConnection());
        content.addView(power);

        LinearLayout locationCard=new LinearLayout(this);
        locationCard.setOrientation(LinearLayout.HORIZONTAL);
        locationCard.setGravity(Gravity.CENTER_VERTICAL);
        locationCard.setPadding(dp(18),dp(14),dp(18),dp(14));
        locationCard.setBackground(new android.graphics.drawable.RippleDrawable(android.content.res.ColorStateList.valueOf(0x22D9BD83),cardBackground(SURFACE,16),background(Color.WHITE)));
        LinearLayout.LayoutParams locCardParams=new LinearLayout.LayoutParams(-1,-2);
        locCardParams.setMargins(0,0,0,dp(12));
        locationCard.setLayoutParams(locCardParams);

        LinearLayout locCol=new LinearLayout(this);
        locCol.setOrientation(LinearLayout.VERTICAL);
        TextView locTitle=label("LOCATION",11,GOLD);
        locTitle.setTypeface(body,Typeface.BOLD);
        locTitle.setLetterSpacing(0.06f);
        server=label("Automatic",15,TEXT);
        server.setTypeface(body,Typeface.BOLD);
        locCol.addView(locTitle);
        locCol.addView(server);
        locationCard.addView(locCol,new LinearLayout.LayoutParams(0,-2,1));

        TextView changeAction=label("Change ›",13,MUTED);
        locationCard.addView(changeAction);
        locationCard.setOnClickListener(v->showPage(1));
        content.addView(locationCard);

        LinearLayout speedCard=new LinearLayout(this);
        speedCard.setOrientation(LinearLayout.HORIZONTAL);
        speedCard.setGravity(Gravity.CENTER);
        speedCard.setPadding(dp(18),dp(12),dp(18),dp(12));
        speedCard.setBackground(cardBackground(SURFACE,14));
        LinearLayout.LayoutParams speedCardParams=new LinearLayout.LayoutParams(-1,-2);
        speedCardParams.setMargins(0,0,0,dp(10));
        speedCard.setLayoutParams(speedCardParams);

        speedText=label("↓ 0 B/s      ↑ 0 B/s",13,TEXT);
        speedText.setTypeface(body);
        speedCard.addView(speedText);
        content.addView(speedCard);

        updateHome();
    }
    private void toggleConnection(){
        if(UpdateActivity.isRequired(this)){UpdateActivity.openRequired(this);return;}
        if(!ready){feedback.setText("Your local engine is starting.");return;}
        if(checkingAccount){checkingAccount=false;connectAttempt++;updateHome();feedback.setText("Connection cancelled.");return;}
        if(TunnelService.lifecycle.canCancel()){startService(new Intent(this,TunnelService.class).setAction(TunnelService.STOP));statusText.setText("Disconnecting…");power.setText("Disconnecting…");main.postDelayed(this::poll,100);return;}
        if(TunnelService.lifecycle.phase()==VpnLifecycle.Phase.STOPPING){feedback.setText("Finishing the previous connection. Try again in a moment.");return;}
        checkingAccount=true;final int attempt=++connectAttempt;updateHome();feedback.setText("Checking your account…");
        CoreRuntime.IO.execute(()->{try{JSONObject reply=CoreRuntime.request("GET","/api/accounts",null,5000);main.post(()->{if(isDestroyed()||attempt!=connectAttempt)return;checkingAccount=false;updateHome();JSONArray rows=reply.optJSONArray("accounts");if(rows==null||rows.length()==0){showPage(3);feedback.setText("Add your Mozilla account to get started.");return;}feedback.setText("");explainConnection();});}catch(Exception error){main.post(()->{if(isDestroyed()||attempt!=connectAttempt)return;checkingAccount=false;updateHome();showError(error.getMessage());});}});
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
    private void showAccountIntro(){
        android.content.SharedPreferences prefs=getSharedPreferences("interface",MODE_PRIVATE);
        if(checkingAccountIntro||accountIntroShowing||prefs.getBoolean("account_intro_seen",false)||UpdateActivity.isRequired(this))return;
        checkingAccountIntro=true;
        CoreRuntime.IO.execute(()->{try{JSONObject reply=CoreRuntime.request("GET","/api/accounts",null,5000);JSONArray accounts=reply.optJSONArray("accounts");int count=accounts==null?-1:accounts.length();main.post(()->{
            checkingAccountIntro=false;if(isDestroyed()||!visible||!AccountSetup.shouldShow(prefs.getBoolean("account_intro_seen",false),count,UpdateActivity.isRequired(this)))return;
            accountIntroShowing=true;
            AlertDialog intro=new AlertDialog.Builder(this).setTitle("Your Firefox account").setMessage(AccountSetup.MESSAGE)
                .setPositiveButton("Got it",(d,w)->prefs.edit().putBoolean("account_intro_seen",true).apply()).create();
            intro.setOnCancelListener(d->{if(!UpdateActivity.isRequired(this))prefs.edit().putBoolean("account_intro_seen",true).apply();});
            intro.setOnDismissListener(d->accountIntroShowing=false);intro.show();
        });}catch(Exception ignored){main.post(()->checkingAccountIntro=false);}});
    }
    private void startTunnel(){
        if(Build.VERSION.SDK_INT>=33&&checkSelfPermission("android.permission.POST_NOTIFICATIONS")!=android.content.pm.PackageManager.PERMISSION_GRANTED)requestPermissions(new String[]{"android.permission.POST_NOTIFICATIONS"},86);
        Intent intent=new Intent(this,TunnelService.class);startForegroundService(intent);feedback.setText("Starting the VPN connection…");if(statusText!=null){statusText.setText("Connecting…");power.setText("Cancel connection");}main.postDelayed(this::poll,100);
    }
    @Override protected void onActivityResult(int request,int result,Intent value){super.onActivityResult(request,result,value);if(BuildConfig.DEBUG&&request==185&&result==RESULT_OK)startForegroundService(new Intent(this,TunnelService.class).putExtra("qa_tun",true));if(request==85){if(result==RESULT_OK)startTunnel();else feedback.setText("VPN permission was not granted. Your connection is unchanged.");}}
    private final Runnable pollAgain=this::poll;
    private void poll(){
        if(!visible||!ready||polling)return;polling=true;
        CoreRuntime.IO.execute(()->{try{JSONObject value=CoreRuntime.request("GET","/api/status",null,4000);main.post(()->{if(!isDestroyed()){status=value;updateHome();warmConnection();}});}catch(Exception ignored){main.post(()->{if(!isDestroyed()){status=new JSONObject();updateHome();feedback.setText("Connection status is unavailable. Check the VPN notification or try again.");}});}finally{main.post(()->{polling=false;if(visible&&!isDestroyed()){main.removeCallbacks(pollAgain);main.postDelayed(pollAgain,checkingAccount||TunnelService.lifecycle.phase()==VpnLifecycle.Phase.STARTING||TunnelService.lifecycle.phase()==VpnLifecycle.Phase.STOPPING?350:1000);}});}});
    }
    private boolean verified(){JSONObject state=status.optJSONObject("state"),test=status.optJSONObject("last_test");return ConnectionTruth.verified(TunnelService.active,state==null?"off":state.optString("phase"),test!=null&&test.optBoolean("ok"),test==null?"":test.optString("at"),state==null?"":state.optString("since"),status.optString("network_changed_at"));}
    private void warmConnection(){if(!TunnelService.active||verified()||warming||SystemClock.elapsedRealtime()<nextWarmup)return;warming=true;nextWarmup=SystemClock.elapsedRealtime()+30000;CoreRuntime.IO.execute(()->{try{CoreRuntime.request("POST","/api/test",new JSONObject(),20000);}catch(Exception ignored){}finally{main.post(()->warming=false);}});}
    private void updateHome(){
        if(page!=0||statusText==null)return;
        JSONObject state=status.optJSONObject("state"),test=status.optJSONObject("last_test");
        String phase=state==null?"off":state.optString("phase","off");
        boolean verified=verified();
        String selected=status.optString("selection_name");
        if(server!=null)server.setText(selected.isEmpty()?"Automatic":selected);
        String problem=connectionProblem(status);
        VpnLifecycle.Phase transition=TunnelService.lifecycle.phase();

        GradientDrawable dotBg=new GradientDrawable();
        dotBg.setShape(GradientDrawable.OVAL);

        if(checkingAccount){
            statusText.setText("CHECKING ACCOUNT…");
            statusText.setTextColor(GOLD);
            dotBg.setColor(GOLD);
            if(statusDot!=null)statusDot.setBackground(dotBg);
            detailText.setVisibility(View.GONE);
            power.setText("CANCEL");
            power.setTextColor(GOLD);
            power.setBackground(pillBackground(GOLD,SURFACE,28,2));
            startInfinityBreathing();
        }else if(verified){
            statusText.setText("PROTECTED");
            statusText.setTextColor(GREEN);
            dotBg.setColor(GREEN);
            if(statusDot!=null)statusDot.setBackground(dotBg);
            detailText.setVisibility(View.GONE);
            power.setText("DISCONNECT");
            power.setTextColor(GOLD);
            power.setBackground(pillBackground(GOLD,SURFACE,28,2));
            startInfinityBreathing();
        }else if(!problem.isEmpty()){
            statusText.setText("ATTENTION REQUIRED");
            statusText.setTextColor(AMBER);
            dotBg.setColor(AMBER);
            if(statusDot!=null)statusDot.setBackground(dotBg);
            detailText.setText(problem+"  ·  Tap for details");
            detailText.setVisibility(View.VISIBLE);
            detailText.setOnClickListener(v->showError(problem));
            power.setText("RECONNECT");
            power.setTextColor(BG);
            power.setBackground(background(GOLD));
            stopInfinityBreathing();
        }else if(transition==VpnLifecycle.Phase.STOPPING){
            statusText.setText("DISCONNECTING…");
            statusText.setTextColor(MUTED);
            dotBg.setColor(MUTED);
            if(statusDot!=null)statusDot.setBackground(dotBg);
            detailText.setVisibility(View.GONE);
            power.setText("DISCONNECTING…");
            power.setTextColor(MUTED);
            power.setBackground(background(SURFACE));
            stopInfinityBreathing();
        }else if(TunnelService.active||transition==VpnLifecycle.Phase.STARTING){
            statusText.setText("CONNECTING…");
            statusText.setTextColor(GOLD);
            dotBg.setColor(GOLD);
            if(statusDot!=null)statusDot.setBackground(dotBg);
            detailText.setVisibility(View.GONE);
            power.setText("CANCEL");
            power.setTextColor(GOLD);
            power.setBackground(pillBackground(GOLD,SURFACE,28,2));
            startInfinityBreathing();
        }else{
            statusText.setText("DISCONNECTED");
            statusText.setTextColor(MUTED);
            dotBg.setColor(MUTED);
            if(statusDot!=null)statusDot.setBackground(dotBg);
            detailText.setVisibility(View.GONE);
            power.setText("CONNECT");
            power.setTextColor(BG);
            power.setBackground(background(GOLD));
            stopInfinityBreathing();
        }
        power.setEnabled(transition!=VpnLifecycle.Phase.STOPPING);
        try{
            String[] rate=Mobile.speed().split(",");
            speedText.setText("↓ "+bytes(Double.parseDouble(rate[0]))+"/s      ↑ "+bytes(Double.parseDouble(rate[1]))+"/s");
        }catch(Exception ignored){}
    }
    private String bytes(double amount){String[] unit={"B","KB","MB","GB"};int n=0;while(amount>=1000&&n<3){amount/=1000;n++;}return String.format(Locale.US,n==0?"%.0f %s":"%.1f %s",amount,unit[n]);}
    static String connectionProblem(JSONObject value){
        JSONObject state=value.optJSONObject("state"),proxy=value.optJSONObject("proxy");String failure=value.optString("connect_failure");
        if(state!=null&&(state.optString("phase").equals("error")||state.optString("code").equals("no_usable_account")||state.optString("code").equals("no_accounts")))return state.optString("message","Your saved account is not available. Check Accounts for details.");
        if(proxy!=null&&!proxy.optString("wake_error").isEmpty())return proxy.optString("wake_error");
        JSONObject test=value.optJSONObject("last_test");if(test!=null&&state!=null&&!test.optBoolean("ok")&&ConnectionTruth.fresh(test.optString("at"),state.optString("since"),value.optString("network_changed_at"))&&!test.optString("error").isEmpty())return "Connection check: "+test.optString("error");
        if(!failure.isEmpty())return "Connection could not complete ("+failure+"). Check your account and network, then retry.";
        return "";
    }
    private void locationPage(){
        heading("Servers","");
        EditText search=new EditText(this);search.setSingleLine(true);search.setHint("Search country or code");search.setTextColor(TEXT);search.setHintTextColor(MUTED);content.addView(search);
        Button measure=button(pinging?"Testing…":"Test ping",()->{
            if(pinging)return;pinging=true;feedback.setText("Testing ping…");
            CoreRuntime.IO.execute(()->{try{CoreRuntime.request("POST","/api/locations/ping",object("codes",new JSONArray()),60000);}catch(Exception error){main.post(()->feedback.setText("Ping test failed. Try again."));}finally{main.post(()->{pinging=false;if(page==1)showPage(1);});}});
        });measure.setEnabled(!pinging);content.addView(measure);
        LinearLayout list=new LinearLayout(this);list.setOrientation(LinearLayout.VERTICAL);content.addView(list);
        Runnable render=()->{list.removeAllViews();String q=search.getText().toString().trim().toLowerCase(Locale.ROOT);List<JSONObject> values=new ArrayList<>();for(int i=0;i<locations.length();i++){JSONObject row=locations.optJSONObject(i);if(row!=null&&!LocationOrder.pinned(row.optString("code")))values.add(row);}values.sort((a,b)->LocationOrder.compare(a.optString("code"),a.optString("name"),a.optInt("ping_ms",0),b.optString("code"),b.optString("name"),b.optInt("ping_ms",0)));
            for(JSONObject row:values){String code=row.optString("code"),name=row.optString("name",code);if(!(name+" "+code).toLowerCase(Locale.ROOT).contains(q))continue;int ping=row.optInt("ping_ms",0);list.addView(button(name+(ping>0?"  ·  "+ping+" ms":""),()->api("POST","/api/location",object("country",code),10000,reply->{feedback.setText("Location selected: "+name);showPage(0);})));}
            if(list.getChildCount()==0){TextView empty=label("No matching locations yet.",14,MUTED);list.addView(empty);}
        };
        search.addTextChangedListener(new TextWatcher(){public void beforeTextChanged(CharSequence s,int start,int count,int after){}public void onTextChanged(CharSequence s,int start,int before,int count){render.run();}public void afterTextChanged(Editable value){}});
        content.addView(button("Recommended",()->api("POST","/api/location",object("country",""),10000,reply->{showPage(0);})),1);
        content.addView(button("Fastest",()->api("POST","/api/location",object("country","fastest"),60000,reply->{showPage(0);})),2);
        api("GET","/api/locations",null,15000,reply->{JSONArray rows=reply.optJSONArray("locations");locations=rows==null?new JSONArray():rows;if(page==1)render.run();});render.run();
    }
    private void accounts(){
        heading("Accounts","Sign in with Mozilla.");
        content.addView(button("Add an account",()->{if(signIn==null||signIn.closed)signIn=new SignInFlow();}));
        boolean hidden=getPreferences(MODE_PRIVATE).getBoolean("hide_email",true);
        content.addView(button(hidden?"Show email addresses":"Hide email addresses",()->{getPreferences(MODE_PRIVATE).edit().putBoolean("hide_email",!hidden).apply();showPage(3);}));
        LinearLayout rows=new LinearLayout(this);rows.setOrientation(LinearLayout.VERTICAL);content.addView(rows);
        api("GET","/api/accounts",null,7000,reply->{if(page!=3)return;JSONArray data=reply.optJSONArray("accounts");if(data==null||data.length()==0){note("No accounts yet. Your account session stays in this app's private storage.");return;}
            for(int i=0;i<data.length();i++){JSONObject account=data.optJSONObject(i);String id=account.optString("id"),email=account.optString("email");JSONObject usage=account.optJSONObject("usage");String quota=usage==null?"Quota not checked":usage.optBoolean("unlimited")?"Unlimited":bytes(usage.optDouble("remaining_bytes",0))+" remaining";String reset=usage==null?"":usage.optString("reset_at");
                rows.addView(label((hidden?"••••••••":email)+"\n"+account.optString("status")+" · "+quota+(reset.isEmpty()?"":"\nRenews: "+reset),15,TEXT));
                rows.addView(button("Refresh balance",()->api("POST","/api/accounts/"+id+"/refresh",new JSONObject(),15000,r->{if(page==3)showPage(3);})));
                String reason=account.optString("reason_detail",account.optString("reason"));if(!reason.isEmpty())rows.addView(label(hidden&&!email.isEmpty()?reason.replace(email,"••••••••"):reason,14,MUTED));
                rows.addView(button(account.optBoolean("enabled",true)?"Use this account":"Enable and use this account",()->api("POST","/api/accounts/"+id+"/enabled",object("enabled",true),7000,enabled->api("POST","/api/accounts/"+id+"/activate",new JSONObject(),7000,selected->{feedback.setText("Account selected. You can connect from VPN.");showPage(0);} ))));
                rows.addView(button("Remove account",()->new AlertDialog.Builder(this).setTitle("Remove this account?").setMessage("This removes its stored session from this device.").setNegativeButton("Cancel",null).setPositiveButton("Remove",(d,w)->api("DELETE","/api/accounts/"+id,null,7000,r->{if(page==3)showPage(3);})).show()));
            }
        });
    }
    private void appRules(){
        heading("App rules","Selected apps bypass VPN. Reconnect to apply.");
        Set<String> selected=new HashSet<>(getSharedPreferences("interface",MODE_PRIVATE).getStringSet("direct_apps",Collections.emptySet()));
        content.addView(button("Save",()->{getSharedPreferences("interface",MODE_PRIVATE).edit().putStringSet("direct_apps",selected).apply();feedback.setText(TunnelService.active?"Saved. Reconnect to apply.":"Saved.");}));
        LinearLayout apps=new LinearLayout(this);apps.setOrientation(LinearLayout.VERTICAL);content.addView(apps);
        CoreRuntime.IO.execute(()->{
            Intent launcher=new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER);
            List<android.content.pm.ResolveInfo> entries=getPackageManager().queryIntentActivities(launcher,0);entries.sort((a,b)->a.loadLabel(getPackageManager()).toString().compareToIgnoreCase(b.loadLabel(getPackageManager()).toString()));
            main.post(()->{if(isDestroyed()||page!=2)return;Set<String> seen=new HashSet<>();for(android.content.pm.ResolveInfo entry:entries){String pkg=entry.activityInfo.packageName;if(pkg.equals(getPackageName())||!seen.add(pkg))continue;CheckBox check=new CheckBox(this);check.setText(entry.loadLabel(getPackageManager()));check.setTextColor(TEXT);check.setTypeface(body);check.setChecked(selected.contains(pkg));check.setOnCheckedChangeListener((b,value)->{if(value)selected.add(pkg);else selected.remove(pkg);});apps.addView(check);}if(apps.getChildCount()==0)apps.addView(label("No visible launcher apps found.",14,MUTED));});
        });
    }
    private void settings(){
        heading("Settings","");
        content.addView(button("⚡ Check for Updates  (v"+BuildConfig.VERSION_NAME+")",()->checkForUpdates(true)));
        content.addView(button("Mozilla accounts",()->showPage(3)));
        content.addView(button("App rules (Bypass)",()->showPage(2)));
        content.addView(button("Connection diagnostics",()->api("POST","/api/test",new JSONObject(),20000,reply->feedback.setText(reply.optBoolean("ok")?"Connection verified.":"Connection could not be verified."))));
        content.addView(button("About",()->new AlertDialog.Builder(this)
            .setTitle("III85III VPN")
            .setMessage("Version "+BuildConfig.VERSION_NAME+"\nSecurity-hardened engine.\nDirect in-app updates enabled.")
            .setPositiveButton("Done",null)
            .setNeutralButton("Privacy",(d,w)->openDocument("privacy"))
            .setNegativeButton("Licenses",(d,w)->openDocument("licenses"))
            .show()));
    }
    private void help(){heading("Help","");content.addView(button("Check connection",()->api("POST","/api/test",new JSONObject(),20000,reply->feedback.setText(reply.optBoolean("ok")?"Connection verified.":"Connection could not be verified. Check your account and network."))));content.addView(button("Test ping",()->showPage(1)));note("Try another server or check your Mozilla account.");note("App rules exclude selected apps from the VPN.");}

    private final class SignInFlow {
        boolean closed,pending;String session="",promptStamp="",answeredStamp="";AlertDialog dialog;EditText password;
        final Runnable pollFlow=()->fetch();
        SignInFlow(){
            LinearLayout fields=new LinearLayout(MainActivity.this);fields.setPadding(dp(24),dp(8),dp(24),dp(8));fields.setOrientation(LinearLayout.VERTICAL);
            EditText email=new EditText(MainActivity.this);email.setHint("Mozilla email");email.setSingleLine(true);email.setInputType(android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS);email.setTypeface(body);fields.addView(email);
            password=new EditText(MainActivity.this);password.setHint("Mozilla password");password.setSingleLine(true);password.setInputType(129);password.setTransformationMethod(PasswordTransformationMethod.getInstance());password.setTypeface(body);fields.addView(password);
            dialog=new AlertDialog.Builder(MainActivity.this).setTitle("Welcome to III85III").setMessage("Sign in with your own Mozilla account.").setView(fields).setNegativeButton("Cancel",(d,w)->cancel()).setPositiveButton("Sign in",null).create();dialog.setOnCancelListener(d->cancel());dialog.setOnShowListener(d->dialog.getButton(-1).setOnClickListener(v->{
                if(pending||closed)return;if(email.getText().toString().trim().isEmpty()||password.length()==0){feedback.setText("Enter your Mozilla email and password.");return;}
                JSONObject body=object("email",email.getText().toString().trim());try{body.put("password",password.getText().toString());}catch(JSONException ignored){}password.getText().clear();pending=true;dialog.getButton(-1).setEnabled(false);
                CoreRuntime.IO.execute(()->{try{JSONObject reply=CoreRuntime.request("POST","/api/signin",body,60000);String id=reply.optString("id");main.post(()->{pending=false;session=id;if(closed){cancelSession(id);return;}dialog.dismiss();verification();apply(reply);});}catch(Exception error){main.post(()->{pending=false;if(!closed){showError(error.getMessage());dialog.getButton(-1).setEnabled(true);}});}});
            }));dialog.show();
        }
        void verification(){dialog=new AlertDialog.Builder(MainActivity.this).setTitle("Checking your sign-in").setMessage("Waiting for Mozilla…").setNegativeButton("Cancel",(d,w)->cancel()).create();dialog.setOnCancelListener(d->cancel());dialog.show();}
        void apply(JSONObject state){
            if(closed)return;String step=state.optString("step");
            if(step.startsWith("need_")&&!answeredStamp.isEmpty()&&answeredStamp.equals(state.optString("updated"))){dialog.dismiss();verification();main.removeCallbacks(pollFlow);main.postDelayed(pollFlow,400);return;}
            if(step.equals("done")){closed=true;main.removeCallbacks(pollFlow);dialog.dismiss();feedback.setText("Account added.");if(page==3)showPage(3);return;}
            if(step.equals("error")||step.equals("failed")||step.equals("cancelled")){feedback.setText(state.optString("error",state.optString("message","Sign-in could not finish.")));cancel();return;}
            boolean code=step.equals("need_email_code")||step.equals("need_totp")||step.equals("need_unblock_code")||step.equals("need_captcha");
            if(code){
                promptStamp=state.optString("updated");
                dialog.dismiss();LinearLayout fields=new LinearLayout(MainActivity.this);fields.setOrientation(LinearLayout.VERTICAL);fields.setPadding(dp(24),dp(8),dp(24),dp(8));
                String captcha=state.optString("captcha");if(step.equals("need_captcha")&&captcha.startsWith("data:image/")&&captcha.length()<1500000){try{byte[] image=android.util.Base64.decode(captcha.substring(captcha.indexOf(',')+1),android.util.Base64.DEFAULT);ImageView view=new ImageView(MainActivity.this);view.setImageBitmap(android.graphics.BitmapFactory.decodeByteArray(image,0,image.length));fields.addView(view);}catch(Exception ignored){}}
                EditText input=new EditText(MainActivity.this);input.setSingleLine(true);input.setTypeface(body);input.setHint(step.equals("need_captcha")?"Verification characters":"Verification code");fields.addView(input);
                String instruction=state.optString("message","Enter the code requested by Mozilla.");
                if(!state.optString("error").isEmpty())instruction+="\n\n"+state.optString("error");
                dialog=new AlertDialog.Builder(MainActivity.this).setTitle(step.equals("need_totp")?"Enter your security code":step.equals("need_captcha")?"Confirm your sign-in":"Check your email").setMessage(instruction).setView(fields).setNegativeButton("Cancel",(d,w)->cancel()).setPositiveButton("Verify",null).create();
                dialog.setOnCancelListener(d->cancel());
                dialog.setOnShowListener(d->dialog.getButton(-1).setOnClickListener(v->{
                    if(pending||input.length()==0)return;
                    pending=true;answeredStamp=promptStamp;dialog.getButton(-1).setEnabled(false);
                    String value=input.getText().toString().trim();input.getText().clear();
                    CoreRuntime.IO.execute(()->{
                        try{
                            JSONObject reply;
                            try{reply=CoreRuntime.request("POST","/api/signin/"+session,object("value",value),30000);}
                            catch(CoreRuntime.ApiFailure conflict){if(conflict.status!=409)throw conflict;reply=CoreRuntime.request("GET","/api/signin/"+session,null,5000);}
                            JSONObject current=reply;main.post(()->{pending=false;if(!closed)apply(current);});
                        }catch(Exception error){main.post(()->{pending=false;if(!closed){answeredStamp="";showError(error.getMessage());dialog.getButton(-1).setEnabled(true);}});}
                    });
                }));dialog.show();
            }else{dialog.setMessage(state.optString("message","Waiting for Mozilla…"));main.removeCallbacks(pollFlow);main.postDelayed(pollFlow,1500);}
        }
        void fetch(){if(closed||pending)return;pending=true;CoreRuntime.IO.execute(()->{try{JSONObject state=CoreRuntime.request("GET","/api/signin/"+session,null,5000);main.post(()->{pending=false;apply(state);});}catch(Exception error){main.post(()->{pending=false;if(!closed){feedback.setText("Sign-in status could not refresh. You can cancel and retry.");main.postDelayed(pollFlow,2500);}});}});}
        void cancelSession(String id){if(id.isEmpty())return;CoreRuntime.IO.execute(()->{try{CoreRuntime.request("DELETE","/api/signin/"+id,null,3000);}catch(Exception ignored){}});}
        void cancel(){if(closed)return;closed=true;main.removeCallbacks(pollFlow);if(password!=null)password.getText().clear();if(dialog!=null)dialog.dismiss();cancelSession(session);}
    }
}
