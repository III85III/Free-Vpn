'use strict';
const {test}=require('node:test');const assert=require('node:assert/strict');
const {MozillaClient,authPassword,parseUsage,guardianFailure}=require('../src/mozilla.cjs');
const {routerConfig,decision,names}=require('../src/routing.cjs');
const {destination}=require('../src/gateway.cjs');
test('auth derivation normalizes email and never sends the clear password',async()=>{
  let observed;
  const client=new MozillaClient({request:async(url,options)=>{observed=options;return new Response(JSON.stringify({sessionToken:'ab'.repeat(32),verified:false,mustVerify:true,verificationMethod:'totp-2fa'}),{status:200});}});
  const reply=await client.signIn(' USER@example.COM ','test password');
  assert.equal(reply.step,'totp');const body=JSON.parse(observed.body);assert.equal(body.email,'user@example.com');assert.equal(body.authPW.length,64);assert.equal(observed.body.includes('test password'),false);
  assert.equal(authPassword('user@example.com','test password'),body.authPW);
});
test('official session bearer authenticates email-code verification',async()=>{
  let request;
  const client=new MozillaClient({request:async(url,options)=>{request={url,options};return new Response('{}',{status:200});}});
  client.session={token:'ab'.repeat(32)};client.complete=async()=>({step:'done'});
  assert.equal((await client.verify('123456','email')).step,'done');
  assert.ok(request.url.endsWith('/session/verify_code'));assert.equal(request.options.headers.Authorization,'Bearer fxs_'+'ab'.repeat(32));
});

test('account refusals identify the failing stage and safe Mozilla errno without reflecting service data',async()=>{
  for(const errno of [103,107,120,125,169]){
    const client=new MozillaClient({request:async()=>new Response(JSON.stringify({errno,message:'private@example.com secret-token',email:'private@example.com'}),{status:400})});
    await assert.rejects(client.signIn('user@example.com','test password'),e=>e.message.startsWith('Sign-in: ')&&e.message.includes('[Mozilla '+errno+']')&&!e.message.includes('private@example.com')&&!e.message.includes('secret-token'));
  }
});
test('quota and entitlement failures are enforced',async()=>{
  for(const status of [401,403,429,451]){
    const client=new MozillaClient({request:async()=>new Response('{}',{status})});
    await assert.rejects(client.json(client.guardianBase,'/api/v1/fpn/token'),e=>e.code===guardianFailure(status));
  }
});
test('usage supports unlimited and missing headers without inventing a quota',()=>{
  assert.deepEqual(parseUsage(new Headers({'X-Quota-Unlimited':'true'})),{unlimited:true});
  assert.equal(parseUsage(new Headers()),null);
  assert.equal(parseUsage(new Headers({'X-Quota-Limit':'50000000000','X-Quota-Remaining':'1234'})).remaining,1234);
});
test('selected apps use VPN while all other and newly started apps are direct',()=>{
  assert.equal(decision(['firefox.exe'],'C:\\Program Files\\Mozilla Firefox\\FIREFOX.EXE','tcp'),'vpn');
  assert.equal(decision(['firefox.exe'],'new-app.exe','tcp'),'direct');
  assert.equal(decision(['firefox.exe'],'new-app.exe','udp'),'direct');
  assert.equal(decision(['firefox.exe'],'firefox.exe','udp'),'reject');
});
test('router has explicit selected TCP, blocked selected UDP, and direct final policy',()=>{
  const cfg=routerConfig(['FIREFOX.EXE','firefox.exe'],{host:'127.0.0.1',port:10880,username:'test',password:'test'});
  assert.equal(cfg.route.final,'direct');assert.equal(cfg.outbounds.find(o=>o.tag==='vpn').server,'127.0.0.1');
  assert.equal(cfg.route.rules.find(r=>r.network==='tcp').process_path_regex.length,1);
  assert.ok(cfg.route.rules.find(r=>r.network==='tcp').process_path_regex[0].includes('firefox\\.exe$'));
  assert.equal(cfg.inbounds[0].address.length,2);
  assert.equal(cfg.route.rules.find(r=>r.network==='udp').action,'reject');
});
test('empty app selection cannot accidentally enable whole-system VPN',()=>{
  assert.throws(()=>routerConfig([],{host:'127.0.0.1',port:1234,username:'x',password:'x'}));
  assert.throws(()=>names(['*.exe']));
});
test('gateway rejects malformed authorities and unsupported web ports',()=>{
  assert.equal(destination('example.com:443').port,443);assert.throws(()=>destination('user@example.com:443'));assert.throws(()=>destination('example.com:22'));assert.throws(()=>destination('example.com:443\r\nX: x'));
});

function proxyFixture(exp,nbf,date){
  const jwt='header.'+Buffer.from(JSON.stringify({exp,nbf})).toString('base64url')+'.signature';
  return new Response(JSON.stringify({token:jwt}),{status:200,headers:{Date:date,'X-Quota-Limit':'50000000000','X-Quota-Remaining':'1000000'}});
}
test('already expired proxy passes are rejected using the service clock',async()=>{
  const serviceNow=Date.parse('2026-10-28T00:00:00Z')/1000;
  const client=new MozillaClient({request:async()=>proxyFixture(serviceNow-1,serviceNow-600,new Date(serviceNow*1000).toUTCString())});
  client.accessToken=async()=>'test-access';
  await assert.rejects(client.proxyPass(),e=>e.code==='invalid_response');
  assert.equal(client.pass,null);
});
test('a nearly expired pass is refreshed instead of cached for its original lifetime',async()=>{
  const serviceNow=Date.parse('2026-10-28T00:00:00Z')/1000;let requests=0;
  const client=new MozillaClient({request:async()=>{requests++;return proxyFixture(serviceNow+30,serviceNow-570,new Date(serviceNow*1000).toUTCString());}});
  client.accessToken=async()=>'test-access';
  await client.proxyPass();await client.proxyPass();
  assert.equal(requests,2);
});
