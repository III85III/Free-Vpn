package io.github.iii85iii.vpn;

import android.app.Activity;
import android.os.Build;
import android.view.View;
import android.view.WindowInsets;

final class WindowLayout {
    static void apply(Activity activity,View root){
        if(Build.VERSION.SDK_INT<30)return;
        int left=root.getPaddingLeft(),top=root.getPaddingTop(),right=root.getPaddingRight(),bottom=root.getPaddingBottom();
        activity.getWindow().setDecorFitsSystemWindows(false);
        root.setOnApplyWindowInsetsListener((view,value)->{
            android.graphics.Insets safe=value.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout()|WindowInsets.Type.ime());
            view.setPadding(left+safe.left,top+safe.top,right+safe.right,bottom+safe.bottom);return value;
        });
        root.requestApplyInsets();
    }
}
