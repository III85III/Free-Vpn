package io.github.iii85iii.vpn;

import java.util.Locale;
import java.util.regex.Pattern;

final class ErrorMessages {
    private static boolean http(String text,int status){
        return Pattern.compile("\\bhttp(?:\\s+status)?[\\s:=]+"+status+"\\b").matcher(text).find();
    }
    static String summary(String detail){
        String text=detail==null?"":detail.toLowerCase(Locale.ROOT);
        if(text.contains("cross-origin request refused")||text.contains("open the dashboard by ip address or localhost")||text.contains("only the app on this device"))
            return "The app couldn't authenticate with its local engine. Close and reopen the app.";
        if(text.contains("account not entitled")||text.contains("not_entitled"))
            return "Mozilla hasn't granted VPN access to this account. Check its service eligibility.";
        if(http(text,403))
            return "The request was refused (403). Open Technical details to check which service refused it.";
        if(http(text,401)||text.contains("sign-in required")||text.contains("reauth_required"))
            return "Your session wasn't accepted. Sign in again with your own Mozilla account.";
        if(text.contains("quota_exhausted")||text.contains("quota exceeded"))
            return "This account's VPN allowance is used up. Check its renewal date.";
        if(http(text,429))return "Too many requests. Wait before trying again.";
        if(text.contains("unknownhostexception")||text.contains("unable to resolve host"))
            return "The server address couldn't be resolved. Check your network's DNS and retry.";
        if(text.contains("timed out")||text.contains("timeout"))
            return "The server took too long to respond. Please retry.";
        if(text.contains("account")||text.contains("sign-in"))return "Sign-in couldn't finish. Open Technical details for the reason.";
        return "The request couldn't finish. Open Technical details for the reason.";
    }
    static String title(String detail){
        String text=detail==null?"":detail.toLowerCase(Locale.ROOT);
        if(http(text,403))return "Request refused";
        if(text.contains("sign-in")||text.contains("account")||http(text,401))return "Account problem";
        return "Something went wrong";
    }
}
