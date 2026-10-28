'use strict';
const http=require('node:http');
const http2=require('node:http2');
const crypto=require('node:crypto');
const {EventEmitter}=require('node:events');

function same(a,b){const x=Buffer.from(a),y=Buffer.from(b);return x.length===y.length&&crypto.timingSafeEqual(x,y);}
function destination(authority){
  if(typeof authority!=='string'||authority.length>512||/[\s\r\n/@?#]/.test(authority))throw new Error('Invalid destination');
  const url=new URL('http://'+authority);
  const port=Number(url.port)||80;
  if(![80,443,8080,8443].includes(port))throw new Error('This service supports web ports only');
  if(!url.hostname || url.username || url.password)throw new Error('Invalid destination');
  return {authority:authority,host:url.hostname,port};
}
class TunnelGateway extends EventEmitter {
  constructor({credentials,upstream='https://p.m1.fastly-masque.net:2499',connect=http2.connect}={}){
    super();this.credentials=credentials;this.upstream=upstream;this.connectH2=connect;
    this.session=null;this.connecting=null;this.server=null;this.sockets=new Set();this.streams=new Set();this.sessions=new Set();
    this.username='III85III';this.password=crypto.randomBytes(24).toString('base64url');this.sent=0;this.received=0;
  }
  async upstreamSession(){
    if(this.session&&!this.session.closed&&!this.session.destroyed)return this.session;
    if(this.connecting)return this.connecting;
    this.connecting=new Promise((resolve,reject)=>{
      let done=false;const session=this.connectH2(this.upstream,{settings:{enablePush:false,initialWindowSize:1024*1024}});
      this.sessions.add(session);session.once('close',()=>this.sessions.delete(session));
      const timeout=setTimeout(()=>finish(new Error('Upstream connection timed out')),18000);
      const finish=error=>{if(done)return;done=true;clearTimeout(timeout);if(error){session.destroy();reject(error);}else{this.session=session;resolve(session);}};
      session.once('connect',()=>{session.setLocalWindowSize(8*1024*1024);finish();});session.once('error',()=>finish(new Error('Upstream TLS/HTTP2 connection failed')));
      session.once('close',()=>finish(new Error('Upstream connection closed before it was ready')));
      session.on('error',()=>{});session.on('goaway',()=>{if(this.session===session)this.session=null;session.close();});
    }).finally(()=>{this.connecting=null;});
    return this.connecting;
  }
  async open(authority){
    destination(authority);
    const token=await this.credentials();const session=await this.upstreamSession();
    return new Promise((resolve,reject)=>{
      let ready=false,settled=false;
      const stream=session.request({':method':'CONNECT',':authority':authority,'proxy-authorization':'Bearer '+token},{endStream:false});
      this.streams.add(stream);stream.on('close',()=>this.streams.delete(stream));
      const fail=error=>{if(settled)return;settled=true;clearTimeout(timer);stream.close();reject(error);};
      const timer=setTimeout(()=>fail(new Error('Upstream tunnel timed out')),20000);
      stream.once('response',headers=>{if(settled)return;if(headers[':status']!==200){fail(Object.assign(new Error('Proxy refused the tunnel ('+headers[':status']+')'),{status:Number(headers[':status'])}));return;}clearTimeout(timer);ready=settled=true;resolve(stream);});
      stream.on('error',()=>{if(!ready)fail(new Error('Upstream stream failed'));});
      stream.once('close',()=>{if(!ready)fail(new Error('Upstream closed the tunnel before accepting it'));});
    });
  }
  async listen(){
    if(this.server)return this.address;
    const expected='Basic '+Buffer.from(this.username+':'+this.password).toString('base64');
    this.server=http.createServer((req,res)=>{res.writeHead(405);res.end();});
    this.server.on('connection',socket=>{this.sockets.add(socket);socket.on('error',()=>{});socket.on('close',()=>this.sockets.delete(socket));});
    this.server.on('connect',async(req,socket,head)=>{
      if(!same(String(req.headers['proxy-authorization']||''),expected)){socket.end('HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic realm="III85III"\r\nContent-Length: 0\r\n\r\n');return;}
      let stream;
      try{stream=await this.open(req.url);}catch(error){socket.end('HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\n\r\n');this.emit('warning',error.message);return;}
      if(socket.destroyed){stream.close();return;}
      socket.write('HTTP/1.1 200 Connection Established\r\n\r\n');
      if(head.length){this.sent+=head.length;stream.write(head);}
      socket.on('data',b=>{this.sent+=b.length;});
      socket.pipe(stream);stream.pipe(socket);
      stream.on('data',b=>{this.received+=b.length;});
      socket.on('close',()=>stream.close());stream.on('close',()=>socket.destroy());stream.on('error',()=>socket.destroy());
    });
    this.server.on('clientError',(_,socket)=>socket.destroy());
    await new Promise((resolve,reject)=>{this.server.once('error',reject);this.server.listen(0,'127.0.0.1',resolve);});
    this.address={host:'127.0.0.1',port:this.server.address().port,username:this.username,password:this.password};
    return this.address;
  }
  async close(){for(const s of this.sockets)s.destroy();for(const s of this.streams)s.close();for(const session of this.sessions)session.destroy();this.session=null;if(this.server){const server=this.server;this.server=null;await new Promise(resolve=>server.close(resolve));}}
}
module.exports={TunnelGateway,destination,same};
