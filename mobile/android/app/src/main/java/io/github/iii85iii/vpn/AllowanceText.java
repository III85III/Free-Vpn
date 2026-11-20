package io.github.iii85iii.vpn;

import java.util.Locale;

final class AllowanceText {
    static String describe(boolean account,boolean usage,boolean unlimited,double remaining,double limit){
        if(!account)return "Sign in to see your allowance";
        if(!usage)return "Allowance unavailable";
        if(unlimited)return "Unlimited allowance";
        if(!Double.isFinite(remaining)||!Double.isFinite(limit)||remaining<0||limit<=0)return "Allowance unavailable";
        if(remaining==0)return "No allowance remaining";
        String[] units={"B","KB","MB","GB","TB"};int unit=0;
        while(remaining>=1000&&unit<units.length-1){remaining/=1000;unit++;}
        return String.format(Locale.US,unit==0?"%.0f %s remaining":"%.1f %s remaining",remaining,units[unit]);
    }
}
