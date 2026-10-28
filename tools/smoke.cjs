'use strict';
const fs=require('node:fs');const path=require('node:path');const os=require('node:os');const crypto=require('node:crypto');const {spawn}=require('node:child_process');const readline=require('node:readline');
const root=path.resolve(__dirname,'..');const app=process.argv[2]?path.resolve(process.argv[2]):path.join(root,'dist','III85III VPN 2.1 Preview');const data=path.join(root,'test-results','empty-account-smoke');
const controller=spawn(path.join(app,'runtime','node.exe'),[path.join(app,'src','control.cjs'),'--data-dir',data],{windowsHide:true,stdio:['pipe','pipe','pipe']});
let stderr='';controller.stderr.on('data',b=>stderr+=b);
async function main(){
  const lines=readline.createInterface({input:controller.stdout});let timer;
  const hello=await Promise.race([new Promise(resolve=>lines.once('line',line=>resolve(JSON.parse(line)))),new Promise((_,reject)=>{timer=setTimeout(()=>reject(new Error('Startup timeout '+stderr.slice(0,120))),12000);})]);clearTimeout(timer);
  const url='http://127.0.0.1:'+hello.port;const headers={'X-III85III-Token':hello.token,'Content-Type':'application/json'};
  const denied=await fetch(url+'/status');if(denied.status!==401)throw new Error('Unauthenticated API read accepted.');
  const status=await(await fetch(url+'/status',{headers})).json();if(status.phase!=='off'||status.signed_in)throw new Error('Test instance must stay disconnected without an account.');
  const response=await fetch(url+'/apps',{headers});const apps=await response.json();if(!Array.isArray(apps.running))throw new Error('App inventory unavailable.');
  let saved=await fetch(url+'/apps',{method:'PUT',headers,body:JSON.stringify({apps:['selected-test.exe']})});if(!saved.ok)throw new Error('Could not save selected apps.');
  const after=await(await fetch(url+'/apps',{headers})).json();if(after.selected.join()!=='selected-test.exe')throw new Error('Selection did not persist.');
  await fetch(url+'/apps',{method:'PUT',headers,body:'{"apps":[]}'});
  const refused=await fetch(url+'/connect',{method:'POST',headers,body:'{}'});if(refused.ok)throw new Error('Connected without an account.');
  const secret='test-only-'+crypto.randomBytes(16).toString('hex');
  async function helper(mode,input){return new Promise((resolve,reject)=>{const p=spawn(path.join(app,'III85III Tools.exe'),[mode],{windowsHide:true,stdio:['pipe','pipe','pipe']});let text='';p.stdout.on('data',b=>text+=b);p.on('exit',code=>code===0?resolve(text):reject(new Error('DPAPI helper failed.')));p.stdin.end(input);});}
  const ciphertext=await helper('protect',secret);if(ciphertext.includes(secret))throw new Error('Credential was not protected.');if(await helper('unprotect',ciphertext)!==secret)throw new Error('DPAPI roundtrip failed.');
  const report={passed:true,api_requires_private_token:true,starts_disconnected:true,account_empty:true,selected_apps_persist:true,connect_requires_account:true,dpapi_roundtrip:true,network_tunnel_started:false};
  fs.writeFileSync(path.join(root,'test-results','smoke.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
  await fetch(url+'/exit',{method:'POST',headers,body:'{}'});controller.stdin.end();lines.close();
  await Promise.race([new Promise(resolve=>controller.once('exit',resolve)),new Promise((_,reject)=>{const wait=setTimeout(()=>reject(new Error('Child did not exit')),10000);controller.once('exit',()=>clearTimeout(wait));})]);
}
main().catch(error=>{controller.stdin.end();console.error(error.message);process.exitCode=1;});
