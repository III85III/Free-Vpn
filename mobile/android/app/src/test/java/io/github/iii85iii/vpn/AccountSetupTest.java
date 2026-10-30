package io.github.iii85iii.vpn;
import org.junit.Test;
import static org.junit.Assert.*;
public class AccountSetupTest {
    @Test public void firstEmptyProfileGetsIntro(){assertTrue(AccountSetup.shouldShow(false,0,false));}
    @Test public void existingAccountOrSeenIntroDoesNotRepeat(){assertFalse(AccountSetup.shouldShow(false,1,false));assertFalse(AccountSetup.shouldShow(true,0,false));}
    @Test public void updateGateAndUnknownAccountStateTakePriority(){assertFalse(AccountSetup.shouldShow(false,0,true));assertFalse(AccountSetup.shouldShow(false,-1,false));}
    @Test public void setupNamesMozillaCredentials(){assertTrue(AccountSetup.MESSAGE.contains("Mozilla password"));}
}
