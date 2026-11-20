package io.github.iii85iii.vpn;

import org.junit.Test;
import static org.junit.Assert.*;

public final class AllowanceTextTest {
    @Test public void missingUsageDoesNotInventFiftyGigabytes(){assertEquals("Allowance unavailable",AllowanceText.describe(true,false,false,0,0));}
    @Test public void noAccountInvitesSignIn(){assertEquals("Sign in to see your allowance",AllowanceText.describe(false,false,false,0,0));}
    @Test public void exhaustedAllowanceStaysZero(){assertEquals("No allowance remaining",AllowanceText.describe(true,true,false,0,50000000000d));}
    @Test public void remainingComesFromServiceData(){assertEquals("12.5 GB remaining",AllowanceText.describe(true,true,false,12500000000d,50000000000d));}
    @Test public void invalidUsageIsUnknown(){assertEquals("Allowance unavailable",AllowanceText.describe(true,true,false,Double.NaN,0));assertEquals("Allowance unavailable",AllowanceText.describe(true,true,false,-1,50000000000d));}
    @Test public void unlimitedRequiresExplicitServiceFlag(){assertEquals("Unlimited allowance",AllowanceText.describe(true,true,true,0,0));assertEquals("Allowance unavailable",AllowanceText.describe(true,true,false,0,0));}
}
