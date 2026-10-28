'use strict';
const {test}=require('node:test');const assert=require('node:assert/strict');const http2=require('node:http2');const net=require('node:net');
const {TunnelGateway}=require('../src/gateway.cjs');
test('independent gateway authenticates local CONNECT and carries bytes over real HTTP/2',async()=>{
  const server=http2.createServer();let observed;
  server.on('stream',(stream,headers)=>{observed=headers;stream.respond({':status':200});stream.on('data',chunk=>stream.write(chunk));stream.on('end',()=>stream.end());stream.on('error',()=>{});});
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const gateway=new TunnelGateway({credentials:async()=>'dummy-proxy-pass',upstream:'http://127.0.0.1:'+server.address().port});
  const address=await gateway.listen();
  try{
    const response=await new Promise((resolve,reject)=>{
      const socket=net.connect(address.port,address.host);let buffer=Buffer.alloc(0),sent=false;
      socket.setTimeout(4000,()=>socket.destroy(new Error('test timed out')));socket.on('error',reject);
      socket.on('connect',()=>socket.write('CONNECT example.com:443 HTTP/1.1\r\nHost: example.com:443\r\nProxy-Authorization: Basic '+Buffer.from(address.username+':'+address.password).toString('base64')+'\r\n\r\n'));
      socket.on('data',chunk=>{buffer=Buffer.concat([buffer,chunk]);if(!sent&&buffer.includes(Buffer.from('\r\n\r\n'))){assert.match(buffer.toString(),/200 Connection Established/);sent=true;buffer=Buffer.alloc(0);socket.write('independent-payload');}else if(sent&&buffer.toString()==='independent-payload'){socket.destroy();resolve(buffer.toString());}});
    });
    assert.equal(response,'independent-payload');assert.equal(observed[':method'],'CONNECT');assert.equal(observed[':authority'],'example.com:443');assert.equal(observed['proxy-authorization'],'Bearer dummy-proxy-pass');
    const denied=await new Promise((resolve,reject)=>{const s=net.connect(address.port,address.host);s.setTimeout(3000,()=>s.destroy(new Error('timeout')));s.on('error',reject);let text='';s.on('connect',()=>s.write('CONNECT example.com:443 HTTP/1.1\r\nHost: example.com:443\r\n\r\n'));s.on('data',b=>text+=b);s.on('end',()=>resolve(text));});
    assert.match(denied,/407 Proxy Authentication Required/);
  }finally{await gateway.close();await new Promise(resolve=>server.close(resolve));}
});

test('early tunnel bytes remain buffered until a consumer attaches',async()=>{
  const server=http2.createServer();
  server.on('stream',stream=>{stream.on('error',()=>{});stream.respond({':status':200});stream.write('early-server-bytes');});
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const gateway=new TunnelGateway({credentials:async()=>'test-only',upstream:'http://127.0.0.1:'+server.address().port});
  try {
    const stream=await gateway.open('example.com:443');
    await new Promise(resolve=>setTimeout(resolve,40));
    const text=await new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(new Error('Early data was lost')),1000);stream.once('data',data=>{clearTimeout(timer);resolve(data.toString());});stream.once('error',reject);});
    assert.equal(text,'early-server-bytes');stream.close();
  } finally {await gateway.close();await new Promise(resolve=>server.close(resolve));}
});

test('an upstream close before CONNECT acceptance fails promptly',async()=>{
  const server=http2.createServer();
  server.on('stream',stream=>{stream.on('error',()=>{});stream.close(http2.constants.NGHTTP2_REFUSED_STREAM);});
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const gateway=new TunnelGateway({credentials:async()=>'test-only',upstream:'http://127.0.0.1:'+server.address().port});
  try {
    await assert.rejects(Promise.race([gateway.open('example.com:443'),new Promise((_,reject)=>{const timeout=setTimeout(()=>reject(new Error('failure was not prompt')),1200);timeout.unref();})]),/Upstream (stream failed|closed the tunnel)/);
  } finally {await gateway.close();await new Promise(resolve=>server.close(resolve));}
});
