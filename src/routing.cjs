'use strict';
const path=require('node:path');
function names(values){
  if(!Array.isArray(values))throw new TypeError('Selected apps must be an array.');
  const result=[];const seen=new Set();
  for(const value of values){
    if(typeof value!=='string')throw new TypeError('Invalid application name.');
    const name=path.win32.basename(value.trim()).toLowerCase();
    if(!name || !name.endsWith('.exe') || /[\x00-\x1f*?\[\]\/\\]/.test(name))throw new TypeError('Choose an executable file name.');
    if(!seen.has(name)){seen.add(name);result.push(name);}
  }
  if(result.length>100)throw new TypeError('Select at most 100 applications.');
  return result.sort();
}
function routerConfig(selected,gateway,selfPaths=[]){
  const apps=names(selected);
  if(!apps.length)throw new Error('Select at least one app before connecting.');
  if(gateway.host!=='127.0.0.1' || !Number.isInteger(gateway.port) || gateway.port<1 || gateway.port>65535 || !gateway.username || !gateway.password)throw new Error('Invalid private local gateway.');
  return {
    log:{level:'warn',timestamp:true},
    dns:{servers:[{type:'https',tag:'resolver',server:'1.1.1.1',server_port:443,path:'/dns-query'}],final:'resolver',strategy:'ipv4_only'},
    inbounds:[{type:'tun',tag:'selected-apps',interface_name:'III85III',address:['172.31.85.1/30','fd85:85:85::1/126'],mtu:1500,auto_route:true,strict_route:true,stack:'system'}],
    outbounds:[{type:'http',tag:'vpn',server:'127.0.0.1',server_port:gateway.port,username:gateway.username,password:gateway.password},{type:'direct',tag:'direct'}],
    route:{auto_detect_interface:true,default_domain_resolver:'resolver',rules:[
      {port:53,action:'hijack-dns'},
      ...(selfPaths.length?[{process_path:selfPaths,action:'route',outbound:'direct'}]:[]),
      {process_path_regex:apps.map(app=>'(?i)[/\\\\]'+app.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')+'$'),network:'udp',action:'reject'},
      {process_path_regex:apps.map(app=>'(?i)[/\\\\]'+app.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')+'$'),network:'tcp',action:'route',outbound:'vpn'}
    ],final:'direct'}
  };
}
// Pure classifier for regression tests and UI explanations. The router
// configuration is checked by the actual neutral router before use.
function decision(selected,processName,network){
  const set=new Set(names(selected));const name=path.win32.basename(processName||'').toLowerCase();
  if(!set.has(name))return 'direct';return network==='udp'?'reject':'vpn';
}
module.exports={names,routerConfig,decision};
