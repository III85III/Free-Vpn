'use strict';
// Independent protocol implementation from the official references in PROJECT.md.
const crypto = require('node:crypto');
const AUTH = 'https://api.accounts.firefox.com/v1';
const GUARDIAN = 'https://vpn.mozilla.org';
const CLIENT_ID = '5882386c6d801776'; // Published Mozilla public native-client id.
const SCOPE = 'profile https://identity.mozilla.com/apps/vpn';

class ServiceError extends Error {
  constructor(message, status = 0, code = 'service', errno = 0) {
    super(message); this.status = status; this.code = code; this.errno = errno;
  }
}
function authPassword(email, password) {
  const normalized = String(email).trim().toLowerCase();
  const stretch = crypto.pbkdf2Sync(Buffer.from(password, 'utf8'), Buffer.from('identity.mozilla.com/picl/v1/quickStretch:' + normalized), 1000, 32, 'sha256');
  try { return Buffer.from(crypto.hkdfSync('sha256', stretch, Buffer.alloc(0), Buffer.from('identity.mozilla.com/picl/v1/authPW'), 32)).toString('hex'); }
  finally { stretch.fill(0); }
}
function parseUsage(headers) {
  if (headers.get('x-quota-unlimited') === 'true') return { unlimited: true };
  const limit = Number(headers.get('x-quota-limit'));
  const remaining = Number(headers.get('x-quota-remaining'));
  const reset = headers.get('x-quota-reset');
  if (!/^\d+$/.test(headers.get('x-quota-limit')||'') || !/^\d+$/.test(headers.get('x-quota-remaining')||'') || !Number.isSafeInteger(limit) || !Number.isSafeInteger(remaining) || remaining>limit) return null;
  return { unlimited: false, limit, remaining, reset:Number.isFinite(Date.parse(reset||''))?reset:null };
}
function guardianFailure(status) {
  return ({401:'login_required',403:'not_entitled',429:'quota_exceeded',451:'region_unavailable'})[status] || (status >= 500 ? 'server_error' : 'service_error');
}
class MozillaClient {
  constructor({ request = fetch, authBase = AUTH, guardianBase = GUARDIAN, clientId = CLIENT_ID } = {}) {
    this.request = request; this.authBase = authBase; this.guardianBase = guardianBase; this.clientId = clientId;
    this.session = null; this.oauth = null; this.pass = null; this.usage = null;
  }
  async json(base, path, { method='GET', body, authorization } = {}) {
    const headers = { Accept:'application/json', 'User-Agent':'III85III-VPN/2.0' };
    if (authorization) headers.Authorization = authorization;
    if (body !== undefined || method === 'POST') headers['Content-Type'] = 'application/json';
    let response;
    try { response = await this.request(base + path, { method, headers, redirect:'error', body:body === undefined ? undefined : JSON.stringify(body), signal:AbortSignal.timeout(25000) }); }
    catch { throw new ServiceError('The account service did not respond.', 0, 'network'); }
    let data = {};
    if (method !== 'HEAD') { try { data = await response.json(); } catch { if (response.ok) throw new ServiceError('The service returned an invalid response.',response.status,'invalid_response'); } }
    if (!response.ok) {
      if (base === this.guardianBase) {
        this.usage = parseUsage(response.headers) || this.usage;
        const code = guardianFailure(response.status);
        throw new ServiceError(code.replaceAll('_',' '), response.status, code);
      }
      const errno = Number(data.errno) || 0;
      const safe = ({102:'Account not found.',103:'Incorrect Mozilla password.',125:'Mozilla requires additional sign-in verification.',127:'Verification code rejected.',160:'Two-step verification is required.'})[errno] || 'Mozilla account request was refused.';
      throw new ServiceError(safe,response.status,'account',errno);
    }
    return { data, headers:response.headers };
  }
  sessionAuthorization() {
    if (!this.session?.token) throw new ServiceError('Sign in to your Mozilla account.',401,'login_required');
    return 'Bearer fxs_' + this.session.token.replace(/^fxs_/, '');
  }
  async signIn(email, password) {
    email = String(email).trim().toLowerCase();
    if (!email || !password) throw new ServiceError('Enter your Mozilla account email and password.',400,'input');
    const { data } = await this.json(this.authBase,'/account/login',{ method:'POST',body:{email,authPW:authPassword(email,password),service:'vpn'} });
    if (typeof data.sessionToken !== 'string' || !/^[a-f0-9]{64}$/i.test(data.sessionToken)) throw new ServiceError('Mozilla did not provide a valid session.',502,'invalid_response');
    this.session = { token:data.sessionToken, email, uid:data.uid || '', verified:!!data.verified && !data.mustVerify };
    this.oauth = this.pass = null;
    if (!this.session.verified) return { step:String(data.verificationMethod || '').includes('totp') ? 'totp' : 'email_code', message:'Enter the verification code requested by Mozilla.' };
    return this.complete();
  }
  async verify(code, kind) {
    if (!/^\d{6,12}$/.test(String(code))) throw new ServiceError('Enter the numeric verification code.',400,'input');
    const path = kind === 'totp' ? '/session/verify/totp' : '/session/verify_code';
    await this.json(this.authBase,path,{method:'POST',authorization:this.sessionAuthorization(),body:{code:String(code),service:'vpn'}});
    this.session.verified = true;
    return this.complete();
  }
  async accessToken() {
    if (this.oauth && this.oauth.expires > Date.now() + 60000) return this.oauth.token;
    const { data } = await this.json(this.authBase,'/oauth/token',{method:'POST',authorization:this.sessionAuthorization(),body:{client_id:this.clientId,grant_type:'fxa-credentials',scope:SCOPE,ttl:3600}});
    if (typeof data.access_token !== 'string' || !data.access_token) throw new ServiceError('Mozilla did not provide an access token.',502,'invalid_response');
    this.oauth = {token:data.access_token,expires:Date.now()+Math.min(3600,Number(data.expires_in)||3600)*1000};
    return this.oauth.token;
  }
  async complete() {
    const token = await this.accessToken();
    await this.json(this.guardianBase,'/api/v1/fpn/activate',{method:'POST',authorization:'Bearer '+token});
    await this.proxyPass(true);
    return {step:'done',message:'Mozilla account ready.',email:this.session.email,usage:this.usage};
  }
  async proxyPass(force=false) {
    if (!force && this.pass && this.pass.expires > Date.now()+120000) return this.pass.token;
    const token = await this.accessToken();
    const {data,headers} = await this.json(this.guardianBase,'/api/v1/fpn/token',{authorization:'Bearer '+token});
    this.usage = parseUsage(headers);
    if (!this.usage?.unlimited && this.usage?.remaining <= 0) throw new ServiceError('Monthly quota exhausted.',429,'quota_exceeded');
    if (typeof data.token !== 'string' || data.token.split('.').length !== 3) throw new ServiceError('Invalid proxy credential.',502,'invalid_response');
    // HTTPS authenticates the issuer. These claims are decoded only for cache
    // scheduling; the upstream service remains responsible for validating JWTs.
    let claims;try {claims=JSON.parse(Buffer.from(data.token.split('.')[1],'base64url').toString('utf8'));}catch {throw new ServiceError('Invalid proxy credential.',502,'invalid_response');}
    if (!Number.isFinite(claims.exp) || !Number.isFinite(claims.nbf) || claims.exp<=claims.nbf) throw new ServiceError('Invalid proxy lifetime.',502,'invalid_response');
    const responseTime=Date.parse(headers.get('date')||'');
    const now=Number.isFinite(responseTime)?responseTime:Date.now();
    const remaining=claims.exp*1000-now;
    if(remaining<=0)throw new ServiceError('Mozilla returned an expired proxy credential.',502,'invalid_response');
    this.pass={token:data.token,expires:Date.now()+Math.min(3600000,remaining)};
    return this.pass.token;
  }
  async refreshUsage() {
    const token=await this.accessToken();
    const {headers}=await this.json(this.guardianBase,'/api/v1/fpn/token',{method:'HEAD',authorization:'Bearer '+token});
    this.usage=parseUsage(headers);return this.usage;
  }
  restore(record) {
    if(!record || !/^[a-f0-9]{64}$/i.test(record.token || ''))throw new ServiceError('Saved account is invalid.',400,'input');
    this.session={token:record.token,email:String(record.email || ''),uid:String(record.uid || ''),verified:true};
    this.oauth=this.pass=null;
  }
  forget(){this.session=this.oauth=this.pass=this.usage=null;}
}
module.exports={MozillaClient,ServiceError,authPassword,parseUsage,guardianFailure};
