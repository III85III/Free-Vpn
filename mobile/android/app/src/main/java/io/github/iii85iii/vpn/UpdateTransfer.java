package io.github.iii85iii.vpn;

import java.io.*;
import java.net.HttpURLConnection;

/** A bounded, resumable download. Callers must verify the completed file before use. */
final class UpdateTransfer {
    interface Connection { HttpURLConnection open(long offset)throws Exception; }
    interface Progress { void changed(int percent); }
    static void download(File file,long size,Connection source,Progress progress)throws Exception {
        if(file.exists()&&!file.delete())throw new IOException("Cannot reset partial update");
        Exception failure=null;
        for(int attempt=0;attempt<3;attempt++){
            HttpURLConnection request=null;
            try{
                long offset=file.isFile()?file.length():0;
                request=source.open(offset);
                int response=request.getResponseCode();
                if(response==206){
                    String range=request.getHeaderField("Content-Range");
                    if(range==null||!range.startsWith("bytes "+offset+"-")||!range.endsWith("/"+size))throw new IOException("Unexpected download range");
                }else if(response==200){offset=0;}else throw new IOException("Update unavailable");
                try(InputStream input=request.getInputStream();FileOutputStream output=new FileOutputStream(file,offset>0)){
                    long total=offset;byte[] buffer=new byte[65536];int count;
                    while((count=input.read(buffer))!=-1){
                        if(total+count>size)throw new IOException("Update exceeds expected size");
                        output.write(buffer,0,count);total+=count;
                        // 100% means the file has also passed verification, not just transfer.
                        progress.changed((int)Math.min(99,total*100/size));
                    }
                    output.getFD().sync();
                    if(total==size)return;
                    throw new EOFException("Incomplete update");
                }
            }catch(Exception error){failure=error;
                if(file.length()>=size)break;
            }finally{if(request!=null)request.disconnect();}
        }
        throw failure==null?new IOException("Download failed"):failure;
    }
}
