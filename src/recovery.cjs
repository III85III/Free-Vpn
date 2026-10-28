'use strict';
const terminal=new Set(['login_required','not_entitled','quota_exceeded','region_unavailable','account','invalid_response']);
class Recovery {
  constructor(){this.attempt=0;this.next=0;this.stopped=false;}
  reset(){this.attempt=0;this.next=0;this.stopped=false;}
  fail(error,now=Date.now()){
    if(terminal.has(error.code)||[401,403,429,451].includes(error.status)){this.stopped=true;this.next=0;return false;}
    this.attempt++;this.next=now+Math.min(60000,2000*Math.pow(2,Math.min(5,this.attempt-1)));return true;
  }
  due(now=Date.now()){return !this.stopped&&now>=this.next;}
}
module.exports={Recovery,terminal};
