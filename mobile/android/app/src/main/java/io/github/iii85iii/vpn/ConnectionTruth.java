package io.github.iii85iii.vpn;

import java.time.OffsetDateTime;
import java.time.format.DateTimeParseException;

final class ConnectionTruth {
    private static long timestamp(String value){try{return OffsetDateTime.parse(value).toInstant().toEpochMilli();}catch(DateTimeParseException|NullPointerException invalid){return -1;}}
    static boolean verified(boolean tunnelActive,String phase,boolean ok,String testedAt,String startedAt,String networkChangedAt){
        if(!tunnelActive||!"running".equals(phase)||!ok)return false;
        long tested=timestamp(testedAt),started=timestamp(startedAt),changed=timestamp(networkChangedAt);
        return tested>=0&&started>=0&&tested>=started&&(changed<0||tested>=changed);
    }
}
