package io.github.iii85iii.vpn.probe;

import android.app.Activity;
import android.os.Bundle;
import android.util.Log;
import java.io.*;
import java.net.*;

/** Disposable CI probe: own UID, no accounts, one DNS packet to the VPN interface. */
public final class ProbeActivity extends Activity {
    @Override public void onCreate(Bundle state){super.onCreate(state);new Thread(()->{try{
        ByteArrayOutputStream buffer=new ByteArrayOutputStream();DataOutputStream query=new DataOutputStream(buffer);query.writeShort(0x1234);query.writeShort(0x0100);query.writeShort(1);query.writeShort(0);query.writeShort(0);query.writeShort(0);
        for(String label:"iii85iii-smoke.test".split("\\.")){byte[] bytes=label.getBytes("US-ASCII");query.writeByte(bytes.length);query.write(bytes);}query.writeByte(0);query.writeShort(1);query.writeShort(1);
        try(DatagramSocket socket=new DatagramSocket()){
            socket.setSoTimeout(6000);byte[] request=buffer.toByteArray();socket.send(new DatagramPacket(request,request.length,InetAddress.getByName("172.19.0.2"),53));byte[] answer=new byte[2048];DatagramPacket response=new DatagramPacket(answer,answer.length);socket.receive(response);int count=response.getLength();
            if(count<16||(answer[0]&255)!=0x12||(answer[1]&255)!=0x34||(answer[2]&128)==0||(answer[3]&15)!=0||(answer[count-4]&255)!=198||((answer[count-3]&255)!=18&&(answer[count-3]&255)!=19))throw new IOException("Unexpected synthetic DNS response");
            Log.i("III85IIIProbe","PASS native TUN DNS returned a synthetic IPv4 address");
        }
    }catch(Exception error){Log.e("III85IIIProbe","FAIL "+error.getClass().getSimpleName());}finally{runOnUiThread(()->finish());}},"vpn-probe").start();}
}
