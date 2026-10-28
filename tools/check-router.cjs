'use strict';
const fs=require('node:fs');const path=require('node:path');const {spawnSync}=require('node:child_process');
const {routerConfig}=require('../src/routing.cjs');
const root=path.resolve(__dirname,'..');const vendor=path.join(root,'vendor');
const release=fs.readdirSync(vendor).find(n=>n.startsWith('sing-box-v')&&fs.statSync(path.join(vendor,n)).isDirectory());
if(!release)throw new Error('Verified routing dependency is not downloaded.');
const unpack=path.join(vendor,release);const folder=fs.readdirSync(unpack).find(n=>fs.statSync(path.join(unpack,n)).isDirectory());
const exe=path.join(unpack,folder,'sing-box.exe');
const out=path.join(root,'test-results');fs.mkdirSync(out,{recursive:true});const configPath=path.join(out,'router-check.json');
fs.writeFileSync(configPath,JSON.stringify(routerConfig(['firefox.exe','telegram.exe'],{host:'127.0.0.1',port:10880,username:'test-user',password:'not-a-real-secret'}),null,2));
const result=spawnSync(exe,['check','-c',configPath],{encoding:'utf8',windowsHide:true,timeout:15000});
if(result.status!==0){process.stderr.write(result.stderr||result.error?.message||'Router validation failed');process.exit(1);}
console.log('Actual routing engine accepts the include-only configuration.');
