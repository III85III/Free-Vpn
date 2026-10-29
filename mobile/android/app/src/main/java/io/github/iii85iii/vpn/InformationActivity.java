package io.github.iii85iii.vpn;

import android.app.Activity;
import android.os.Bundle;
import android.graphics.Color;
import android.widget.*;
import java.io.*;
import java.nio.charset.StandardCharsets;

public final class InformationActivity extends Activity {
    @Override public void onCreate(Bundle state){
        super.onCreate(state);LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);root.setBackgroundColor(Color.rgb(16,19,25));Button back=new Button(this);back.setText("Back to settings");back.setAllCaps(false);back.setOnClickListener(v->finish());root.addView(back);ScrollView scroll=new ScrollView(this);TextView text=new TextView(this);text.setTextColor(Color.rgb(239,239,234));text.setTextSize(15);text.setTextIsSelectable(true);try{text.setTypeface(android.graphics.Typeface.createFromAsset(getAssets(),"fonts/Lato-Regular.ttf"));}catch(RuntimeException ignored){}int pad=(int)(20*getResources().getDisplayMetrics().density);text.setPadding(pad,pad,pad,pad);scroll.addView(text);root.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));setContentView(root);
        WindowLayout.apply(this,root);
        boolean licenses="licenses".equals(getIntent().getStringExtra("document"));
        CoreRuntime.IO.execute(()->{StringBuilder content=new StringBuilder();try{if(licenses){append("fonts/OFL.txt",content);list("licenses",content);}else append("privacy.txt",content);}catch(IOException unavailable){content.append("This document could not be opened.");}runOnUiThread(()->{if(!isDestroyed())text.setText(content.toString());});});
    }
    private void list(String directory,StringBuilder output)throws IOException{for(String child:getAssets().list(directory)){String path=directory+"/"+child;if(getAssets().list(path).length>0)list(path,output);else append(path,output);}}
    private void append(String path,StringBuilder output)throws IOException{if(output.length()>400000)return;if(!path.equals("privacy.txt"))output.append(path).append("\n\n");try(InputStream input=getAssets().open(path);ByteArrayOutputStream data=new ByteArrayOutputStream()){byte[] buffer=new byte[4096];int n;while((n=input.read(buffer))!=-1){if(data.size()>200000)throw new IOException("Document too large");data.write(buffer,0,n);}output.append(data.toString(StandardCharsets.UTF_8.name())).append("\n\n");}}
}
