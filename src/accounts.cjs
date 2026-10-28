'use strict';
const fs=require('node:fs');const path=require('node:path');const crypto=require('node:crypto');
class Accounts {
  constructor(file,{protect,unprotect}){this.file=file;this.protect=protect;this.unprotect=unprotect;this.records=[];this.active=null;}
  async load(legacy=null){
    if(fs.existsSync(this.file)){
      const value=JSON.parse(await this.unprotect(fs.readFileSync(this.file,'utf8')));
      if(!Array.isArray(value.records)||value.records.length>25)throw new Error('Invalid local account vault.');
      this.records=value.records.filter(row=>/^[a-f0-9]{24}$/.test(row.id||'')&&/^[a-f0-9]{64}$/i.test(row.session?.token||'')&&row.session?.verified===true);this.active=value.active;
    }else if(legacy&&fs.existsSync(legacy)){
      const session=JSON.parse(await this.unprotect(fs.readFileSync(legacy,'utf8')));await this.add(session);
    }
    if(!this.records.some(row=>row.id===this.active))this.active=null;
  }
  async save(){const text=await this.protect(JSON.stringify({records:this.records,active:this.active}));fs.writeFileSync(this.file+'.tmp',text.trim());fs.renameSync(this.file+'.tmp',this.file);}
  async add(session){
    if(!session?.verified||!/^[a-f0-9]{64}$/i.test(session.token||''))throw new Error('Only a verified Mozilla session can be saved.');
    let row=this.records.find(item=>session.uid&&item.session.uid===session.uid||item.session.email?.toLowerCase()===session.email?.toLowerCase());
    if(!row){if(this.records.length>=25)throw new Error('At most 25 local accounts.');row={id:crypto.randomBytes(12).toString('hex'),session};this.records.push(row);}else row.session=session;
    this.active=row.id;await this.save();return row.id;
  }
  current(){return this.records.find(row=>row.id===this.active)?.session||null;}
  async usage(value){const row=this.records.find(item=>item.id===this.active);if(row){row.usage=value;row.updated=new Date().toISOString();await this.save();}}
  list(){return {accounts:this.records.map(row=>({id:row.id,email:row.session.email||'',active:row.id===this.active})),active:this.active};}
  async select(id){if(!this.records.some(row=>row.id===id))throw new Error('Saved account not found.');this.active=id;await this.save();return this.current();}
  async removeActive(){this.records=this.records.filter(row=>row.id!==this.active);this.active=null;await this.save();}
}
module.exports={Accounts};
