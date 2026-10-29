package io.github.iii85iii.vpn;
import org.junit.Test;
import static org.junit.Assert.*;
public class LocationOrderTest {
    @Test public void automaticOptionsStayAboveCountries(){assertTrue(LocationOrder.compare("fastest","Fastest",0,"de","Germany",1)<0);assertTrue(LocationOrder.compare("","Recommended",0,"fastest","Fastest",0)<0);assertTrue(LocationOrder.compare("REC","Automatic",0,"gb","UK",1)<0);}
    @Test public void countriesUseMeasuredLatency(){assertTrue(LocationOrder.compare("de","Germany",20,"gb","UK",80)<0);assertTrue(LocationOrder.compare("de","Germany",20,"xx","Unknown",0)<0);}
    @Test public void equalLatencyHasStableNameOrder(){assertTrue(LocationOrder.compare("ca","Canada",20,"de","Germany",20)<0);}
    @Test public void onlyAutomaticChoicesArePinned(){assertTrue(LocationOrder.pinned(""));assertTrue(LocationOrder.pinned("fastest"));assertTrue(LocationOrder.pinned("REC"));assertFalse(LocationOrder.pinned("gb"));}
}
