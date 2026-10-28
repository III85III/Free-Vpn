'use strict';
// Adapter for the owner's existing III85III desktop interface.
const {Readable}=require('node:stream');const crypto=require('node:crypto');
function usage(value){if(!value)return {};return {unlimited:!!value.unlimited,remaining_bytes:value.remaining,limit_bytes:value.limit,reset_at:value.reset};}
async function forward(app,method,url,body={}){const req=Readable.from([JSON.stringify(body)]);req.method=method;req.url=url;req.headers={'content-type':'application/json'};return app.dispatch(req);}
function accountRows(app){return app.accounts.records.map(row=>({id:row.id,email:row.session.email,status:row.id===app.accounts.active?'Active':'Saved',usage:usage(row.id===app.accounts.active?app.moz.usage:row.usage)}));}
function signState(app,reply){app.signReply=reply;return {id:app.signId,step:reply.step==='done'?'done':reply.step==='totp'?'need_totp':'need_email_code',message:reply.message||'',email:reply.email||''};}
async function compat(app,req){
  const url=req.url,key=req.method+' '+url;
  const body=req.method==='GET'?{}:await app.body(req);
  switch(key){
    case 'GET /api/status':{
      const s=app.status(),active=s.phase==='active'&&s.verified,connected=['active','starting','recovering'].includes(s.phase),at=new Date(app.lastProbe||0).toISOString();
      const current=app.accounts.records.find(row=>row.id===app.accounts.active);
      return {version:s.version,independent:true,state:{phase:connected?'running':s.phase==='off'?'off':'error',message:s.message,since:app.startedAt||at},
        proxy:{started_at:app.startedAt||'',wake_error:''},tun:{state:s.router.interface_up?'on':'off',installed:true,programs_supported:true},tun_supported:true,
        active_account:current?{id:current.id,email:current.session.email,usage:usage(s.usage)}:{},last_test:{ok:active,at:at},system_proxy:'tun',selection:app.settings.selected==='REC'?'':app.settings.selected.toLowerCase(),
        traffic:{down_rate:0,up_rate:0,received:s.traffic.received,sent:s.traffic.sent},auto_connecting:s.phase==='starting'||s.phase==='recovering',no_network:false,
        connect_failure:s.phase==='error'?s.message:'',preferences:s.preferences,recovery:s.recovery,usage_updated:s.usage_updated};
    }
    case 'GET /api/accounts':return {accounts:accountRows(app)};
    case 'GET /api/locations':await app.catalog.load();return {locations:app.catalog.view(app.settings).locations.map(row=>({code:row.code==='REC'?'':row.code.toLowerCase(),name:row.name,ping_ms:row.ping||0,locked:row.locked})),preferences:app.settings};
    case 'POST /api/locations/ping':await app.catalog.load();await app.catalog.ping();return {ok:true};
    case 'POST /api/location':{let selected=String(body.country||'REC').toUpperCase();if(selected==='FASTEST'){await app.catalog.load();selected=app.catalog.view(app.settings).locations[0].code;}await forward(app,'PUT','/preferences',{selected});return {ok:true};}
    case 'GET /api/settings':return {config:{proxy:{transport:'h2'},dashboard:{listen:'127.0.0.1:'+app.server.address().port},tun:{programs:app.apps.map(name=>({name,path:'',udp_only:false}))}},preferences:app.settings};
    case 'PUT /api/settings':await forward(app,'PUT','/apps',{apps:(body.config?.tun?.programs||[]).map(row=>row.name)});return {ok:true};
    case 'GET /api/tun/programs':{const result=await forward(app,'GET','/apps');return {programs:result.running};}
    case 'POST /api/system-proxy':if(body.mode!=='tun')throw new Error('The independent build uses selected-app routing.');return {ok:true};
    case 'POST /api/power':if(body.on){const launch=await app.prepare();app.startedAt=new Date(app.lastProbe).toISOString();return launch;}await app.disconnect();return {ok:true};
    case 'POST /api/test':if(!app.gateway)throw new Error('Connect before testing the tunnel.');await app.probe();app.verified=true;app.lastProbe=Date.now();return {ok:app.routerState().interface_up===true,at:new Date(app.lastProbe).toISOString()};
    case 'POST /api/signin':{app.signId=crypto.randomBytes(12).toString('hex');const reply=await forward(app,'POST','/signin',body);return signState(app,reply);}
    case 'POST /api/app/exit':setImmediate(()=>app.shutdown());return {ok:true};
  }
  if(req.method==='GET'&&url.startsWith('/api/logs'))return {entries:[]};
  if(url.startsWith('/api/signin/')&&url.slice(12)===app.signId){
    if(req.method==='DELETE'){app.signReply=null;return {ok:true};}
    if(req.method==='GET')return signState(app,app.signReply||{step:'email_code',message:'Enter the Mozilla verification code.'});
    return signState(app,await forward(app,'POST','/verify',{code:body.value,kind:app.signReply?.step}));
  }
  const match=/^\/api\/accounts\/([a-f0-9]{24})(\/refresh|\/select)?$/.exec(url);
  if(match){
    const id=match[1];
    if(req.method==='DELETE'){if(app.desired||app.gateway)throw new Error('Disconnect before removing an account.');if(id!==app.accounts.active){app.accounts.records=app.accounts.records.filter(row=>row.id!==id);await app.accounts.save();return {ok:true};}return forward(app,'POST','/logout');}
    if(match[2]==='/select'){await forward(app,'POST','/accounts/select',{id});return {ok:true};}
    if(match[2]==='/refresh'){if(id!==app.accounts.active)throw new Error('Select the account before refreshing its balance.');await forward(app,'POST','/usage');await app.accounts.usage(app.moz.usage);return {ok:true};}
  }
  throw new Error('Unsupported local interface route.');
}
module.exports={compat,forward,usage};
