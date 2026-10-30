package io.github.iii85iii.vpn;

final class AccountSetup {
    static final String URL="https://accounts.firefox.com/";
    static final String MESSAGE="Create a Firefox/Mozilla account before connecting.\nThen sign in here with its email and Mozilla password.";
    static boolean shouldShow(boolean seen,int accountCount,boolean updateRequired){return !seen&&accountCount==0&&!updateRequired;}
    private AccountSetup() {}
}
