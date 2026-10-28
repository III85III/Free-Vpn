'use strict';
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),os=require('node:os'),crypto=require('node:crypto');
const {Accounts}=require('../src/accounts.cjs');const {Control}=require('../src/control.cjs');const {compat}=require('../src/compat.cjs');const {Readable}=require('node:stream');
function folder(){return fs.mkdtempSync(path.join(os.tmpdir(),'iii85iii-account-test-'));}
function remove(dir){const target=path.resolve(dir);if(path.dirname(target)!==path.resolve(os.tmpdir())||!path.basename(target).startsWith('iii85iii-account-test-'))throw new Error('Unsafe cleanup');fs.rmSync(target,{recursive:true,force:true});}
function session(email,byte){return {email,uid:byte,token:byte.repeat(64),verified:true};}
const codec={protect:async text=>Buffer.from(text).toString('base64'),unprotect:async text=>Buffer.from(text,'base64').toString()};
test('multiple profiles persist separately and switching is explicit',async()=>{
  const dir=folder();try{const file=path.join(dir,'accounts.dpapi'),vault=new Accounts(file,codec);const a=await vault.add(session('first@example.invalid','a'));const b=await vault.add(session('second@example.invalid','b'));assert.notEqual(a,b);assert.equal(vault.current().email,'second@example.invalid');assert.equal(fs.readFileSync(file,'utf8').includes('first@example.invalid'),false);
    const reopened=new Accounts(file,codec);await reopened.load();assert.equal(reopened.current().email,'second@example.invalid');await reopened.select(a);assert.equal(reopened.current().email,'first@example.invalid');assert.equal(reopened.list().accounts.length,2);assert.equal(JSON.stringify(reopened.list()).includes('token'),false);await reopened.removeActive();assert.equal(reopened.current(),null);assert.equal(reopened.list().accounts.length,1);
  }finally{remove(dir);}
});
test('re-adding an account updates its session rather than duplicating quota profiles',async()=>{
  const dir=folder();try{const vault=new Accounts(path.join(dir,'accounts.dpapi'),codec);await vault.add(session('person@example.invalid','a'));const updated=session('PERSON@example.invalid','b');updated.uid='a';await vault.add(updated);assert.equal(vault.records.length,1);assert.equal(vault.current().token,'b'.repeat(64));await assert.rejects(vault.add({...updated,verified:false}));}finally{remove(dir);}
});
test('valid login remains saved even when the VPN account has no quota',async()=>{
  const dir=folder();try{const app=new Control('C:\\test',dir);app.accounts=new Accounts(path.join(dir,'accounts.dpapi'),codec);app.moz.session=null;app.moz.usage={remaining:0,limit:50000000000};await assert.rejects(app.identity(async()=>{app.moz.session=session('quota@example.invalid','a');throw Object.assign(new Error('quota'),{code:'quota_exceeded'});}));assert.equal(app.accounts.list().accounts.length,1);assert.equal(app.accounts.records[0].usage.remaining,0);}finally{remove(dir);}
});
test('existing desktop adapter supplies safe disconnected state and MFA step names',async()=>{
  const dir=folder();try{const app=new Control('C:\\test',dir);const req=(method,url,body)=>{const stream=Readable.from([JSON.stringify(body||{})]);stream.method=method;stream.url=url;stream.headers={'content-type':'application/json'};return stream;};
    const status=await compat(app,req('GET','/api/status'));assert.equal(status.state.phase,'off');assert.equal(status.last_test.ok,false);assert.deepEqual(status.active_account,{});
    app.moz.signIn=async()=>({step:'totp',message:'verify'});const state=await compat(app,req('POST','/api/signin',{email:'person@example.invalid',password:'test-only'}));assert.equal(state.step,'need_totp');assert.equal(state.id.length,24);
  }finally{remove(dir);}
});
