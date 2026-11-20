package io.github.iii85iii.vpn;

/** Shared UI/service state. No method reconnects after a manual stop. */
final class VpnLifecycle {
    enum Phase { OFF, STARTING, ACTIVE, STOPPING }
    private Phase phase=Phase.OFF;
    synchronized Phase phase(){return phase;}
    synchronized boolean start(){if(phase!=Phase.OFF)return false;phase=Phase.STARTING;return true;}
    synchronized boolean activate(){if(phase!=Phase.STARTING)return false;phase=Phase.ACTIVE;return true;}
    synchronized boolean stop(){if(phase==Phase.STOPPING)return false;phase=Phase.STOPPING;return true;}
    synchronized void finished(){phase=Phase.OFF;}
    synchronized boolean canCancel(){return phase==Phase.STARTING||phase==Phase.ACTIVE;}
}
