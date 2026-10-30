package io.github.iii85iii.vpn;

import org.junit.Test;
import java.io.*;
import java.net.*;
import java.nio.file.Files;
import java.util.*;
import static org.junit.Assert.*;

public final class UpdateTransferTest {
    private static HttpURLConnection reply(int code,String range,byte[] bytes)throws Exception{
        return new HttpURLConnection(new URL("https://github.com/")){
            public int getResponseCode(){return code;}
            public String getHeaderField(String key){return "Content-Range".equals(key)?range:null;}
            public InputStream getInputStream(){return new ByteArrayInputStream(bytes);}
            public void connect(){} public void disconnect(){} public boolean usingProxy(){return false;}
        };
    }
    @Test public void interruptedTransferResumesAtWrittenOffset()throws Exception{
        File file=File.createTempFile("update-",".part");List<Long> offsets=new ArrayList<>();List<Integer> progress=new ArrayList<>();
        try{
            UpdateTransfer.download(file,6,offset->{offsets.add(offset);return offset==0?reply(200,null,new byte[]{1,2,3}):reply(206,"bytes 3-5/6",new byte[]{4,5,6});},progress::add);
            assertEquals(Arrays.asList(0L,3L),offsets);assertArrayEquals(new byte[]{1,2,3,4,5,6},Files.readAllBytes(file.toPath()));assertEquals(Integer.valueOf(99),progress.get(progress.size()-1));
        }finally{file.delete();}
    }
    @Test public void serverIgnoringRangeRestartsWithoutAppending()throws Exception{
        File file=File.createTempFile("update-",".part");int[] count={0};
        try{UpdateTransfer.download(file,3,offset->++count[0]==1?reply(200,null,new byte[]{9}):reply(200,null,new byte[]{1,2,3}),p->{});assertArrayEquals(new byte[]{1,2,3},Files.readAllBytes(file.toPath()));}finally{file.delete();}
    }
    @Test public void retriesAreBoundedAndNeverAcceptIncompleteData()throws Exception{
        File file=File.createTempFile("update-",".part");int[] count={0};
        try{try{UpdateTransfer.download(file,3,offset->{count[0]++;return reply(200,null,new byte[]{1});},p->{});fail("Incomplete update accepted");}catch(EOFException expected){}assertEquals(3,count[0]);}finally{file.delete();}
    }
    @Test public void rejectsUnexpectedRange()throws Exception{
        File file=File.createTempFile("update-",".part");
        try{try{UpdateTransfer.download(file,3,offset->reply(206,"bytes 2-4/8",new byte[]{1,2,3}),p->{});fail("Wrong range accepted");}catch(IOException expected){assertEquals(0,file.length());}}finally{file.delete();}
    }
    @Test public void rejectsOversizedFile()throws Exception{
        File file=File.createTempFile("update-",".part");
        try{try{UpdateTransfer.download(file,2,offset->reply(200,null,new byte[]{1,2,3}),p->{});fail("Oversized update accepted");}catch(IOException expected){assertEquals(0,file.length());}}finally{file.delete();}
    }
}
