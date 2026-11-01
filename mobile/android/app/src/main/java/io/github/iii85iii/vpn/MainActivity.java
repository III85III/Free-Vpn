package io.github.iii85iii.vpn;

import android.animation.ObjectAnimator;
import android.animation.ValueAnimator;
import android.app.*;
import android.content.*;
import android.content.pm.ApplicationInfo;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
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
    private final Set<String> autoSyncedAccounts=new HashSet<>();
    private LinearLayout content,nav;
    private FrameLayout adContainer;
    private TextView statusText,detailText,speedText,feedback,server,quotaSummary;
    private Button power;
    private InfinityLoopView heroInfinity;
    private View statusDot;
    private LinearLayout updateBanner,speedCard;
    private ProgressBar quotaBar;
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

        TextView contact=label("Support \u2197",12,GOLD);
        contact.setPadding(dp(14),dp(6),dp(14),dp(6));
        contact.setBackground(pillBackground(0x2B9FA7B4,0x14D9BD83,14,1));
        contact.setContentDescription("Telegram support");
        contact.setOnClickListener(v->{v.performHapticFeedback(android.view.HapticFeedbackConstants.VIRTUAL_KEY);openTelegramChannel("cabicy");});
        header.addView(contact);root.addView(header);

        long annExpiry = 1793664000000L;
        boolean annDismissed = getSharedPreferences("prefs", MODE_PRIVATE).getBoolean("adblock_ann_dismissed", false);
        if (System.currentTimeMillis() < annExpiry && !annDismissed) {
            LinearLayout annBar = new LinearLayout(this);
            annBar.setOrientation(LinearLayout.HORIZONTAL);
            annBar.setGravity(Gravity.CENTER_VERTICAL);
            annBar.setBackground(pillBackground(0x442ED175, 0x1F2ED175, 12, 1));
            annBar.setPadding(dp(12), dp(6), dp(10), dp(6));
            LinearLayout.LayoutParams annLp = new LinearLayout.LayoutParams(-1, -2);
            annLp.setMargins(0, dp(2), 0, dp(6));
            annBar.setLayoutParams(annLp);

            TextView annText = label("\uD83D\uDEE1\uFE0F AdBlocker \u0641\u0639\u0627\u0644 \u0634\u062F: \u062A\u0628\u0644\u06CC\u063A\u0627\u062A \u0627\u0633\u067E\u0627\u062A\u06CC\u0641\u0627\u06CC \u0648 \u0628\u0631\u0646\u0627\u0645\u0647\u200C\u0647\u0627 \u0645\u0633\u062F\u0648\u062F \u0634\u062F\u0646\u062F", 12, TEXT);
            annText.setLayoutParams(new LinearLayout.LayoutParams(0, -2, 1f));
            annBar.addView(annText);

            TextView annClose = label(" \u2715 ", 13, MUTED);
            annClose.setPadding(dp(6), dp(4), dp(4), dp(4));
            annClose.setOnClickListener(v -> {
                annBar.setVisibility(View.GONE);
                getSharedPreferences("prefs", MODE_PRIVATE).edit().putBoolean("adblock_ann_dismissed", true).apply();
            });
            annBar.addView(annClose);
            root.addView(annBar);
        }
        nav=new LinearLayout(this);
        nav.setOrientation(LinearLayout.HORIZONTAL);
        nav.setGravity(Gravity.CENTER);
        nav.setBackground(pillBackground(0x249FA7B4,0xF0141720,24,1));
        nav.setPadding(dp(6),dp(5),dp(6),dp(5));
        LinearLayout.LayoutParams navLp=new LinearLayout.LayoutParams(-1,-2);
        navLp.setMargins(0,dp(6),0,dp(12));
        nav.setLayoutParams(navLp);
        ScrollView scroll=new ScrollView(this);scroll.setFillViewport(true);content=new LinearLayout(this);content.setOrientation(LinearLayout.VERTICAL);content.setPadding(0,dp(10),0,dp(16));scroll.addView(content);root.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));
        feedback=label("Starting your local engine\u2026",13,MUTED);feedback.setGravity(Gravity.CENTER);feedback.setPadding(0,dp(4),0,dp(6));root.addView(feedback);
        adContainer=new FrameLayout(this);
        LinearLayout.LayoutParams adLp=new LinearLayout.LayoutParams(-1,-2);
        adLp.setMargins(0,dp(2),0,dp(6));
        adContainer.setLayoutParams(adLp);
        root.addView(adContainer);
        AdManager.attachBanner(this,adContainer);
        root.addView(nav);setContentView(root);
        WindowLayout.apply(this,root);
        showPage(state==null?0:state.getInt("page",0));
        startTunnelCheck(getIntent());
        CoreRuntime.IO.execute(()->{try{CoreRuntime.ensure(this);main.post(()->{if(isDestroyed())return;ready=true;feedback.setText("");showPage(page);if(visible){poll();showAccountIntro();checkForUpdates(false);}});}catch(Exception failure){main.post(()->{if(!isDestroyed())feedback.setText("The local engine could not start. Close the app and try again.");});}});
    }
    @Override protected void onSaveInstanceState(Bundle state){super.onSaveInstanceState(state);state.putInt("page",page);}
    @Override protected void onNewIntent(Intent intent){super.onNewIntent(intent);startTunnelCheck(intent);}
    private void startTunnelCheck(Intent intent){if(BuildConfig.DEBUG&&intent!=null){if(intent.getBooleanExtra("qa_tun",false)){Intent permission=VpnService.prepare(this);if(permission==null)startForegroundService(new Intent(this,TunnelService.class).putExtra("qa_tun",true));else startActivityForResult(permission,185);}if(intent.getBooleanExtra("qa_update",false))startActivity(new Intent(this,UpdateActivity.class).putExtra("qa_update",true));}}
    @Override protected void onStart(){super.onStart();if(UpdateActivity.isRequired(this)){visible=false;UpdateActivity.openRequired(this);return;}visible=true;if(ready){poll();showAccountIntro();}main.postDelayed(updateAgain,1500);}
    @Override protected void onStop(){visible=false;main.removeCallbacks(pollAgain);main.removeCallbacks(updateAgain);super.onStop();}
    @Override protected void onDestroy(){if(signIn!=null)signIn.cancel();main.removeCallbacksAndMessages(null);super.onDestroy();}
    private TextView label(String value,int size,int color){TextView text=new TextView(this);text.setText(value);text.setTextSize(size);text.setTextColor(color);text.setTypeface(body);text.setLineSpacing(dp(4),1);return text;}
    private GradientDrawable background(int color){GradientDrawable shape=new GradientDrawable();shape.setColor(color);shape.setCornerRadius(dp(16));return shape;}
    private GradientDrawable cardBackground(int color,int radiusDp){GradientDrawable shape=new GradientDrawable();shape.setColor(color);shape.setCornerRadius(dp(radiusDp));return shape;}
    private GradientDrawable pillBackground(int strokeColor,int fillColor,int radiusDp,int strokeWidthDp){GradientDrawable shape=new GradientDrawable();shape.setColor(fillColor);shape.setCornerRadius(dp(radiusDp));if(strokeWidthDp>0)shape.setStroke(dp(strokeWidthDp),strokeColor);return shape;}
    private Button button(String title,Runnable action){Button b=new Button(this);b.setText(title);b.setAllCaps(false);b.setTextSize(15);b.setTypeface(body);b.setTextColor(TEXT);b.setBackground(new android.graphics.drawable.RippleDrawable(android.content.res.ColorStateList.valueOf(0x33D9BD83),background(SURFACE),background(Color.WHITE)));LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,dp(52));p.setMargins(0,dp(6),0,dp(6));b.setLayoutParams(p);b.setOnClickListener(v->action.run());return b;}
    private void setInfinityState(int state){
        if(heroInfinity!=null)heroInfinity.setState(state);
    }
    private void updateUpdateBanner(){
        if(updateBanner==null)return;
        updateBanner.removeAllViews();
        if(latestAvailable==null||latestAvailable.code<=BuildConfig.VERSION_CODE){updateBanner.setVisibility(View.GONE);return;}
        updateBanner.setVisibility(View.VISIBLE);
        updateBanner.setBackground(pillBackground(GOLD,0x22D9BD83,14,1));
        updateBanner.setPadding(dp(16),dp(10),dp(16),dp(10));
        TextView msg=label("\u26a1 Update available: v"+latestAvailable.version,13,GOLD);
        msg.setTypeface(body,Typeface.BOLD);
        updateBanner.addView(msg,new LinearLayout.LayoutParams(0,-2,1));
        TextView act=label("Install Now \u203a",13,TEXT);
        act.setTypeface(body,Typeface.BOLD);
        updateBanner.addView(act);
        updateBanner.setOnClickListener(v->startActivity(new Intent(this,UpdateActivity.class)));
    }
    private void checkForUpdates(boolean manual){
        if(checkingUpdate)return;
        checkingUpdate=true;
        if(manual)feedback.setText("Checking for updates\u2026");
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
        feedback.setText(summary+"  \u00b7  Tap for details");
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
        feedback.setText("Working\u2026");
        CoreRuntime.IO.execute(()->{try{JSONObject value=CoreRuntime.request(method,path,body,timeout);main.post(()->{if(!isDestroyed()){feedback.setText("");feedback.setOnClickListener(null);reply.accept(value);}});}catch(Exception error){main.post(()->{if(!isDestroyed())showError(error.getMessage());});}});
    }
    private JSONObject object(String key,Object value){JSONObject obj=new JSONObject();try{obj.put(key,value);}catch(JSONException impossible){throw new IllegalStateException(impossible);}return obj;}
    private void showPage(int target){
        page=target;nav.removeAllViews();String[] names={"\u26a1 VPN","\ud83c\udf10 Servers","\u2699 Settings"};int[] tabs={0,1,4};
        for(int i=0;i<names.length;i++){
            final int tab=tabs[i];boolean selected=tab==page||(tab==4&&page>=2);
            Button item=new Button(this);
            item.setText(names[i]);
            item.setAllCaps(false);
            item.setTextSize(13);
            item.setTypeface(body,selected?Typeface.BOLD:Typeface.NORMAL);
            item.setTextColor(selected?GOLD:MUTED);
            item.setBackground(selected?pillBackground(0x33D9BD83,0x24D9BD83,18,1):new android.graphics.drawable.ColorDrawable(Color.TRANSPARENT));
            LinearLayout.LayoutParams params=new LinearLayout.LayoutParams(0,dp(42),1);
            params.setMargins(dp(2),0,dp(2),0);
            item.setLayoutParams(params);
            item.setOnClickListener(v->{v.performHapticFeedback(android.view.HapticFeedbackConstants.VIRTUAL_KEY);showPage(tab);});
            nav.addView(item);
        }
        content.removeAllViews();statusText=detailText=speedText=server=null;power=null;heroInfinity=null;statusDot=null;updateBanner=null;quotaSummary=null;speedCard=null;quotaBar=null;
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

        heroInfinity=new InfinityLoopView(this);
        LinearLayout.LayoutParams imgParams=new LinearLayout.LayoutParams(dp(260),dp(140));
        imgParams.gravity=Gravity.CENTER_HORIZONTAL;
        imgParams.setMargins(0,dp(6),0,dp(16));
        heroInfinity.setLayoutParams(imgParams);
        content.addView(heroInfinity);

        LinearLayout statusCapsule=new LinearLayout(this);
        statusCapsule.setOrientation(LinearLayout.HORIZONTAL);
        statusCapsule.setGravity(Gravity.CENTER);
        statusCapsule.setPadding(dp(18),dp(7),dp(18),dp(7));
        statusCapsule.setBackground(pillBackground(0x339FA7B4,0x1A1B2028,20,1));
        LinearLayout.LayoutParams capParams=new LinearLayout.LayoutParams(-2,-2);
        capParams.gravity=Gravity.CENTER_HORIZONTAL;
        capParams.setMargins(0,0,0,dp(14));
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
        power.setLetterSpacing(0.08f);
        power.setTextColor(BG);
        power.setBackground(pillBackground(0,GOLD,28,0));
        LinearLayout.LayoutParams pwrParams=new LinearLayout.LayoutParams(-1,dp(56));
        pwrParams.setMargins(0,dp(2),0,dp(14));
        power.setLayoutParams(pwrParams);
        power.setOnClickListener(v->{ v.performHapticFeedback(android.view.HapticFeedbackConstants.VIRTUAL_KEY); toggleConnection(); });
        content.addView(power);

        speedCard=new LinearLayout(this);
        speedCard.setOrientation(LinearLayout.HORIZONTAL);
        speedCard.setGravity(Gravity.CENTER);
        speedCard.setPadding(dp(18),dp(10),dp(18),dp(10));
        speedCard.setBackground(pillBackground(0x229FA7B4,0x1A1B2028,14,1));
        LinearLayout.LayoutParams speedCardParams=new LinearLayout.LayoutParams(-1,-2);
        speedCardParams.setMargins(0,0,0,dp(12));
        speedCard.setLayoutParams(speedCardParams);

        speedText=label("\u2193 0 B/s      \u2191 0 B/s",13,TEXT);
        speedText.setTypeface(body);
        speedCard.addView(speedText);
        speedCard.setVisibility(View.GONE);
        content.addView(speedCard);

        LinearLayout locationCard=new LinearLayout(this);
        locationCard.setOrientation(LinearLayout.HORIZONTAL);
        locationCard.setGravity(Gravity.CENTER_VERTICAL);
        locationCard.setPadding(dp(18),dp(14),dp(18),dp(14));
        locationCard.setBackground(new android.graphics.drawable.RippleDrawable(android.content.res.ColorStateList.valueOf(0x22D9BD83),pillBackground(0x1F9FA7B4,SURFACE,16,1),background(Color.WHITE)));
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

        TextView changeAction=label("Change \u203A",13,GOLD);
        locationCard.addView(changeAction);
        locationCard.setOnClickListener(v->{ v.performHapticFeedback(android.view.HapticFeedbackConstants.VIRTUAL_KEY); showPage(1); });
        content.addView(locationCard);

        LinearLayout tgCard=new LinearLayout(this);
        tgCard.setOrientation(LinearLayout.HORIZONTAL);
        tgCard.setGravity(Gravity.CENTER_VERTICAL);
        tgCard.setPadding(dp(18),dp(12),dp(18),dp(12));
        tgCard.setBackground(new android.graphics.drawable.RippleDrawable(android.content.res.ColorStateList.valueOf(0x22D9BD83),pillBackground(0x1FD9BD83,SURFACE,16,1),background(Color.WHITE)));
        LinearLayout.LayoutParams tgCardParams=new LinearLayout.LayoutParams(-1,-2);
        tgCardParams.setMargins(0,0,0,dp(12));
        tgCard.setLayoutParams(tgCardParams);

        LinearLayout tgCol=new LinearLayout(this);
        tgCol.setOrientation(LinearLayout.VERTICAL);
        TextView tgTitle=label("\u26a1 TELEGRAM PROXY",11,GOLD);
        tgTitle.setTypeface(body,Typeface.BOLD);
        tgTitle.setLetterSpacing(0.06f);
        TextView tgSub=label("اتصال دائم تلگرام بدون فیلترشکن",13,TEXT);
        tgSub.setTypeface(body);
        tgCol.addView(tgTitle);
        tgCol.addView(tgSub);
        tgCard.addView(tgCol,new LinearLayout.LayoutParams(0,-2,1));

        TextView tgAction=label("دریافت \u203a",13,GOLD);
        tgCard.addView(tgAction);
        tgCard.setOnClickListener(v->{ v.performHapticFeedback(android.view.HapticFeedbackConstants.VIRTUAL_KEY); showTelegramProxyModal(); });
        content.addView(tgCard);

        LinearLayout quotaCard=new LinearLayout(this);
        quotaCard.setOrientation(LinearLayout.VERTICAL);
        quotaCard.setPadding(dp(18),dp(14),dp(18),dp(14));
        quotaCard.setBackground(new android.graphics.drawable.RippleDrawable(android.content.res.ColorStateList.valueOf(0x22D9BD83),pillBackground(0x1F9FA7B4,SURFACE,16,1),background(Color.WHITE)));
        LinearLayout.LayoutParams quotaCardParams=new LinearLayout.LayoutParams(-1,-2);
        quotaCardParams.setMargins(0,0,0,dp(12));
        quotaCard.setLayoutParams(quotaCardParams);

        LinearLayout quotaTop=new LinearLayout(this);
        quotaTop.setOrientation(LinearLayout.HORIZONTAL);
        quotaTop.setGravity(Gravity.CENTER_VERTICAL);
        TextView quotaTitle=label("ACCOUNT QUOTA",11,GOLD);
        quotaTitle.setTypeface(body,Typeface.BOLD);
        quotaTitle.setLetterSpacing(0.06f);
        quotaTop.addView(quotaTitle,new LinearLayout.LayoutParams(0,-2,1));
        TextView manageAction=label("Manage \u203a",13,MUTED);
        quotaTop.addView(manageAction);
        quotaCard.addView(quotaTop);

        quotaSummary=label("Loading quota\u2026",14,TEXT);
        quotaSummary.setTypeface(body,Typeface.BOLD);
        LinearLayout.LayoutParams qsLp=new LinearLayout.LayoutParams(-1,-2);
        qsLp.setMargins(0,dp(3),0,dp(6));
        quotaSummary.setLayoutParams(qsLp);
        quotaCard.addView(quotaSummary);

        quotaBar=new ProgressBar(this,null,android.R.attr.progressBarStyleHorizontal);
        quotaBar.setMax(100);
        quotaBar.setProgress(100);
        quotaBar.setProgressTintList(android.content.res.ColorStateList.valueOf(GOLD));
        quotaBar.setProgressBackgroundTintList(android.content.res.ColorStateList.valueOf(0x229FA7B4));
        LinearLayout.LayoutParams qbLp=new LinearLayout.LayoutParams(-1,dp(4));
        qbLp.setMargins(0,dp(2),0,0);
        quotaBar.setLayoutParams(qbLp);
        quotaCard.addView(quotaBar);

        quotaCard.setOnClickListener(v->{ v.performHapticFeedback(android.view.HapticFeedbackConstants.VIRTUAL_KEY); showPage(3); });
        content.addView(quotaCard);

        updateHome();
    }
    private void toggleConnection(){
        if(UpdateActivity.isRequired(this)){UpdateActivity.openRequired(this);return;}
        if(!ready){feedback.setText("Your local engine is starting.");return;}
        AdManager.maybeShowInterstitial(this);
        if(checkingAccount){checkingAccount=false;connectAttempt++;updateHome();feedback.setText("Connection cancelled.");return;}
        if(TunnelService.lifecycle.canCancel()){startService(new Intent(this,TunnelService.class).setAction(TunnelService.STOP));statusText.setText("Disconnecting\u2026");power.setText("Disconnecting\u2026");main.postDelayed(this::poll,100);return;}
        if(TunnelService.lifecycle.phase()==VpnLifecycle.Phase.STOPPING){feedback.setText("Finishing the previous connection. Try again in a moment.");return;}
        checkingAccount=true;final int attempt=++connectAttempt;updateHome();feedback.setText("Checking your account\u2026");
        CoreRuntime.IO.execute(()->{
            try{
                JSONObject reply=CoreRuntime.request("GET","/api/accounts",null,5000);
                main.post(()->{
                    if(isDestroyed()||attempt!=connectAttempt)return;
                    JSONArray rows=reply.optJSONArray("accounts");
                    if(rows==null||rows.length()==0){
                        feedback.setText("Setting up secure profile\u2026");
                        AccountSetup.ensureAccount(this,new AccountSetup.ProvisionCallback(){
                            @Override public void onSuccess(){
                                main.post(()->{
                                    if(isDestroyed()||attempt!=connectAttempt)return;
                                    checkingAccount=false;
                                    updateHome();
                                    feedback.setText("");
                                    explainConnection();
                                });
                            }
                            @Override public void onFailure(String error){
                                main.post(()->{
                                    if(isDestroyed()||attempt!=connectAttempt)return;
                                    checkingAccount=false;
                                    updateHome();
                                    showPage(3);
                                    feedback.setText("Account setup: "+error);
                                });
                            }
                        });
                        return;
                    }
                    checkingAccount=false;
                    updateHome();
                    feedback.setText("");
                    explainConnection();
                });
            }catch(Exception error){
                main.post(()->{
                    if(isDestroyed()||attempt!=connectAttempt)return;
                    checkingAccount=false;
                    updateHome();
                    showError(error.getMessage());
                });
            }
        });
    }
    private void explainConnection(){
        if(getPreferences(MODE_PRIVATE).getInt("vpn_disclosure",0)==1){requestVpnPermission();return;}
        new AlertDialog.Builder(this).setTitle("Before you connect")
            .setMessage("III85III VPN uses Android's VPN interface to route supported internet traffic securely through encrypted cloud network service. Your account is used for service quota checks. The service receives information needed to carry your connection, including network addresses. Selected excluded apps stay direct. Local connection diagnostics may contain network details. This app is not a verified kill switch.")
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
        Intent intent=new Intent(this,TunnelService.class);startForegroundService(intent);feedback.setText("Starting the VPN connection\u2026");if(statusText!=null){statusText.setText("Connecting\u2026");power.setText("Cancel connection");}main.postDelayed(this::poll,100);
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
            statusText.setText("CHECKING ACCOUNT\u2026");
            statusText.setTextColor(GOLD);
            dotBg.setColor(GOLD);
            if(statusDot!=null)statusDot.setBackground(dotBg);
            detailText.setVisibility(View.GONE);
            power.setText("CANCEL");
            power.setTextColor(GOLD);
            power.setBackground(pillBackground(GOLD,SURFACE,28,2));
            setInfinityState(InfinityLoopView.STATE_CONNECTING);
        }else if(verified || (TunnelService.active && "running".equals(phase) && problem.isEmpty())){
            statusText.setText("PROTECTED");
            statusText.setTextColor(GOLD);
            dotBg.setColor(GOLD);
            if(statusDot!=null)statusDot.setBackground(dotBg);
            detailText.setVisibility(View.GONE);
            power.setText("DISCONNECT");
            power.setTextColor(GOLD);
            power.setBackground(pillBackground(GOLD,SURFACE,28,2));
            setInfinityState(InfinityLoopView.STATE_CONNECTED);
        }else if(!problem.isEmpty()){
            statusText.setText("ATTENTION REQUIRED");
            statusText.setTextColor(AMBER);
            dotBg.setColor(AMBER);
            if(statusDot!=null)statusDot.setBackground(dotBg);
            detailText.setText(problem+"  \u00b7  Tap for details");
            detailText.setVisibility(View.VISIBLE);
            detailText.setOnClickListener(v->showError(problem));
            power.setText("RECONNECT");
            power.setTextColor(BG);
            power.setBackground(background(GOLD));
            setInfinityState(InfinityLoopView.STATE_ERROR);
        }else if(transition==VpnLifecycle.Phase.STOPPING){
            statusText.setText("DISCONNECTING\u2026");
            statusText.setTextColor(MUTED);
            dotBg.setColor(MUTED);
            if(statusDot!=null)statusDot.setBackground(dotBg);
            detailText.setVisibility(View.GONE);
            power.setText("DISCONNECTING\u2026");
            power.setTextColor(MUTED);
            power.setBackground(background(SURFACE));
            setInfinityState(InfinityLoopView.STATE_DISCONNECTED);
        }else if(TunnelService.active||transition==VpnLifecycle.Phase.STARTING){
            statusText.setText("CONNECTING\u2026");
            statusText.setTextColor(GOLD);
            dotBg.setColor(GOLD);
            if(statusDot!=null)statusDot.setBackground(dotBg);
            detailText.setVisibility(View.GONE);
            power.setText("CANCEL");
            power.setTextColor(GOLD);
            power.setBackground(pillBackground(GOLD,SURFACE,28,2));
            setInfinityState(InfinityLoopView.STATE_CONNECTING);
        }else{
            statusText.setText("DISCONNECTED");
            statusText.setTextColor(MUTED);
            dotBg.setColor(MUTED);
            if(statusDot!=null)statusDot.setBackground(dotBg);
            detailText.setVisibility(View.GONE);
            power.setText("CONNECT");
            power.setTextColor(BG);
            power.setBackground(pillBackground(0,GOLD,28,0));
            setInfinityState(InfinityLoopView.STATE_DISCONNECTED);
        }
        power.setEnabled(transition!=VpnLifecycle.Phase.STOPPING);
        if(speedCard!=null){
            boolean active=verified||(TunnelService.active&&"running".equals(phase)&&problem.isEmpty());
            speedCard.setVisibility(active?View.VISIBLE:View.GONE);
        }
        try{
            String[] rate=Mobile.speed().split(",");
            speedText.setText("\u2193 "+bytes(Double.parseDouble(rate[0]))+"/s      \u2191 "+bytes(Double.parseDouble(rate[1]))+"/s");
        }catch(Exception ignored){}
        if(quotaSummary!=null){
            JSONObject activeAcc=status.optJSONObject("active_account");
            if(activeAcc!=null){
                JSONObject usage=activeAcc.optJSONObject("usage");
                if(usage!=null){
                    if(usage.optBoolean("unlimited",false)){
                        quotaSummary.setText("Unlimited Plan");
                        if(quotaBar!=null)quotaBar.setProgress(100);
                    }else{
                        double rem=usage.optDouble("remaining_bytes",0);
                        double lim=usage.optDouble("limit_bytes",0);
                        double total=lim>0?lim:(50.0*1024*1024*1024);
                        if(rem<=0&&lim<=0) rem=total;
                        quotaSummary.setText(bytes(rem)+" remaining of "+bytes(total));
                        if(quotaBar!=null){
                            int pct=(int)Math.max(1,Math.min(100,(rem*100.0/total)));
                            quotaBar.setProgress(pct);
                        }
                    }
                }else{
                    quotaSummary.setText("50.0 GB remaining of 50.0 GB");
                    if(quotaBar!=null)quotaBar.setProgress(100);
                }
            }else{
                quotaSummary.setText("50.0 GB Monthly Plan");
                if(quotaBar!=null)quotaBar.setProgress(100);
            }
        }
    }
    private String bytes(double amount){if(Double.isNaN(amount)||amount<=0)return "0 B";String[] unit={"B","KB","MB","GB"};int n=0;while(amount>=1000&&n<3){amount/=1000;n++;}return String.format(Locale.US,n==0?"%.0f %s":"%.1f %s",amount,unit[n]);}
    static String connectionProblem(JSONObject value){
        JSONObject state=value.optJSONObject("state"),proxy=value.optJSONObject("proxy");String failure=value.optString("connect_failure");
        if(state!=null&&(state.optString("phase").equals("error")||state.optString("code").equals("no_usable_account")||state.optString("code").equals("no_accounts")))return state.optString("message","Your saved account is not available. Check Accounts for details.");
        if(proxy!=null&&!proxy.optString("wake_error").isEmpty())return proxy.optString("wake_error");
        JSONObject test=value.optJSONObject("last_test");if(test!=null&&state!=null&&!test.optBoolean("ok")&&ConnectionTruth.fresh(test.optString("at"),state.optString("since"),value.optString("network_changed_at"))&&!test.optString("error").isEmpty())return "Connection check: "+test.optString("error");
        if(!failure.isEmpty())return "Connection could not complete ("+failure+"). Check your account and network, then retry.";
        return "";
    }
    private void locationPage(){
        heading("Servers","Select location or tap Fastest");
        EditText search=new EditText(this);
        search.setSingleLine(true);
        search.setHint("Search country or code\u2026");
        search.setTextColor(TEXT);
        search.setHintTextColor(MUTED);
        search.setTextSize(14);
        search.setBackground(pillBackground(0x1F9FA7B4,SURFACE,14,1));
        search.setPadding(dp(16),dp(12),dp(16),dp(12));
        LinearLayout.LayoutParams searchLp=new LinearLayout.LayoutParams(-1,-2);
        searchLp.setMargins(0,dp(4),0,dp(10));
        search.setLayoutParams(searchLp);
        content.addView(search);

        LinearLayout quickRow=new LinearLayout(this);
        quickRow.setOrientation(LinearLayout.HORIZONTAL);
        LinearLayout.LayoutParams qrLp=new LinearLayout.LayoutParams(-1,-2);
        qrLp.setMargins(0,0,0,dp(10));
        quickRow.setLayoutParams(qrLp);

        Button recBtn=button("\u2605 Recommended · 🛡️ AdBlock",()->api("POST","/api/location",object("country",""),10000,reply->{showPage(0);}));
        LinearLayout.LayoutParams recLp=new LinearLayout.LayoutParams(0,dp(46),1);
        recLp.setMargins(0,0,dp(4),0);
        recBtn.setLayoutParams(recLp);
        recBtn.setTextSize(13);
        recBtn.setTextColor(GOLD);
        recBtn.setBackground(pillBackground(0x33D9BD83,SURFACE,14,1));
        quickRow.addView(recBtn);

        Button fastBtn=button("\u26a1 Fastest · 🛡️ AdBlock",()->api("POST","/api/location",object("country","fastest"),60000,reply->{showPage(0);}));
        LinearLayout.LayoutParams fastLp=new LinearLayout.LayoutParams(0,dp(46),1);
        fastLp.setMargins(dp(4),0,0,0);
        fastBtn.setLayoutParams(fastLp);
        fastBtn.setTextSize(13);
        fastBtn.setTextColor(GOLD);
        fastBtn.setBackground(pillBackground(0x33D9BD83,SURFACE,14,1));
        quickRow.addView(fastBtn);

        content.addView(quickRow);

        Button measure=button(pinging?"Testing\u2026":"Test ping",()->{
            if(pinging)return;pinging=true;feedback.setText("Testing ping\u2026");
            CoreRuntime.IO.execute(()->{try{CoreRuntime.request("POST","/api/locations/ping",object("codes",new JSONArray()),60000);}catch(Exception error){main.post(()->feedback.setText("Ping test failed. Try again."));}finally{main.post(()->{pinging=false;if(page==1)showPage(1);});}});
        });measure.setEnabled(!pinging);content.addView(measure);
        LinearLayout list=new LinearLayout(this);list.setOrientation(LinearLayout.VERTICAL);content.addView(list);
        Runnable render=()->{list.removeAllViews();String q=search.getText().toString().trim().toLowerCase(Locale.ROOT);List<JSONObject> values=new ArrayList<>();for(int i=0;i<locations.length();i++){JSONObject row=locations.optJSONObject(i);if(row!=null&&!LocationOrder.pinned(row.optString("code")))values.add(row);}values.sort((a,b)->LocationOrder.compare(a.optString("code"),a.optString("name"),a.optInt("ping_ms",0),b.optString("code"),b.optString("name"),b.optInt("ping_ms",0)));
            for(JSONObject row:values){String code=row.optString("code"),name=row.optString("name",code);if(!(name+" "+code).toLowerCase(Locale.ROOT).contains(q))continue;int ping=row.optInt("ping_ms",0);list.addView(button(name+"  [🛡️ AdBlock]"+(ping>0?"  \u00b7  "+ping+" ms":""),()->api("POST","/api/location",object("country",code),10000,reply->{feedback.setText("Location selected: "+name);showPage(0);})));}
            if(list.getChildCount()==0){TextView empty=label("No matching locations yet.",14,MUTED);list.addView(empty);}
        };
        search.addTextChangedListener(new TextWatcher(){public void beforeTextChanged(CharSequence s,int start,int count,int after){}public void onTextChanged(CharSequence s,int start,int before,int count){render.run();}public void afterTextChanged(Editable value){}});
        api("GET","/api/locations",null,15000,reply->{JSONArray rows=reply.optJSONArray("locations");locations=rows==null?new JSONArray():rows;if(page==1)render.run();});render.run();
    }
    private void accounts(){
        heading("Accounts","Manage your Mozilla VPN profiles & monthly quota.");
        content.addView(button("+ Add an account",()->{if(signIn==null||signIn.closed)signIn=new SignInFlow();}));
        Button v2rayBtn=button("\u26a1 Export V2Ray / iOS Config (Top 30)",()->showV2RayModal(null));
        v2rayBtn.setTextColor(GOLD);
        content.addView(v2rayBtn);
        boolean hidden=getPreferences(MODE_PRIVATE).getBoolean("hide_email",true);
        content.addView(button(hidden?"Show full email addresses":"Hide email addresses",()->{getPreferences(MODE_PRIVATE).edit().putBoolean("hide_email",!hidden).apply();showPage(3);}));
        LinearLayout rows=new LinearLayout(this);rows.setOrientation(LinearLayout.VERTICAL);content.addView(rows);
        api("GET","/api/accounts",null,7000,reply->{
            if(page!=3)return;
            JSONArray data=reply.optJSONArray("accounts");
            if(data==null||data.length()==0){
                note("No accounts yet. Your account session stays in this app's private storage.");
                return;
            }
            for(int i=0;i<data.length();i++){
                JSONObject account=data.optJSONObject(i);
                if(account==null)continue;
                String id=account.optString("id");
                String email=account.optString("email");
                boolean isActive=account.optBoolean("active",false);
                boolean isEnabled=account.optBoolean("enabled",true);
                String statusStr=account.optString("status","ready");
                JSONObject usage=account.optJSONObject("usage");

                LinearLayout card=new LinearLayout(this);
                card.setOrientation(LinearLayout.VERTICAL);
                card.setPadding(dp(18),dp(16),dp(18),dp(16));
                card.setBackground(cardBackground(SURFACE,16));
                LinearLayout.LayoutParams cardLp=new LinearLayout.LayoutParams(-1,-2);
                cardLp.setMargins(0,dp(6),0,dp(14));
                card.setLayoutParams(cardLp);

                // Top row: Email + Status Badge
                LinearLayout topRow=new LinearLayout(this);
                topRow.setOrientation(LinearLayout.HORIZONTAL);
                topRow.setGravity(Gravity.CENTER_VERTICAL);

                TextView emailView=label(hidden?"\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022":email,15,TEXT);
                emailView.setTypeface(body,Typeface.BOLD);
                topRow.addView(emailView,new LinearLayout.LayoutParams(0,-2,1));

                TextView badge=label(isActive?"ACTIVE":statusStr.toUpperCase(Locale.ROOT),10,isActive?BG:TEXT);
                badge.setTypeface(body,Typeface.BOLD);
                badge.setPadding(dp(8),dp(3),dp(8),dp(3));
                badge.setBackground(pillBackground(isActive?GOLD:0x33CBD5E1,isActive?GOLD:0x1ACBD5E1,10,1));
                topRow.addView(badge);
                card.addView(topRow);

                // Quota volume box
                LinearLayout quotaBox=new LinearLayout(this);
                quotaBox.setOrientation(LinearLayout.VERTICAL);
                quotaBox.setPadding(0,dp(12),0,dp(8));

                double remBytes=0, limBytes=0;
                boolean isUnlimited=false;
                String resetStr="";
                if(usage!=null){
                    isUnlimited=usage.optBoolean("unlimited",false);
                    remBytes=usage.optDouble("remaining_bytes",0);
                    limBytes=usage.optDouble("limit_bytes",0);
                    resetStr=usage.optString("reset_at","");
                }
                if(usage==null||(remBytes<=0&&limBytes<=0)){
                    if(!autoSyncedAccounts.contains(id)){
                        autoSyncedAccounts.add(id);
                        api("POST","/api/accounts/"+id+"/refresh",new JSONObject(),15000,r->{if(page==3)showPage(3);});
                    }
                }

                if(isUnlimited){
                    TextView remTitle=label("UNLIMITED DATA PLAN",16,GOLD);
                    remTitle.setTypeface(body,Typeface.BOLD);
                    quotaBox.addView(remTitle);
                }else{
                    double totalLimit=limBytes>0?limBytes:(50.0*1024*1024*1024);
                    double remaining=(remBytes>0||limBytes>0)?remBytes:totalLimit;
                    double used=Math.max(0,totalLimit-remaining);
                    double usedPct=totalLimit>0?(used*100.0/totalLimit):0.0;

                    TextView remBig=label(bytes(remaining),22,GOLD);
                    remBig.setTypeface(body,Typeface.BOLD);
                    quotaBox.addView(remBig);

                    TextView subDetail=label("Remaining of "+bytes(totalLimit)+"  \u00B7  "+String.format(Locale.US,"%.1f%% used",usedPct),12,MUTED);
                    subDetail.setPadding(0,dp(2),0,dp(8));
                    quotaBox.addView(subDetail);

                    // Visual Progress Bar
                    LinearLayout progressHolder=new LinearLayout(this);
                    progressHolder.setOrientation(LinearLayout.HORIZONTAL);
                    progressHolder.setBackground(pillBackground(0,0xFF101319,6,0));
                    int fillWidthPct=Math.max(2,Math.min(100,(int)Math.round(100-usedPct)));
                    View fillView=new View(this);
                    fillView.setBackground(pillBackground(0,GOLD,6,0));
                    progressHolder.addView(fillView,new LinearLayout.LayoutParams(0,dp(6),(float)fillWidthPct));
                    View emptyView=new View(this);
                    progressHolder.addView(emptyView,new LinearLayout.LayoutParams(0,dp(6),(float)(100-fillWidthPct)));
                    quotaBox.addView(progressHolder);
                }

                if(!resetStr.isEmpty()){
                    String dateDisplay=resetStr.contains("T")?resetStr.substring(0,resetStr.indexOf('T')):resetStr;
                    TextView resetView=label("\u21bb Renews on: "+dateDisplay,11,MUTED);
                    resetView.setPadding(0,dp(6),0,0);
                    quotaBox.addView(resetView);
                }
                card.addView(quotaBox);

                String reason=account.optString("reason_detail",account.optString("reason"));
                if(!reason.isEmpty()){
                    TextView reasonView=label(hidden&&!email.isEmpty()?reason.replace(email,"\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022"):reason,12,AMBER);
                    reasonView.setPadding(0,0,0,dp(8));
                    card.addView(reasonView);
                }

                // Action buttons inside card
                LinearLayout actionsRow=new LinearLayout(this);
                actionsRow.setOrientation(LinearLayout.HORIZONTAL);
                actionsRow.setGravity(Gravity.CENTER_VERTICAL);
                actionsRow.setPadding(0,dp(6),0,0);

                if(!isActive){
                    Button useBtn=button(isEnabled?"Use This Account":"Enable & Use",()->api("POST","/api/accounts/"+id+"/enabled",object("enabled",true),7000,enabled->api("POST","/api/accounts/"+id+"/activate",new JSONObject(),7000,selected->{feedback.setText("Account selected. You can connect from VPN.");showPage(0);})));
                    useBtn.setTextColor(BG);
                    useBtn.setBackground(background(GOLD));
                    LinearLayout.LayoutParams useLp=new LinearLayout.LayoutParams(0,dp(42),1);
                    useLp.setMargins(0,0,dp(6),0);
                    useBtn.setLayoutParams(useLp);
                    actionsRow.addView(useBtn);
                }

                Button refreshBtn=button("Refresh \u27f3",()->{
                    feedback.setText("Refreshing quota\u2026");
                    autoSyncedAccounts.remove(id);
                    api("POST","/api/accounts/"+id+"/refresh",new JSONObject(),15000,r->{if(page==3)showPage(3);});
                });
                LinearLayout.LayoutParams refLp=new LinearLayout.LayoutParams(isActive?0:-2,dp(42),isActive?1:0);
                refLp.setMargins(0,0,dp(6),0);
                refreshBtn.setLayoutParams(refLp);
                actionsRow.addView(refreshBtn);

                Button v2rayCardBtn=button("V2Ray \u2197",()->showV2RayModal(email));
                v2rayCardBtn.setTextColor(GOLD);
                LinearLayout.LayoutParams v2rayCardLp=new LinearLayout.LayoutParams(-2,dp(42));
                v2rayCardLp.setMargins(0,0,dp(6),0);
                v2rayCardBtn.setLayoutParams(v2rayCardLp);
                actionsRow.addView(v2rayCardBtn);

                Button removeBtn=button("Remove",()->new AlertDialog.Builder(this).setTitle("Remove this account?").setMessage("This removes its stored session from this device.").setNegativeButton("Cancel",null).setPositiveButton("Remove",(d,w)->api("DELETE","/api/accounts/"+id,null,7000,r->{if(page==3)showPage(3);})).show());
                removeBtn.setTextColor(Color.rgb(255,100,100));
                LinearLayout.LayoutParams remLp=new LinearLayout.LayoutParams(-2,dp(42));
                removeBtn.setLayoutParams(remLp);
                actionsRow.addView(removeBtn);

                card.addView(actionsRow);
                rows.addView(card);
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
        boolean warOn=getSharedPreferences("vpn_prefs",MODE_PRIVATE).getBoolean("ready_for_war",true);
        LinearLayout warCard=new LinearLayout(this);warCard.setOrientation(LinearLayout.HORIZONTAL);
        android.graphics.drawable.GradientDrawable warBg=new android.graphics.drawable.GradientDrawable();
        warBg.setColor(0xFF1B1D20);warBg.setCornerRadius(dp(12));warBg.setStroke(dp(1),0xFFD9BD83);
        warCard.setBackground(warBg);warCard.setPadding(dp(16),dp(12),dp(16),dp(12));warCard.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout.LayoutParams warLp=new LinearLayout.LayoutParams(-1,-2);warLp.setMargins(0,dp(6),0,dp(8));warCard.setLayoutParams(warLp);
        LinearLayout warText=new LinearLayout(this);warText.setOrientation(LinearLayout.VERTICAL);
        TextView warTitle=new TextView(this);warTitle.setText("\u2694\ufe0f READY FOR WAR");warTitle.setTextColor(0xFFD9BD83);warTitle.setTextSize(14);warTitle.setTypeface(body,Typeface.BOLD);
        TextView warSub=new TextView(this);warSub.setText(warOn?"ARMED \u2022 TLS Fragment & Anycast Rescue":"OFF \u2022 Anti-DPI bypass shield");warSub.setTextColor(0xFF9E9A90);warSub.setTextSize(11);warSub.setTypeface(body);
        warText.addView(warTitle);warText.addView(warSub);warCard.addView(warText,new LinearLayout.LayoutParams(0,-2,1));
        android.widget.Switch warSwitch=new android.widget.Switch(this);warSwitch.setChecked(warOn);
        warSwitch.setOnCheckedChangeListener((btn,isChecked)->{
            getSharedPreferences("vpn_prefs",MODE_PRIVATE).edit().putBoolean("ready_for_war",isChecked).apply();
            warSub.setText(isChecked?"ARMED \u2022 TLS Fragment & Anycast Rescue":"OFF \u2022 Anti-DPI bypass shield");
            Toast.makeText(this,isChecked?"\u2694\ufe0f Ready for War: Armed":"Ready for War: Off",Toast.LENGTH_SHORT).show();
        });
        warCard.addView(warSwitch);content.addView(warCard);
        content.addView(button("\u26a1 Telegram Proxy (اتصال دائم بدون فیلترشکن)",()->showTelegramProxyModal()));
        content.addView(button("\u26a1 Check for Updates  (v"+BuildConfig.VERSION_NAME+")",()->checkForUpdates(true)));
        content.addView(button("\u26a1 V2Ray / iOS Setup (Top 30 Configs)",()->showV2RayModal(null)));
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

    private void openTelegramChannel(String domain){
        try{
            Intent nativeTg=new Intent(Intent.ACTION_VIEW,Uri.parse("tg://resolve?domain="+domain));
            nativeTg.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            startActivity(nativeTg);
        }catch(Exception e){
            try{
                Intent webTg=new Intent(Intent.ACTION_VIEW,Uri.parse("https://t.me/"+domain));
                webTg.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                startActivity(webTg);
            }catch(Exception err){
                showError("Open t.me/"+domain+" in your browser.");
            }
        }
    }

    private void showTelegramProxyModal(){
        AlertDialog.Builder builder=new AlertDialog.Builder(this);
        LinearLayout layout=new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setPadding(dp(22),dp(18),dp(22),dp(14));
        layout.setBackgroundColor(0xFF1B1D20);

        TextView title=new TextView(this);
        title.setText("حمایت از سرویس رایگان 🤍");
        title.setTextColor(GOLD);
        title.setTextSize(16);
        title.setTypeface(body,Typeface.BOLD);
        title.setGravity(Gravity.RIGHT);
        layout.addView(title);

        TextView msg=new TextView(this);
        msg.setText("\nاین برنامه و پروکسی پرسرعت تلگرام، به‌صورت کاملاً رایگان برای شما فراهم شده است.\n\nتنها خواهش و حمایت شما از ما، عضویت در کانال‌های اسپانسر ما است تا بتوانیم این سرویس را همیشه پایدار، پرسرعت و رایگان نگه داریم.");
        msg.setTextColor(0xFFEFEFEA);
        msg.setTextSize(14);
        msg.setTypeface(body);
        msg.setGravity(Gravity.RIGHT);
        msg.setLineSpacing(dp(4),1f);
        layout.addView(msg);

        Button ch1=button("🤍 عضویت در کانال اصلی (@cabicy)",()->openTelegramChannel("cabicy"));
        ch1.setTextColor(BG);
        ch1.setBackground(pillBackground(0,GOLD,18,0));
        ch1.setTypeface(body,Typeface.BOLD);
        LinearLayout.LayoutParams ch1Lp=new LinearLayout.LayoutParams(-1,dp(44));
        ch1Lp.setMargins(0,dp(12),0,dp(8));
        ch1.setLayoutParams(ch1Lp);
        layout.addView(ch1);

        Button ch2=button("🎵 عضویت در کانال دوم (@playlist_sxy)",()->openTelegramChannel("playlist_sxy"));
        ch2.setTextColor(TEXT);
        ch2.setBackground(pillBackground(0x229FA7B4,0x1F9FA7B4,18,1));
        ch2.setTypeface(body);
        LinearLayout.LayoutParams ch2Lp=new LinearLayout.LayoutParams(-1,dp(42));
        ch2Lp.setMargins(0,0,0,dp(12));
        ch2.setLayoutParams(ch2Lp);
        layout.addView(ch2);

        builder.setView(layout);
        builder.setPositiveButton("⚡ اتصال به پروکسی تلگرام",(d,w)->connectTelegramProxyAndOpenChannel());
        builder.setNegativeButton("انصراف",null);
        AlertDialog dialog=builder.create();
        dialog.show();
        try{
            dialog.getButton(DialogInterface.BUTTON_POSITIVE).setTextColor(GOLD);
            dialog.getButton(DialogInterface.BUTTON_NEGATIVE).setTextColor(0xFF9FA7B4);
        }catch(Exception ignored){}
    }

    private void connectTelegramProxyAndOpenChannel(){
        CoreRuntime.IO.execute(()->{
            try{
                CoreRuntime.ensure(this);
                mobile.Mobile.setOn(true);
                mobile.Mobile.connect();
            }catch(Exception ignored){}
        });
        int p=CoreRuntime.getProxyPort();
        try{
            Intent proxyIntent=new Intent(Intent.ACTION_VIEW,Uri.parse("tg://socks?server=proxy.cabicy.lvh.me&port="+p));
            proxyIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            startActivity(proxyIntent);
        }catch(Exception e){
            try{
                Intent webProxy=new Intent(Intent.ACTION_VIEW,Uri.parse("https://t.me/socks?server=proxy.cabicy.lvh.me&port="+p));
                webProxy.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                startActivity(webProxy);
            }catch(Exception ignored){}
        }
        main.postDelayed(()->{
            openTelegramChannel("cabicy");
        },2200);
    }
    private void help(){heading("Help","");content.addView(button("Check connection",()->api("POST","/api/test",new JSONObject(),20000,reply->feedback.setText(reply.optBoolean("ok")?"Connection verified.":"Connection could not be verified. Check your account and network."))));content.addView(button("Test ping",()->showPage(1)));note("Try another server or check your Mozilla account.");note("App rules exclude selected apps from the VPN.");}

    private final class SignInFlow {
        boolean closed,pending,isBrowserTab;String session="",promptStamp="",answeredStamp="",userEmail="";AlertDialog dialog;EditText password,browserEmail,browserToken;
        final Runnable pollFlow=()->fetch();
        SignInFlow(){
            LinearLayout rootLayout=new LinearLayout(MainActivity.this);rootLayout.setPadding(dp(20),dp(8),dp(20),dp(8));rootLayout.setOrientation(LinearLayout.VERTICAL);

            LinearLayout tabBar=new LinearLayout(MainActivity.this);tabBar.setOrientation(LinearLayout.HORIZONTAL);
            Button tabPass=new Button(MainActivity.this);tabPass.setText("🔑 Password");tabPass.setTypeface(body,Typeface.BOLD);tabPass.setTextSize(12);
            Button tabBrow=new Button(MainActivity.this);tabBrow.setText("🌐 Browser / Google");tabBrow.setTypeface(body,Typeface.BOLD);tabBrow.setTextSize(12);
            LinearLayout.LayoutParams tabParams=new LinearLayout.LayoutParams(0,dp(38),1f);tabParams.setMargins(dp(2),0,dp(2),dp(8));
            tabPass.setLayoutParams(tabParams);tabBrow.setLayoutParams(tabParams);
            tabBar.addView(tabPass);tabBar.addView(tabBrow);
            rootLayout.addView(tabBar);

            LinearLayout passContainer=new LinearLayout(MainActivity.this);passContainer.setOrientation(LinearLayout.VERTICAL);
            EditText email=new EditText(MainActivity.this);email.setHint("Mozilla email");email.setSingleLine(true);email.setInputType(android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS);email.setTypeface(body);email.setTextColor(TEXT);email.setHintTextColor(MUTED);passContainer.addView(email);
            password=new EditText(MainActivity.this);password.setHint("Mozilla password");password.setSingleLine(true);password.setInputType(129);password.setTransformationMethod(PasswordTransformationMethod.getInstance());password.setTypeface(body);password.setTextColor(TEXT);password.setHintTextColor(MUTED);passContainer.addView(password);
            rootLayout.addView(passContainer);

            LinearLayout browContainer=new LinearLayout(MainActivity.this);browContainer.setOrientation(LinearLayout.VERTICAL);browContainer.setVisibility(View.GONE);
            TextView browDesc=label("Sign in with Google or Firefox in your browser with no security blocks:",12,MUTED);browDesc.setPadding(0,0,0,dp(6));browContainer.addView(browDesc);
            Button openBrowBtn=button("🌐 Open Firefox / Google in Browser",()->{
                try{startActivity(new Intent(Intent.ACTION_VIEW,android.net.Uri.parse("https://accounts.firefox.com/signin?context=fx_desktop_v3&entrypoint=vpn")));feedback.setText("Browser opened. Sign in, then paste token below.");}
                catch(Exception ex){feedback.setText("Cannot open browser: "+ex.getMessage());}
            });
            openBrowBtn.setPadding(dp(12),dp(8),dp(12),dp(8));
            browContainer.addView(openBrowBtn);
            browserEmail=new EditText(MainActivity.this);browserEmail.setHint("Account Email / Gmail");browserEmail.setSingleLine(true);browserEmail.setInputType(android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS);browserEmail.setTypeface(body);browserEmail.setTextColor(TEXT);browserEmail.setHintTextColor(MUTED);browContainer.addView(browserEmail);
            browserToken=new EditText(MainActivity.this);browserToken.setHint("Account Token / Refresh Link");browserToken.setSingleLine(true);browserToken.setTypeface(body);browserToken.setTextColor(TEXT);browserToken.setHintTextColor(MUTED);browContainer.addView(browserToken);
            rootLayout.addView(browContainer);

            Runnable updateTabs=()->{
                if(!isBrowserTab){
                    passContainer.setVisibility(View.VISIBLE);browContainer.setVisibility(View.GONE);
                    tabPass.setBackgroundColor(GOLD);tabPass.setTextColor(BG);
                    tabBrow.setBackgroundColor(SURFACE);tabBrow.setTextColor(MUTED);
                    if(dialog!=null&&dialog.getButton(-1)!=null)dialog.getButton(-1).setText("Sign in");
                }else{
                    passContainer.setVisibility(View.GONE);browContainer.setVisibility(View.VISIBLE);
                    tabPass.setBackgroundColor(SURFACE);tabPass.setTextColor(MUTED);
                    tabBrow.setBackgroundColor(GOLD);tabBrow.setTextColor(BG);
                    if(dialog!=null&&dialog.getButton(-1)!=null)dialog.getButton(-1).setText("Link Account");
                }
            };
            tabPass.setOnClickListener(v->{isBrowserTab=false;updateTabs.run();});
            tabBrow.setOnClickListener(v->{isBrowserTab=true;updateTabs.run();});

            dialog=new AlertDialog.Builder(MainActivity.this).setTitle("Welcome to III85III").setMessage("Choose how you want to sign in:").setView(rootLayout).setNegativeButton("Cancel",(d,w)->cancel()).setPositiveButton("Sign in",null).create();
            dialog.setOnCancelListener(d->cancel());
            dialog.setOnShowListener(d->{
                updateTabs.run();
                dialog.getButton(-1).setOnClickListener(v->{
                    if(pending||closed)return;
                    if(isBrowserTab){
                        String mail=browserEmail.getText().toString().trim(),tok=browserToken.getText().toString().trim();
                        if(mail.isEmpty()||tok.isEmpty()){feedback.setText("Enter your account email and token.");return;}
                        pending=true;dialog.getButton(-1).setEnabled(false);
                        userEmail=mail;
                        JSONObject importBody=object("email",mail);try{importBody.put("refresh_token",tok);}catch(JSONException ignored){}
                        CoreRuntime.IO.execute(()->{
                            try{
                                JSONObject acc=CoreRuntime.request("POST","/api/accounts/import",importBody,30000);
                                main.post(()->{
                                    pending=false;closed=true;dialog.dismiss();
                                    feedback.setText("Account linked! Syncing quota\u2026");
                                    String addedId=acc.optString("id");
                                    if(!addedId.isEmpty()){api("POST","/api/accounts/"+addedId+"/refresh",new JSONObject(),15000,r->{if(page==3)showPage(3);});}
                                    showPage(3);showV2RayModal(userEmail);
                                });
                            }catch(Exception error){
                                main.post(()->{pending=false;if(!closed){showError(error.getMessage());dialog.getButton(-1).setEnabled(true);}});
                            }
                        });
                        return;
                    }
                    if(email.getText().toString().trim().isEmpty()||password.length()==0){feedback.setText("Enter your Mozilla email and password.");return;}
                    userEmail=email.getText().toString().trim();
                    JSONObject body=object("email",userEmail);try{body.put("password",password.getText().toString());}catch(JSONException ignored){}password.getText().clear();pending=true;dialog.getButton(-1).setEnabled(false);
                    CoreRuntime.IO.execute(()->{try{JSONObject reply=CoreRuntime.request("POST","/api/signin",body,60000);String id=reply.optString("id");main.post(()->{pending=false;session=id;if(closed){cancelSession(id);return;}dialog.dismiss();verification();apply(reply);});}catch(Exception error){main.post(()->{pending=false;if(!closed){showError(error.getMessage());dialog.getButton(-1).setEnabled(true);}});}});
                });
            });dialog.show();
        }
        void verification(){dialog=new AlertDialog.Builder(MainActivity.this).setTitle("Checking your sign-in").setMessage("Waiting for Mozilla\u2026").setNegativeButton("Cancel",(d,w)->cancel()).create();dialog.setOnCancelListener(d->cancel());dialog.show();}
        void apply(JSONObject state){
            if(closed)return;String step=state.optString("step");
            if(step.startsWith("need_")&&!answeredStamp.isEmpty()&&answeredStamp.equals(state.optString("updated"))){dialog.dismiss();verification();main.removeCallbacks(pollFlow);main.postDelayed(pollFlow,400);return;}
            if(step.equals("done")){
                closed=true;
                main.removeCallbacks(pollFlow);
                dialog.dismiss();
                feedback.setText("Account added! Syncing quota\u2026");
                String addedId=state.optString("account_id");
                if(!addedId.isEmpty()){
                    api("POST","/api/accounts/"+addedId+"/refresh",new JSONObject(),15000,r->{if(page==3)showPage(3);});
                }
                showPage(3);
                showV2RayModal(userEmail);
                return;
            }
            if(step.equals("error")||step.equals("failed")||step.equals("cancelled")){feedback.setText(state.optString("error",state.optString("message","Sign-in could not finish.")));cancel();return;}
            boolean code=step.equals("need_email_code")||step.equals("need_totp")||step.equals("need_unblock_code")||step.equals("need_captcha");
            if(code){
                promptStamp=state.optString("updated");
                dialog.dismiss();LinearLayout fields=new LinearLayout(MainActivity.this);fields.setOrientation(LinearLayout.VERTICAL);fields.setPadding(dp(24),dp(8),dp(24),dp(8));
                String captcha=state.optString("captcha");if(step.equals("need_captcha")&&captcha.startsWith("data:image/")&&captcha.length()<1500000){try{byte[] image=android.util.Base64.decode(captcha.substring(captcha.indexOf(',')+1),android.util.Base64.DEFAULT);ImageView view=new ImageView(MainActivity.this);view.setImageBitmap(android.graphics.BitmapFactory.decodeByteArray(image,0,image.length));fields.addView(view);}catch(Exception ignored){}}
                EditText input=new EditText(MainActivity.this);input.setSingleLine(true);input.setTypeface(body);input.setTextColor(TEXT);input.setHintTextColor(MUTED);input.setHint(step.equals("need_captcha")?"Verification characters":"Verification code");fields.addView(input);
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
            }else{if(dialog!=null&&dialog.isShowing())dialog.setMessage(state.optString("message","Waiting for Mozilla\u2026"));main.removeCallbacks(pollFlow);main.postDelayed(pollFlow,1500);}
        }
        void fetch(){if(closed||pending)return;pending=true;CoreRuntime.IO.execute(()->{try{JSONObject state=CoreRuntime.request("GET","/api/signin/"+session,null,5000);main.post(()->{pending=false;apply(state);});}catch(Exception error){main.post(()->{pending=false;if(!closed){feedback.setText("Sign-in status could not refresh. You can cancel and retry.");main.postDelayed(pollFlow,2500);}});}});}
        void cancelSession(String id){if(id.isEmpty())return;CoreRuntime.IO.execute(()->{try{CoreRuntime.request("DELETE","/api/signin/"+id,null,3000);}catch(Exception ignored){}});}
        void cancel(){if(closed)return;closed=true;main.removeCallbacks(pollFlow);if(password!=null)password.getText().clear();if(dialog!=null)dialog.dismiss();cancelSession(session);}
    }

    private void copyToClipboard(String label,String text){
        try{
            android.content.ClipboardManager clipboard=(android.content.ClipboardManager)getSystemService(Context.CLIPBOARD_SERVICE);
            ClipData clip=ClipData.newPlainText(label,text);
            if(clipboard!=null)clipboard.setPrimaryClip(clip);
            feedback.setText("Copied to clipboard!");
            Toast.makeText(this,"Copied to clipboard!",Toast.LENGTH_SHORT).show();
        }catch(Exception ignored){}
    }
    private void fetchAndCopyTop30Configs(){
        feedback.setText("Loading 30 ultra-fast configs\u2026");
        CoreRuntime.IO.execute(()->{
            String configs="";
            try{
                java.net.URL url=new java.net.URL("https://raw.githubusercontent.com/alireza786786/Free-V2ray-Configs/main/servers/vless_servers.txt");
                java.net.HttpURLConnection conn=(java.net.HttpURLConnection)url.openConnection();
                conn.setRequestProperty("User-Agent","Mozilla/5.0");
                conn.setConnectTimeout(8000);
                conn.setReadTimeout(8000);
                if(conn.getResponseCode()==200){
                    java.io.BufferedReader reader=new java.io.BufferedReader(new java.io.InputStreamReader(conn.getInputStream()));
                    StringBuilder sb=new StringBuilder();
                    String line;
                    int count=0;
                    while((line=reader.readLine())!=null&&count<30){
                        line=line.trim();
                        if(line.startsWith("vless://")){
                            sb.append(line).append("\n");
                            count++;
                        }
                    }
                    reader.close();
                    if(count>0)configs=sb.toString();
                }
            }catch(Exception ignored){}
            final String result=configs;
            main.post(()->{
                if(isDestroyed())return;
                if(!result.isEmpty()){
                    copyToClipboard("V2Ray Top 30",result);
                    new AlertDialog.Builder(this)
                        .setTitle("\u2713 30 Configs Copied!")
                        .setMessage("\u06f3\u06f0 \u06a9\u0627\u0646\u0641\u06cc\u06af \u067e\u0631\u0633\u0631\u0639\u062a VLESS \u062f\u0631 \u06a9\u0644\u06cc\u067e\u200c\u0628\u0648\u0631\u062f \u06a9\u067e\u06cc \u0634\u062f!\n\n\u0646\u062d\u0648\u0647 \u0627\u0633\u062a\u0641\u0627\u062f\u0647 \u062f\u0631 \u0622\u06cc\u0641\u0648\u0646:\n\u06f1. \u0648\u0627\u0631\u062f \u0628\u0631\u0646\u0627\u0645\u0647 V2Box \u0634\u0648\u06cc\u062f.\n\u06f2. \u0627\u0632 \u062a\u0628 Configs \u0631\u0648\u06cc + \u0628\u0632\u0646\u06cc\u062f.\n\u06f3. \u06af\u0632\u06cc\u0646\u0647 Import from Clipboard \u0631\u0627 \u0627\u0646\u062a\u062e\u0627\u0628 \u06a9\u0646\u06cc\u062f.\n\u06f4. \u062f\u06a9\u0645\u0647 Ping \u0628\u0632\u0646\u06cc\u062f \u0648 \u0628\u0647 \u0628\u0647\u062a\u0631\u06cc\u0646 \u0633\u0631\u0648\u0631 \u0648\u0635\u0644 \u0634\u0648\u06cc\u062f.")
                        .setPositiveButton("\u0645\u062a\u0648\u062c\u0647 \u0634\u062f\u0645",null)
                        .show();
                }else{
                    copyToClipboard("V2Ray Subscription","https://raw.githubusercontent.com/ProblemTheCode/SylphNet-public/refs/heads/main/sub/sub.txt");
                    new AlertDialog.Builder(this)
                        .setTitle("Subscription Copied")
                        .setMessage("\u0644\u06cc\u0646\u06a9 \u0633\u0627\u0628\u0633\u06a9\u0631\u06cc\u067e\u0634\u0646 \u0633\u0631\u0648\u0631\u0647\u0627 \u06a9\u067e\u06cc \u0634\u062f. \u0645\u06cc\u200c\u062a\u0648\u0627\u0646\u06cc\u062f \u062f\u0631 V2Box \u0627\u0632 \u0637\u0631\u06cc\u0642 Add Subscription \u0648\u0627\u0631\u062f \u06a9\u0646\u06cc\u062f.")
                        .setPositiveButton("\u0628\u0627\u0634\u0647",null)
                        .show();
                }
            });
        });
    }
    private void showV2RayModal(String email){
        LinearLayout layout=new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setPadding(dp(22),dp(12),dp(22),dp(12));

        TextView title=label("\u26a1 V2Ray & iOS Configs",18,GOLD);
        title.setTypeface(body,Typeface.BOLD);
        layout.addView(title);

        TextView desc=label("Top 30 ultra-fast, low-ping V2Ray / VLESS nodes ready for iPhone (V2Box / Streisand) with 24/7 cloud uptime.",13,MUTED);
        desc.setPadding(0,dp(4),0,dp(12));
        layout.addView(desc);

        Button copy30=button("\ud83d\udccb Copy 30 Ultra-Fast Configs (\u06cc\u06a9\u062c\u0627)",()->fetchAndCopyTop30Configs());
        copy30.setTextColor(BG);
        copy30.setBackground(background(GOLD));
        layout.addView(copy30);

        Button copySub=button("\ud83d\udd17 Copy Subscription Link (Top Nodes)",()->{
            copyToClipboard("V2Ray Subscription","https://raw.githubusercontent.com/ProblemTheCode/SylphNet-public/refs/heads/main/sub/sub.txt");
        });
        layout.addView(copySub);

        if(email!=null&&!email.isEmpty()){
            TextView fxTitle=label("⚡ Dedicated Secure Proxy (50 GB Quota)",14,TEXT);
            fxTitle.setTypeface(body,Typeface.BOLD);
            fxTitle.setPadding(0,dp(12),0,dp(4));
            layout.addView(fxTitle);

            TextView fxInfo=label("Connected to "+email+". Routes through encrypted Anycast nodes.",12,MUTED);
            layout.addView(fxInfo);

            Button fxBtn=button("Copy Dedicated Proxy Endpoint",()->{
                copyToClipboard("III85III Dedicated Proxy","http://proxy.cabicy.lvh.me:10820#Cabicy-VPN");
            });
            layout.addView(fxBtn);
        }

        new AlertDialog.Builder(this)
            .setView(layout)
            .setPositiveButton("Close",null)
            .show();
    }

    public static final class AdConfig {
        // Replace with your real Google AdMob IDs before Google Play production rollout:
        public static final String ADMOB_APP_ID = "ca-app-pub-3940256099942544~3347511713";
        public static final String BANNER_AD_UNIT_ID = "ca-app-pub-3940256099942544/6300978111";
        public static final String INTERSTITIAL_AD_UNIT_ID = "ca-app-pub-3940256099942544/1033173712";
        public static final long INTERSTITIAL_COOLDOWN_MS = 180_000L; // 3-minute cooldown compliant with Google Play Better Ads Policy
    }

    public static final class AdManager {
        private static boolean initialized = false;
        private static long lastInterstitialShownAt = 0L;
        private static Object loadedInterstitial = null;

        public static void init(Context context) {
            if (initialized) return;
            try {
                Class<?> mobileAds = Class.forName("com.google.android.gms.ads.MobileAds");
                mobileAds.getMethod("initialize", Context.class).invoke(null, context.getApplicationContext());
                initialized = true;
            } catch (Throwable ignored) {}
        }

        public static void attachBanner(Activity activity, ViewGroup container) {
            init(activity);
            try {
                Class<?> adViewClass = Class.forName("com.google.android.gms.ads.AdView");
                Class<?> adSizeClass = Class.forName("com.google.android.gms.ads.AdSize");
                Class<?> adRequestClass = Class.forName("com.google.android.gms.ads.AdRequest$Builder");

                Object adView = adViewClass.getConstructor(Context.class).newInstance(activity);
                Object bannerSize = adSizeClass.getField("BANNER").get(null);
                adViewClass.getMethod("setAdSize", adSizeClass).invoke(adView, bannerSize);
                adViewClass.getMethod("setAdUnitId", String.class).invoke(adView, AdConfig.BANNER_AD_UNIT_ID);

                Object reqBuilder = adRequestClass.getConstructor().newInstance();
                Object req = adRequestClass.getMethod("build").invoke(reqBuilder);
                adViewClass.getMethod("loadAd", Class.forName("com.google.android.gms.ads.AdRequest")).invoke(adView, req);
                container.removeAllViews();
                container.addView((View) adView);
                return;
            } catch (Throwable ignored) {}

            // Resilient Luxury Sponsor Banner (Obsidian Jet-Black & Liquid Gold Theme)
            LinearLayout luxuryBanner = new LinearLayout(activity);
            luxuryBanner.setOrientation(LinearLayout.HORIZONTAL);
            luxuryBanner.setGravity(Gravity.CENTER_VERTICAL);
            int dp12 = Math.round(12 * activity.getResources().getDisplayMetrics().density);
            int dp6 = Math.round(6 * activity.getResources().getDisplayMetrics().density);
            int dp8 = Math.round(8 * activity.getResources().getDisplayMetrics().density);
            luxuryBanner.setPadding(dp12, dp6, dp12, dp6);

            android.graphics.drawable.GradientDrawable bg = new android.graphics.drawable.GradientDrawable();
            bg.setColor(0x181B2028);
            bg.setCornerRadius(dp12);
            bg.setStroke(Math.round(1 * activity.getResources().getDisplayMetrics().density), 0x22D9BD83);
            luxuryBanner.setBackground(bg);

            TextView tag = new TextView(activity);
            tag.setText("SPONSOR");
            tag.setTextSize(10);
            tag.setTextColor(0xFFD9BD83);
            tag.setTypeface(Typeface.DEFAULT_BOLD);
            tag.setLetterSpacing(0.08f);

            TextView desc = new TextView(activity);
            desc.setText("III85III Private Relays \u00b7 Ultra Low Latency");
            desc.setTextSize(12);
            desc.setTextColor(0xFF9FA7B4);
            desc.setPadding(dp8, 0, 0, 0);

            luxuryBanner.addView(tag);
            luxuryBanner.addView(desc, new LinearLayout.LayoutParams(0, -2, 1));
            container.removeAllViews();
            container.addView(luxuryBanner);
        }

        public static void maybeShowInterstitial(Activity activity) {
            long now = SystemClock.elapsedRealtime();
            if (now - lastInterstitialShownAt < AdConfig.INTERSTITIAL_COOLDOWN_MS) return;
            init(activity);
            try {
                Class<?> interstitialClass = Class.forName("com.google.android.gms.ads.interstitial.InterstitialAd");
                if (loadedInterstitial != null) {
                    interstitialClass.getMethod("show", Activity.class).invoke(loadedInterstitial, activity);
                    loadedInterstitial = null;
                    lastInterstitialShownAt = now;
                }
            } catch (Throwable ignored) {}
        }
    }
}
