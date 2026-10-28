'use strict';
const fs=require('node:fs');const path=require('node:path');const crypto=require('node:crypto');const http=require('node:http');const tls=require('node:tls');
const {spawn}=require('node:child_process');const {MozillaClient}=require('./mozilla.cjs');const {TunnelGateway,same}=require('./gateway.cjs');const {names,routerConfig}=require('./routing.cjs');
const preferences=require('./preferences.cjs');const {Locations}=require('./locations.cjs');const {Recovery}=require('./recovery.cjs');
const {Accounts}=require('./accounts.cjs');
const {compat}=require('./compat.cjs');

async function command(exe,args,input=''){
  return new Promise((resolve,reject)=>{
    const p=spawn(exe,args,{windowsHide:true,stdio:['pipe','pipe','pipe']});let output='',error='';
    p.stdout.on('data',b=>{output+=b;if(output.length>1048576)p.kill();});p.stderr.on('data',b=>{error+=b;if(error.length>10000)error=error.slice(-10000);});
    p.on('error',reject);p.on('exit',code=>code===0?resolve(output):reject(new Error('Local helper failed ('+code+'). '+error.slice(0,200))));p.stdin.on('error',()=>{});p.stdin.end(input);
  });
}
class Control {
  constructor(root,data){
    this.root=root;this.data=data;this.tools=path.join(root,'III85III Tools.exe');this.moz=new MozillaClient();
    this.token=crypto.randomBytes(32).toString('hex');this.phase='off';this.message='Choose apps and sign in.';this.apps=[];this.gateway=null;this.server=null;this.verified=false;this.stopping=false;
    this.accountFile=path.join(data,'account.dpapi');this.settingsFile=path.join(data,'settings.json');this.configFile=path.join(data,'route.json');this.stateFile=path.join(data,'route-state.json');this.stopFile=path.join(data,'route-stop');
    this.settings=preferences.defaults();this.catalog=new Locations({cacheFile:path.join(data,'locations-cache.json')});this.recovery=new Recovery();
    this.desired=false;this.generation=0;this.monitorBusy=false;this.mutating=false;this.lastProbe=0;this.lastUsage=0;this.usageUpdated=null;this.connectDeadline=0;
    this.accounts=new Accounts(path.join(data,'accounts.dpapi'),{protect:input=>command(this.tools,['protect'],input),unprotect:input=>command(this.tools,['unprotect'],input)});
  }
  async initialize(){
    await command(this.tools,['private-dir',this.data]);
    this.settings=preferences.read(this.settingsFile);this.apps=names(this.settings.apps);
    try{await this.accounts.load(this.accountFile);if(this.accounts.current()){this.moz.restore(this.accounts.current());this.message='Account restored. Choose apps and connect.';}}
    catch{this.message='Saved accounts could not be opened. Sign in again.';}
  }
  persist(){this.settings.apps=this.apps;preferences.write(this.settingsFile,this.settings);}
  async saveAccount(){if(!this.moz.session?.verified)return;await this.accounts.add(this.moz.session);await this.accounts.usage(this.moz.usage);this.usageUpdated=new Date().toISOString();}
  async identity(action){const token=this.moz.session?.token,verified=this.moz.session?.verified;try{const reply=await action();if(reply.step==='done')await this.saveAccount();return reply;}catch(error){if(this.moz.session?.verified&&(!verified||this.moz.session.token!==token))await this.saveAccount();throw error;}}
  routerState(){try{return JSON.parse(fs.readFileSync(this.stateFile,'utf8'));}catch{return {state:'off',interface_up:false};}}
  status(){
    const route=this.routerState();let phase=this.phase;
    if(phase==='starting'&&route.interface_up&&this.verified)phase='active';
    if(['starting','active'].includes(phase)&&route.state==='failed')phase='error';
    return {phase,message:route.state==='failed'?'The selected-app router failed. Connect again to approve Windows routing.':this.message,email:this.moz.session?.email||'',signed_in:!!this.moz.session?.verified,usage:this.moz.usage,usage_updated:this.usageUpdated,apps:this.apps,router:route,verified:this.verified&&route.interface_up===true,preferences:this.settings,selected:this.settings.selected,recovery:{enabled:this.settings.autoReconnect,attempt:this.recovery.attempt,next_retry:this.recovery.next||null,stopped:this.recovery.stopped},traffic:{sent:this.gateway?.sent||0,received:this.gateway?.received||0},version:'2.1.0-preview'};
  }
  async probe(){
    const stream=await this.gateway.open('www.mozilla.org:443');
    await new Promise((resolve,reject)=>{
      const socket=tls.connect({socket:stream,servername:'www.mozilla.org',ALPNProtocols:['http/1.1']});let received='';let done=false;
      const finish=error=>{if(done)return;done=true;socket.destroy();error?reject(error):resolve();};
      socket.setTimeout(15000,()=>finish(new Error('Connection test timed out.')));socket.on('error',()=>finish(new Error('Remote TLS verification failed.')));
      socket.on('secureConnect',()=>socket.write('HEAD / HTTP/1.1\r\nHost: www.mozilla.org\r\nConnection: close\r\n\r\n'));
      socket.on('data',b=>{received+=b.toString('latin1');if(received.includes('\r\n')){if(/^HTTP\/1\.[01] [234]\d\d /.test(received))finish();else finish(new Error('Remote website did not accept the request.'));}});
      socket.on('close',()=>{if(!done)finish(new Error('Connection closed before verification.'));});
    });
  }
  async prepare(){
    if(!this.moz.session?.verified)throw new Error('Sign in with your Mozilla account first.');
    if(!this.apps.length)throw new Error('Select at least one app to use VPN.');
    await this.disconnect();this.phase='preparing';this.message='Checking Mozilla access…';
    try{
      await this.catalog.load();const location=this.catalog.get(this.settings.selected);
      await this.moz.proxyPass();this.usageUpdated=new Date().toISOString();this.lastUsage=Date.now();
      this.gateway=new TunnelGateway({credentials:()=>this.moz.proxyPass(),upstream:'https://'+location.host+':'+location.port});
      this.gateway.on('warning',()=>{this.message='A connection was refused by the upstream service.';});
      const endpoint=await this.gateway.listen();await this.probe();this.verified=true;this.lastProbe=Date.now();
      fs.writeFileSync(this.configFile,JSON.stringify(routerConfig(this.apps,endpoint,[path.join(this.root,'runtime','node.exe'),path.join(this.root,'runtime','sing-box.exe'),path.join(this.root,'III85III VPN.exe'),path.join(this.root,'III85III Route Host.exe')]),null,2));
      for(const file of [this.stopFile,this.stateFile])if(fs.existsSync(file))fs.unlinkSync(file);
      const router=path.join(this.root,'runtime','sing-box.exe');
      await command(router,['check','-c',this.configFile]);
      this.desired=true;this.recovery.reset();this.connectDeadline=Date.now()+90000;
      this.phase='starting';this.message='Approve the Windows routing prompt. Only selected apps use VPN.';
      return {elevation_required:true,config:this.configFile,state:this.stateFile,stop:this.stopFile,parent:process.pid};
    }catch(error){await this.disconnect();this.phase='error';this.message=error.message;throw error;}
  }
  async disconnect(){
    this.desired=false;this.generation++;this.recovery.reset();
    fs.writeFileSync(this.stopFile,'stop');
    const deadline=Date.now()+6000;
    while(Date.now()<deadline){const route=this.routerState();if(['off','stopped','failed'].includes(route.state))break;await new Promise(resolve=>setTimeout(resolve,150));}
    if(this.gateway){const gateway=this.gateway;this.gateway=null;await gateway.close();}
    this.verified=false;this.phase='off';this.message='Disconnected. Applications use their ordinary connection.';
    if(fs.existsSync(this.configFile))fs.unlinkSync(this.configFile);
  }
  async monitor(){
    if(this.monitorBusy||this.mutating||this.stopping||!this.desired||!this.gateway)return;
    this.monitorBusy=true;const generation=this.generation;
    try{
      const route=this.routerState();
      if(['failed','stopped'].includes(route.state)||(!route.interface_up&&Date.now()>this.connectDeadline)){
        this.desired=false;this.verified=false;this.phase='error';this.message='Windows routing stopped. Connect again to approve routing.';return;
      }
      if(!route.interface_up)return;
      if(this.settings.recent[0]!==this.settings.selected){this.settings.recent=[this.settings.selected,...this.settings.recent.filter(x=>x!==this.settings.selected)].slice(0,8);this.persist();}
      const recovering=['recovering','error'].includes(this.phase);
      if(recovering&&(!this.settings.autoReconnect||!this.recovery.due()))return;
      if(!recovering&&Date.now()-this.lastProbe<20000)return;
      try{
        await this.probe();if(generation!==this.generation||!this.desired)return;
        this.verified=true;this.phase='active';this.lastProbe=Date.now();this.recovery.reset();this.message='Selected apps are connected. Other apps stay direct.';
        if(Date.now()-this.lastUsage>300000){try{await this.moz.refreshUsage();this.usageUpdated=new Date().toISOString();this.lastUsage=Date.now();}catch(error){if(['quota_exceeded','login_required','not_entitled'].includes(error.code))throw error;}}
      }catch(error){
        if(generation!==this.generation||!this.desired)return;
        this.verified=false;const retry=this.recovery.fail(error);this.phase=retry&&this.settings.autoReconnect?'recovering':'error';
        this.message=retry?(this.settings.autoReconnect?'Connection interrupted. Retrying automatically.':'Connection interrupted. Automatic reconnect is disabled.'):error.message;
        if(!retry)this.desired=false;
      }
    }finally{this.monitorBusy=false;}
  }
  async body(req){
    if(!String(req.headers['content-type']||'').startsWith('application/json'))throw Object.assign(new Error('JSON is required.'),{status:415});
    let text='';for await(const chunk of req){text+=chunk;if(Buffer.byteLength(text)>32768)throw Object.assign(new Error('Request is too large.'),{status:413});}
    try{return JSON.parse(text||'{}');}catch{throw Object.assign(new Error('Invalid JSON.'),{status:400});}
  }
  async dispatch(req){
    if(req.url.startsWith('/api/'))return compat(this,req);
    const key=req.method+' '+req.url;
    switch(key){
      case 'GET /status':return this.status();
      case 'GET /preferences':return this.settings;
      case 'GET /accounts':return this.accounts.list();
      case 'POST /accounts/select':{const body=await this.body(req);if(this.desired||this.gateway)throw new Error('Disconnect before changing accounts.');this.moz.forget();this.moz.restore(await this.accounts.select(body.id));this.usageUpdated=null;return this.accounts.list();}
      case 'PUT /preferences':{
        const body=await this.body(req);
        if('selected' in body){if(this.desired||this.gateway)throw new Error('Disconnect before changing location.');await this.catalog.load();this.catalog.get(body.selected);}
        this.settings=preferences.patch(this.settings,body);this.persist();return this.settings;
      }
      case 'GET /locations':await this.catalog.load();return this.catalog.view(this.settings);
      case 'POST /locations/ping':await this.body(req);await this.catalog.load();await this.catalog.ping();return this.catalog.view(this.settings);
      case 'GET /apps':return {selected:this.apps,running:JSON.parse(await command(this.tools,['processes']))};
      case 'PUT /apps':{
        const body=await this.body(req);const selected=names(body.apps);
        if(this.desired||this.gateway)throw new Error('Disconnect before changing the selected apps.');
        this.apps=selected;this.persist();return {saved:true,apps:this.apps};
      }
      case 'POST /signin':{const body=await this.body(req);if(this.desired||this.gateway)throw new Error('Disconnect before adding an account.');return this.identity(()=>this.moz.signIn(body.email,body.password));}
      case 'POST /verify':{const body=await this.body(req);return this.identity(()=>this.moz.verify(body.code,body.kind));}
      case 'POST /connect':await this.body(req);return this.prepare();
      case 'POST /disconnect':await this.body(req);await this.disconnect();return {ok:true};
      case 'POST /usage':await this.body(req);await this.moz.refreshUsage();this.lastUsage=Date.now();this.usageUpdated=new Date().toISOString();return {usage:this.moz.usage,updated:this.usageUpdated};
      case 'POST /logout':await this.body(req);await this.disconnect();await this.accounts.removeActive();this.moz.forget();this.usageUpdated=null;if(fs.existsSync(this.accountFile))fs.unlinkSync(this.accountFile);return {ok:true};
      case 'POST /exit':await this.body(req);setImmediate(()=>this.shutdown());return {ok:true};
      default:throw Object.assign(new Error('Unknown local API route.'),{status:404});
    }
  }
  async listen(){
    this.server=http.createServer(async(req,res)=>{
      res.setHeader('Content-Type','application/json');res.setHeader('Cache-Control','no-store');res.setHeader('X-Content-Type-Options','nosniff');
      const token=String(req.headers['x-iii85iii-token']||'');
      if(!same(token,this.token)){res.writeHead(401);res.end('{"error":"Local authentication required."}');return;}
      if(req.headers.origin&&req.headers.origin!=='http://'+req.headers.host){res.writeHead(403);res.end('{"error":"Cross-origin request refused."}');return;}
      const mutation=req.method!=='GET';
      if(mutation&&(this.mutating||this.monitorBusy)){res.writeHead(409);res.end('{"error":"A connection operation is in progress. Try again shortly."}');return;}
      if(mutation)this.mutating=true;
      try{res.end(JSON.stringify(await this.dispatch(req)));}catch(error){res.writeHead(error.status>=400&&error.status<600?error.status:400);res.end(JSON.stringify({error:error.message,code:error.code||'local'}));}finally{if(mutation)this.mutating=false;}
    });
    await new Promise((resolve,reject)=>{this.server.once('error',reject);this.server.listen(0,'127.0.0.1',resolve);});
    this.monitorTimer=setInterval(()=>this.monitor().catch(()=>{}),2000);this.monitorTimer.unref();
    return {port:this.server.address().port,token:this.token,pid:process.pid};
  }
  async shutdown(){if(this.stopping)return;this.stopping=true;clearInterval(this.monitorTimer);try{await this.disconnect();}finally{this.server?.close();process.exit(0);}}
}
if(require.main===module){
  const root=path.resolve(__dirname,'..');const i=process.argv.indexOf('--data-dir');const data=i>=0?path.resolve(process.argv[i+1]):path.join(process.env.LOCALAPPDATA,'III85III VPN Independent');
  const app=new Control(root,data);
  app.initialize().then(()=>app.listen()).then(info=>process.stdout.write(JSON.stringify(info)+'\n')).catch(()=>{process.stderr.write('III85III startup failed.\n');process.exit(1);});
  process.stdin.resume();process.stdin.on('end',()=>app.shutdown());process.on('SIGINT',()=>app.shutdown());process.on('SIGTERM',()=>app.shutdown());
}
module.exports={Control,command};
