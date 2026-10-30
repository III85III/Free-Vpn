package io.github.iii85iii.vpn;

final class AccountSetup {
    static final String MESSAGE="To use VPN, first create a Firefox/Mozilla account.\nThen enter its email and Mozilla password in the app.";
    static boolean shouldShow(boolean seen,int accountCount,boolean updateRequired){return !seen&&accountCount==0&&!updateRequired;}
    private AccountSetup() {}
}
