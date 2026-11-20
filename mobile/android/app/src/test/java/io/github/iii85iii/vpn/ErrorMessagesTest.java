package io.github.iii85iii.vpn;

import org.junit.Test;
import static org.junit.Assert.*;

public final class ErrorMessagesTest {
    @Test public void remoteDenialIsNotCalledAnInternetFailure(){
        String message=ErrorMessages.summary("FxA request failed (HTTP 403): refused");
        assertTrue(message.contains("refused (403)"));assertFalse(message.contains("Check your connection"));
        assertEquals("Request refused",ErrorMessages.title("FxA request failed (HTTP 403)"));
    }
    @Test public void localRejectionDoesNotAskForMozillaPassword(){
        assertTrue(ErrorMessages.summary("cross-origin request refused (HTTP 403)").contains("local engine"));
        assertFalse(ErrorMessages.summary("cross-origin request refused (HTTP 403)").contains("Sign in again"));
    }
    @Test public void explicitEntitlementHasDifferentAdvice(){
        assertTrue(ErrorMessages.summary("pass returned HTTP 403 (account not entitled)").contains("service eligibility"));
    }
    @Test public void quotaAndRateLimitsAreDifferent(){
        assertTrue(ErrorMessages.summary("HTTP 429 (quota exceeded)").contains("renewal date"));
        assertTrue(ErrorMessages.summary("FxA request failed HTTP 429").contains("Wait"));
    }
    @Test public void statusMatchingDoesNotTreatAccountIdentifiersAsHttpStatus(){
        assertFalse(ErrorMessages.summary("Account 403123 unavailable").contains("refused (403)"));
        assertFalse(ErrorMessages.summary("HTTP 4030").contains("refused (403)"));
    }
    @Test public void safeSummaryDoesNotEchoPrivateServerResponses(){
        assertFalse(ErrorMessages.summary("HTTP 403 email=private@example.test token=secret").contains("private@example.test"));
        assertFalse(ErrorMessages.summary(null).isEmpty());
    }
}
