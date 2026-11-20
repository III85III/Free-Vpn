package io.github.iii85iii.vpn;

import java.net.URI;

final class UpdatePolicy {
    static boolean required(long installed,long offered){return offered>installed;}
    static final String FEED="https://raw.githubusercontent.com/III85III/Free-Vpn/main/docs/updates.json";
    static void validate(String address,String hash,long size,long version)throws Exception{
        URI uri=new URI(address);
        if(!"https".equals(uri.getScheme())||!"github.com".equals(uri.getHost())||uri.getPort()!=-1||uri.getUserInfo()!=null||uri.getQuery()!=null||uri.getFragment()!=null||!uri.getPath().startsWith("/III85III/Free-Vpn/releases/download/")||!uri.getPath().endsWith("/III85III-VPN.apk")||uri.getRawPath().contains("%")||uri.getPath().contains(".."))throw new IllegalArgumentException("Invalid update download");
        if(!hash.matches("[0-9a-fA-F]{64}")||size<1024||size>250L*1024*1024||version<1)throw new IllegalArgumentException("Invalid update metadata");
    }
    static boolean trustedRedirect(URI uri){String host=uri.getHost();return "https".equals(uri.getScheme())&&uri.getUserInfo()==null&&(uri.getPort()==-1||uri.getPort()==443)&&host!=null&&(host.equals("github.com")||host.equals("raw.githubusercontent.com")||host.endsWith(".githubusercontent.com"));}
}
