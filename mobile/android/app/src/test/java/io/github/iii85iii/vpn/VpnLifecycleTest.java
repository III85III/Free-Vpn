package io.github.iii85iii.vpn;

import org.junit.Test;
import static org.junit.Assert.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;

public final class VpnLifecycleTest {
    @Test public void startsDisconnected(){assertEquals(VpnLifecycle.Phase.OFF,new VpnLifecycle().phase());}
    @Test public void repeatedStartDoesNotQueueConnections(){VpnLifecycle flow=new VpnLifecycle();assertTrue(flow.start());assertFalse(flow.start());assertEquals(VpnLifecycle.Phase.STARTING,flow.phase());}
    @Test public void aStartingConnectionCanBeCancelled(){VpnLifecycle flow=new VpnLifecycle();flow.start();assertTrue(flow.canCancel());flow.stop();assertFalse(flow.activate());}
    @Test public void manualStopCannotTurnIntoAnAutomaticReconnect(){VpnLifecycle flow=new VpnLifecycle();flow.start();flow.activate();flow.stop();assertFalse(flow.start());flow.finished();assertFalse(flow.canCancel());assertEquals(VpnLifecycle.Phase.OFF,flow.phase());}
    @Test public void activeConnectionCanBeStoppedOnce(){VpnLifecycle flow=new VpnLifecycle();flow.start();assertTrue(flow.activate());assertTrue(flow.stop());assertFalse(flow.stop());}
    @Test public void concurrentStartsHaveOneWinner()throws Exception{
        VpnLifecycle flow=new VpnLifecycle();CountDownLatch release=new CountDownLatch(1);AtomicInteger starts=new AtomicInteger();ExecutorService pool=Executors.newFixedThreadPool(6);
        try{for(int n=0;n<12;n++)pool.submit(()->{try{release.await();if(flow.start())starts.incrementAndGet();}catch(InterruptedException e){Thread.currentThread().interrupt();}});release.countDown();pool.shutdown();assertTrue(pool.awaitTermination(2,TimeUnit.SECONDS));assertEquals(1,starts.get());}finally{pool.shutdownNow();}
    }
}
