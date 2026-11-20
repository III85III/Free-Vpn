package io.github.iii85iii.vpn;
import org.junit.Test;
import static org.junit.Assert.*;
import java.net.URI;

public final class UpdatePolicyTest {
    @Test public void newerVersionIsMandatory(){assertTrue(UpdatePolicy.required(23003,23004));}
    @Test public void installingRequiredVersionClearsGate(){assertFalse(UpdatePolicy.required(23004,23004));assertFalse(UpdatePolicy.required(23005,23004));}
    private static final String URL="https://github.com/III85III/Free-Vpn/releases/download/v2.3.1/III85III-VPN.apk",HASH="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    @Test public void acceptsOfficialApk()throws Exception{UpdatePolicy.validate(URL,HASH,12000000,23002);}
    @Test public void rejectsOtherRepositories()throws Exception{reject(URL.replace("III85III/Free-Vpn","other/project"),HASH,12000000,23002);}
    @Test public void rejectsInsecureDownload()throws Exception{reject(URL.replace("https:","http:"),HASH,12000000,23002);}
    @Test public void rejectsMissingChecksum()throws Exception{reject(URL,"",12000000,23002);}
    @Test public void rejectsUnboundedSize()throws Exception{reject(URL,HASH,300L*1024*1024,23002);}
    @Test public void refusesUntrustedRedirects()throws Exception{assertFalse(UpdatePolicy.trustedRedirect(new URI("https://github.com.evil.example/file.apk")));assertFalse(UpdatePolicy.trustedRedirect(new URI("http://release-assets.githubusercontent.com/file.apk")));assertTrue(UpdatePolicy.trustedRedirect(new URI("https://release-assets.githubusercontent.com/file.apk?signature=example")));}
    private static void reject(String url,String hash,long size,long code)throws Exception{try{UpdatePolicy.validate(url,hash,size,code);fail("Invalid update accepted");}catch(IllegalArgumentException expected){}}
}
