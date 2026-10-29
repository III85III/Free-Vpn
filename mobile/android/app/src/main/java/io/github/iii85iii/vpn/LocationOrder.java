package io.github.iii85iii.vpn;

final class LocationOrder {
    private LocationOrder() {}
    static boolean pinned(String code) {return code.isEmpty()||code.equals("fastest")||code.equals("REC");}
    static int compare(String codeA,String nameA,int pingA,String codeB,String nameB,int pingB){
        int rankA=rank(codeA),rankB=rank(codeB);
        if(rankA!=rankB)return Integer.compare(rankA,rankB);
        int result=Integer.compare(pingA>0?pingA:Integer.MAX_VALUE,pingB>0?pingB:Integer.MAX_VALUE);
        if(result!=0)return result;
        result=nameA.compareToIgnoreCase(nameB);
        return result!=0?result:codeA.compareToIgnoreCase(codeB);
    }
    private static int rank(String code){return code.isEmpty()?0:code.equals("fastest")?1:code.equals("REC")?2:3;}
}
