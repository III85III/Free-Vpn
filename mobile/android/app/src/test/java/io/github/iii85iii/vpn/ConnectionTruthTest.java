package io.github.iii85iii.vpn;

import org.junit.Test;
import static org.junit.Assert.*;

public final class ConnectionTruthTest {
    private final String started="2026-10-29T10:00:00Z",fresh="2026-10-29T10:00:05.123Z",old="2026-10-29T09:59:00Z";
    @Test public void freshVerifiedTunnelIsConnected(){assertTrue(ConnectionTruth.verified(true,"running",true,fresh,started,""));}
    @Test public void oldTestFromPreviousConnectionIsRejected(){assertFalse(ConnectionTruth.verified(true,"running",true,old,started,""));}
    @Test public void stoppedTunnelCannotLookConnected(){assertFalse(ConnectionTruth.verified(false,"running",true,fresh,started,""));}
    @Test public void errorPhaseCannotLookConnected(){assertFalse(ConnectionTruth.verified(true,"error",true,fresh,started,""));}
    @Test public void failedProbeCannotLookConnected(){assertFalse(ConnectionTruth.verified(true,"running",false,fresh,started,""));}
    @Test public void malformedTimesFailClosed(){assertFalse(ConnectionTruth.verified(true,"running",true,"unknown",started,""));assertFalse(ConnectionTruth.verified(true,"running",true,fresh,"unknown",""));}
    @Test public void networkChangeInvalidatesOldProbe(){assertFalse(ConnectionTruth.verified(true,"running",true,fresh,started,"2026-10-29T10:01:00Z"));}
    @Test public void timezoneOffsetsDescribeSameInstant(){assertTrue(ConnectionTruth.verified(true,"running",true,"2026-10-29T13:30:05+03:30",started,""));}
}
