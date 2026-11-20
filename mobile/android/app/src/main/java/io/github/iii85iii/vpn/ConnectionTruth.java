package io.github.iii85iii.vpn;

import java.time.OffsetDateTime;
import java.time.format.DateTimeParseException;
import org.json.JSONObject;

final class ConnectionTruth {
    private static long timestamp(String value){try{return OffsetDateTime.parse(value).toInstant().toEpochMilli();}catch(DateTimeParseException|NullPointerException invalid){return -1;}}
    static boolean verified(boolean tunnelActive,String phase,boolean ok,String testedAt,String startedAt,String networkChangedAt){
        if(!tunnelActive||!"running".equals(phase)||!ok)return false;
        return fresh(testedAt,startedAt,networkChangedAt);
    }
    static boolean verified(boolean tunnelActive, JSONObject status, String tunnelStartedAt) {
        JSONObject state = status.optJSONObject("state"), test = status.optJSONObject("last_test");
        if (state == null || test == null) return false;
        String testedAt = test.optString("at");
        return verified(tunnelActive, state.optString("phase"), test.optBoolean("ok"), testedAt,
            state.optString("since"), status.optString("network_changed_at")) &&
            fresh(testedAt, tunnelStartedAt, "");
    }
    static boolean fresh(String testedAt,String startedAt,String networkChangedAt){
        long tested=timestamp(testedAt),started=timestamp(startedAt),changed=timestamp(networkChangedAt);
        return tested>=0&&started>=0&&tested>=started&&(changed<0||tested>=changed);
    }
}
