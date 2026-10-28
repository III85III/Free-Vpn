'use strict';
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),os=require('node:os'),path=require('node:path');
const {defaults,patch,read,write}=require('../src/preferences.cjs');
const {Recovery}=require('../src/recovery.cjs');
const {Locations,parse}=require('../src/locations.cjs');
const {Control}=require('../src/control.cjs');
const {parseUsage}=require('../src/mozilla.cjs');
test('settings preserve selected apps, deduplicate favorites and persist language/theme',()=>{
  const folder=fs.mkdtempSync(path.join(os.tmpdir(),'iii85iii-prefs-'));
  try{let settings=defaults();settings.apps=['firefox.exe'];settings=patch(settings,{language:'en',theme:'light',favorites:['GB','GB','DE'],autoReconnect:false});write(path.join(folder,'settings.json'),settings);const restored=read(path.join(folder,'settings.json'));assert.deepEqual(restored.apps,['firefox.exe']);assert.deepEqual(restored.favorites,['GB','DE']);assert.equal(restored.language,'en');assert.equal(restored.theme,'light');assert.equal(restored.autoReconnect,false);assert.throws(()=>patch(settings,{selected:'https://evil.example'}));assert.throws(()=>patch(settings,{autoReconnect:'true'}));}finally{fs.rmSync(folder,{recursive:true,force:true});}
});
test('recovery backs off, caps retries and stops for quota/account failures',()=>{
  const retry=new Recovery();retry.fail(new Error(),1000);assert.equal(retry.next,3000);assert.equal(retry.due(2999),false);retry.fail(new Error(),3000);assert.equal(retry.next,7000);for(let i=0;i<20;i++)retry.fail(new Error(),10000);assert.equal(retry.next,70000);
  for(const error of [{code:'quota_exceeded'},{code:'login_required'},{status:403},{status:429},{status:451}]){retry.reset();assert.equal(retry.fail(error),false);assert.equal(retry.due(),false);}
});
test('catalog admits only official endpoints and does not execute browser filter expressions',()=>{
  const row=(code,hostname,extra={})=>({code,name:code,cities:[{servers:[{hostname,port:2499}]}],...extra});
  const result=parse({data:[row('DE','fra1.m1.fastly-masque.net'),row('GB','evil.example'),row('US','us1.m1.fastly-masque.net',{filter_expression:'throw new Error()'}),row('CA','ca1.m1.fastly-masque.net',{locked:true})]});
  assert.deepEqual(result.map(x=>x.code),['REC','DE','CA']);assert.equal(result.find(x=>x.code==='CA').locked,true);
});
test('ping ordering puts unknown measurements last and survives catalog failures',async()=>{
  const catalog=new Locations({request:async()=>{throw new Error('offline');},measure:async row=>row.code==='REC'?50:10});
  catalog.rows.push({code:'DE',name:'Germany',host:'de.m1.fastly-masque.net',port:2499,ping:null});await catalog.ping();assert.equal(catalog.view(defaults()).locations[0].code,'DE');await catalog.load(true);assert.ok(catalog.error);assert.equal(catalog.get('REC').port,2499);
});
function controller(){const data=fs.mkdtempSync(path.join(os.tmpdir(),'iii85iii-control-'));const app=new Control('C:\\test',data);app.routerState=()=>({state:'running',interface_up:true});app.gateway={close:async()=>{}};app.desired=true;app.phase='active';app.lastProbe=0;app.lastUsage=Date.now();app.connectDeadline=Date.now()+90000;app.settings.selected='DE';app.persist=()=>{};return {app,data};}
test('transient loss retries on the existing adapter and records successful last location',async()=>{
  const {app,data}=controller();try{app.probe=async()=>{throw new Error('offline');};await app.monitor();assert.equal(app.phase,'recovering');assert.equal(app.verified,false);assert.ok(app.recovery.next>Date.now());app.recovery.next=0;app.probe=async()=>{};await app.monitor();assert.equal(app.phase,'active');assert.equal(app.verified,true);assert.equal(app.settings.recent[0],'DE');}finally{fs.rmSync(data,{recursive:true,force:true});}
});
test('manual disconnect intent suppresses retry; account denials never auto-retry',async()=>{
  const {app,data}=controller();try{let probes=0;app.desired=false;app.probe=async()=>{probes++;};await app.monitor();assert.equal(probes,0);app.desired=true;app.probe=async()=>{throw {code:'quota_exceeded',message:'quota'};};await app.monitor();assert.equal(app.desired,false);assert.equal(app.phase,'error');assert.equal(app.recovery.stopped,true);}finally{fs.rmSync(data,{recursive:true,force:true});}
});
test('an interrupted in-flight health probe cannot restore connected state after disconnect',async()=>{
  const {app,data}=controller();try{let finish;app.probe=()=>new Promise(resolve=>finish=resolve);const pending=app.monitor();app.generation++;app.desired=false;app.verified=false;app.phase='off';finish();await pending;assert.equal(app.phase,'off');assert.equal(app.verified,false);}finally{fs.rmSync(data,{recursive:true,force:true});}
});
test('quota values and reset are service-reported, malformed counters stay unknown',()=>{
  assert.equal(parseUsage(new Headers({'x-quota-limit':'50','x-quota-remaining':'51'})),null);
  assert.equal(parseUsage(new Headers({'x-quota-limit':'50','x-quota-remaining':'-1'})),null);
  const usage=parseUsage(new Headers({'x-quota-limit':'50000000000','x-quota-remaining':'12000000','x-quota-reset':'2026-11-01T00:00:00Z'}));assert.equal(usage.reset,'2026-11-01T00:00:00Z');assert.equal(usage.remaining,12000000);
});
