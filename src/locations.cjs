'use strict';
const tls=require('node:tls');const fs=require('node:fs');const path=require('node:path');
const URL='https://firefox.settings.services.mozilla.com/v1/buckets/main/collections/vpn-serverlist/records';
const recommended=()=>({code:'REC',name:'Recommended',locked:false,host:'p.m1.fastly-masque.net',port:2499,ping:null});
function parse(data){
  if(!data||!Array.isArray(data.data))throw new Error('Invalid Mozilla location catalog.');
  const locations=new Map([['REC',recommended()]]);
  for(const row of data.data){
    // Conditional browser-only entries are not evaluated or impersonated.
    if(row.deleted||row.filter_expression||typeof row.code!=='string'||!/^([A-Z]{2}|REC)$/.test(row.code))continue;
    for(const city of Array.isArray(row.cities)?row.cities:[])for(const server of Array.isArray(city.servers)?city.servers:[]){
      if(typeof server.hostname!=='string'||!/^([a-z0-9-]+\.)+fastly-masque\.net$/i.test(server.hostname)||server.port!==2499)continue;
      if(!locations.has(row.code)||row.code==='REC')locations.set(row.code,{code:row.code,name:String(row.name||row.code).slice(0,80),locked:row.locked===true,host:server.hostname.toLowerCase(),port:server.port,ping:null});
      break;
    }
  }
  return [...locations.values()];
}
function latency(server){return new Promise(resolve=>{const started=performance.now();const socket=tls.connect({host:server.host,port:server.port,servername:server.host,ALPNProtocols:['h2']});let done=false;const finish=result=>{if(done)return;done=true;socket.destroy();resolve(result);};socket.setTimeout(3500,()=>finish(null));socket.on('error',()=>finish(null));socket.once('secureConnect',()=>finish(socket.authorized&&socket.alpnProtocol==='h2'?Math.max(1,Math.round(performance.now()-started)):null));});}
class Locations {
  constructor({request=fetch,cacheFile=null,measure=latency}={}){this.request=request;this.cacheFile=cacheFile;this.measure=measure;this.rows=[recommended()];this.loaded=0;this.error='';this.pinging=false;}
  async load(force=false){
    if(!force&&this.loaded&&Date.now()-this.loaded<3600000)return this.rows;
    try{const response=await this.request(URL,{redirect:'error',signal:AbortSignal.timeout(15000)});if(!response.ok)throw new Error();const data=await response.json();const rows=parse(data);this.rows=rows;this.loaded=Date.now();this.error='';if(this.cacheFile){fs.mkdirSync(path.dirname(this.cacheFile),{recursive:true});fs.writeFileSync(this.cacheFile,JSON.stringify(data));}}
    catch{this.error='Catalog unavailable; recommended or cached locations are shown.';if(this.cacheFile)try{this.rows=parse(JSON.parse(fs.readFileSync(this.cacheFile,'utf8')));}catch{}}
    return this.rows;
  }
  get(code){const item=this.rows.find(row=>row.code===code);if(!item)throw new Error('This location is not in the current Mozilla catalog.');return item;}
  async ping(){if(this.pinging)return;this.pinging=true;try{let index=0;await Promise.all(Array.from({length:4},async()=>{while(index<this.rows.length){const item=this.rows[index++];item.ping=await this.measure(item);}}));}finally{this.pinging=false;}}
  view(settings){return {locations:[...this.rows].sort((a,b)=>(a.ping??Infinity)-(b.ping??Infinity)||a.name.localeCompare(b.name)),selected:settings.selected,favorites:settings.favorites,recent:settings.recent,pinging:this.pinging,notice:this.error};}
}
module.exports={Locations,parse,latency,recommended};
