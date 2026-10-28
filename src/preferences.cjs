'use strict';
const fs=require('node:fs');const path=require('node:path');
const defaults=()=>({apps:[],selected:'REC',favorites:[],recent:[],autoReconnect:true,language:'en',theme:'dark',closeToTray:true});
function code(value){if(typeof value!=='string'||!/^([A-Z]{2}|REC)$/.test(value))throw new Error('Invalid location code.');return value;}
function patch(current,input){
  if(!input||typeof input!=='object'||Array.isArray(input))throw new Error('Invalid preferences.');
  const next={...current};
  for(const key of ['autoReconnect','closeToTray'])if(key in input){if(typeof input[key]!=='boolean')throw new Error('Invalid '+key);next[key]=input[key];}
  if('language' in input){if(!['fa','en'].includes(input.language))throw new Error('Invalid language.');next.language=input.language;}
  if('theme' in input){if(!['light','dark'].includes(input.theme))throw new Error('Invalid theme.');next.theme=input.theme;}
  if('selected' in input)next.selected=code(input.selected);
  if('favorites' in input){if(!Array.isArray(input.favorites)||input.favorites.length>50)throw new Error('Invalid favorites.');next.favorites=[...new Set(input.favorites.map(code))];}
  return next;
}
function read(file){try{const raw=JSON.parse(fs.readFileSync(file,'utf8'));const result=patch(defaults(),raw);result.apps=Array.isArray(raw.apps)?raw.apps:[];result.recent=Array.isArray(raw.recent)?raw.recent.filter(x=>typeof x==='string'&&/^([A-Z]{2}|REC)$/.test(x)).slice(0,8):[];return result;}catch{return defaults();}}
function write(file,value){fs.mkdirSync(path.dirname(file),{recursive:true});fs.writeFileSync(file+'.tmp',JSON.stringify(value,null,2));fs.renameSync(file+'.tmp',file);}
module.exports={defaults,patch,read,write,code};
