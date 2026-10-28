'use strict';
// Independent protocol implementation from the official references in PROJECT.md.
const crypto = require('node:crypto');
const AUTH = 'https://api.accounts.firefox.com/v1';
const GUARDIAN = 'https://vpn.mozilla.com';
const CLIENT_ID = '5882386c6d801776'; // Published Mozilla public native-client id.
const SCOPE = 'profile https://identity.mozilla.com/apps/vpn';
const POW_ALPHABET = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789';
const FIREFOX_UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:158.0) Gecko/20100101 Firefox/158.0';

class ServiceError extends Error {
  constructor(message, status = 0, code = 'service', errno = 0) {
    super(message); this.status = status; this.code = code; this.errno = errno;
  }
}

async function solveFastly(request = fetch, userAgent = FIREFOX_UA, siteBase = 'https://accounts.firefox.com') {
  try {
    const r1 = await request(siteBase + '/', { headers: { 'User-Agent': userAgent, 'Accept': 'text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8' } });
    const html = await r1.text();
    const mPrefix = html.match(/\/_fs-ch-[A-Za-z0-9]+/);
    if (!mPrefix) return [];
    const prefix = mPrefix[0];
    let cookies = (r1.headers.getSetCookie ? r1.headers.getSetCookie() : [r1.headers.get('set-cookie')]).filter(Boolean);
    const r2 = await request(siteBase + prefix + '/script.js?reload=true', {
      headers: { 'User-Agent': userAgent, 'Cookie': cookies.map(c => c.split(';')[0]).join('; ') }
    });
    const script = await r2.text();
    const mInit = script.match(/init\((\[[^\]]*\]),\s*"([^"]+)",\s*"([^"]+)"/);
    if (!mInit) return cookies;
    let challenges = JSON.parse(mInit[1]);
    let token = mInit[2];
    for (let round = 1; round <= 8; round++) {
      const answers = [];
      for (const ch of challenges) {
        if (ch.ty === 'pow') {
          const { base: powBase, hash: targetHex, hmac, expires } = ch.data;
          let found = null;
          for (let i = 0; i < POW_ALPHABET.length && !found; i++) {
            for (let j = 0; j < POW_ALPHABET.length; j++) {
              const cand = POW_ALPHABET[i] + POW_ALPHABET[j];
              if (crypto.createHash('sha256').update(powBase + cand).digest('hex') === targetHex) { found = cand; break; }
            }
          }
          answers.push({ ty: 'pow', base: powBase, answer: found, hmac, expires });
        } else if (ch.ty === 'pat') {
          answers.push({ ty: 'pat', auth: '' });
        } else if (ch.ty === 'clientmetrics') {
          answers.push({
            ty: 'clientmetrics',
            webdriver: false,
            bot_detection_result: { bot_detected: false, bot_kind: null },
            browser_metrics: { client_data: '{}', error_trace: null },
            detector_results: {},
            v: 2
          });
        }
      }
      const r3 = await request(siteBase + prefix + '/fst-post-back', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Accept': 'application/json', 'User-Agent': userAgent, 'Origin': siteBase, 'Cookie': cookies.map(c => c.split(';')[0]).join('; ') },
        body: JSON.stringify({ token, data: answers })
      });
      const sc3 = (r3.headers.getSetCookie ? r3.headers.getSetCookie() : [r3.headers.get('set-cookie')]).filter(Boolean);
      for (const s of sc3) { cookies = cookies.filter(c => c.split('=')[0] !== s.split('=')[0]); cookies.push(s); }
      const pb = await r3.json();
      if (pb.status === 'success') break;
      challenges = pb.ch; token = pb.tok;
    }
    return cookies;
  } catch {
    return [];
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
    this.cookies = [];
  }
  async json(base, path, { method='GET', body, authorization } = {}) {
    const isAuth = base === this.authBase;
    const ua = isAuth ? FIREFOX_UA : 'III85III-VPN/2.0';
    const headers = { Accept:'application/json', 'User-Agent': ua };
    if (authorization) headers.Authorization = authorization;
    if (body !== undefined || method === 'POST') headers['Content-Type'] = 'application/json';

    const maxAttempts = isAuth ? 3 : 1;
    let response;

    for (let attempt = 1; attempt <= maxAttempts; attempt++) {
      if (isAuth && this.cookies.length > 0) {
        headers.Cookie = this.cookies.map(c => c.split(';')[0].trim()).join('; ');
      }
      try {
        response = await this.request(base + path, {
          method,
          headers,
          redirect: 'error',
          body: body === undefined ? undefined : JSON.stringify(body),
          signal: AbortSignal.timeout(25000)
        });
      } catch {
        throw new ServiceError('The account service did not respond.', 0, 'network');
      }

      if (isAuth && response.headers) {
        const sc = (response.headers.getSetCookie ? response.headers.getSetCookie() : [response.headers.get('set-cookie')]).filter(Boolean);
        for (const s of sc) {
          this.cookies = this.cookies.filter(c => c.split('=')[0] !== s.split('=')[0]);
          this.cookies.push(s);
        }
      }

      if (response.status === 406 && isAuth && attempt < maxAttempts) {
        if (this.cookies.length === 0) {
          const fresh = await solveFastly(this.request, FIREFOX_UA);
          if (fresh && fresh.length > 0) {
            for (const s of fresh) {
              this.cookies = this.cookies.filter(c => c.split('=')[0] !== s.split('=')[0]);
              this.cookies.push(s);
            }
          }
        }
        continue;
      }
      break;
    }

    let data = {};
    if (method !== 'HEAD') {
      try { data = await response.json(); } catch {
        if (response.ok) throw new ServiceError('The service returned an invalid response.', response.status, 'invalid_response');
      }
    }
    if (!response.ok) {
      if (base === this.guardianBase) {
        this.usage = parseUsage(response.headers) || this.usage;
        const code = guardianFailure(response.status);
        throw new ServiceError(code.replaceAll('_',' '), response.status, code);
      }
      const errno = Number(data.errno) || 0;
      const safe = ({
        102: 'Mozilla account not found.',
        103: 'Incorrect Mozilla password. If you sign in with Google or Apple, please set a password on your Mozilla account.',
        104: 'Confirm your Mozilla email first.',
        106: 'The sign-in request format was rejected.',
        107: 'A sign-in request parameter was rejected.',
        108: 'A required sign-in parameter is missing.',
        114: 'Too many attempts. Wait a few minutes before trying again.',
        120: 'Mozilla requires the original letter case of your email address.',
        125: 'Mozilla blocked this sign-in for security reasons. Please check your email for an unblock confirmation.',
        126: 'Mozilla requires an account reset.',
        127: 'Verification code rejected.',
        138: 'This Mozilla session still requires verification.',
        149: 'Mozilla does not currently allow sign-in with this email.',
        160: 'Two-step verification is required.',
        162: 'Mozilla rejected the application client identifier.',
        166: 'Mozilla rejected the client type.',
        169: 'Mozilla requires a browser authorization flow for this client.'
      })[errno] || (response.status === 406 ? 'Mozilla requires a browser security check that this app could not complete. Browser sign-in may work while in-app sign-in remains blocked. This is not a password error.' : 'Mozilla account request was refused.');
      const stage = ({'/account/login':'Sign-in','/oauth/token':'Account authorization','/session/verify_code':'Email verification','/session/verify/totp':'Two-step verification'})[path] || 'Account request';
      throw new ServiceError(stage + ': ' + safe + (errno ? ' [Mozilla ' + errno + ']' : ''), response.status, 'account', errno);
    }
    return { data, headers: response.headers };
  }
  sessionAuthorization() {
    if (!this.session?.token) throw new ServiceError('Sign in to your Mozilla account.', 401, 'login_required');
    return 'Bearer fxs_' + this.session.token.replace(/^fxs_/, '');
  }
  async signIn(email, password) {
    email = String(email).trim();
    if (!email || !password) throw new ServiceError('Enter your Mozilla account email and password.', 400, 'input');
    let loginData;
    try {
      const { data } = await this.json(this.authBase, '/account/login', {
        method: 'POST',
        body: { email: email.toLowerCase(), authPW: authPassword(email, password), service: 'vpn' }
      });
      loginData = data;
    } catch (err) {
      if (err.errno === 120 && err.email && err.email !== email.toLowerCase()) {
        const { data } = await this.json(this.authBase, '/account/login', {
          method: 'POST',
          body: { email: err.email, originalLoginEmail: email, authPW: authPassword(err.email, password), service: 'vpn' }
        });
        loginData = data;
      } else {
        throw err;
      }
    }
    if (typeof loginData.sessionToken !== 'string' || !/^[a-f0-9]{64}$/i.test(loginData.sessionToken)) {
      throw new ServiceError('Mozilla did not provide a valid session.', 502, 'invalid_response');
    }
    this.session = { token: loginData.sessionToken, email, uid: loginData.uid || '', verified: !!loginData.verified && !loginData.mustVerify };
    this.oauth = this.pass = null;
    if (!this.session.verified) {
      return { step: String(loginData.verificationMethod || '').includes('totp') ? 'totp' : 'email_code', message: 'Enter the verification code requested by Mozilla.' };
    }
    return this.complete();
  }
  async verify(code, kind) {
    if (!/^\d{6,12}$/.test(String(code))) throw new ServiceError('Enter the numeric verification code.', 400, 'input');
    const path = kind === 'totp' ? '/session/verify/totp' : '/session/verify_code';
    await this.json(this.authBase, path, { method: 'POST', authorization: this.sessionAuthorization(), body: { code: String(code), service: 'vpn' } });
    this.session.verified = true;
    return this.complete();
  }
  async accessToken() {
    if (this.oauth && this.oauth.expires > Date.now() + 60000) return this.oauth.token;
    const { data } = await this.json(this.authBase, '/oauth/token', { method: 'POST', authorization: this.sessionAuthorization(), body: { client_id: this.clientId, grant_type: 'fxa-credentials', scope: SCOPE, ttl: 3600 } });
    if (typeof data.access_token !== 'string' || !data.access_token) throw new ServiceError('Mozilla did not provide an access token.', 502, 'invalid_response');
    this.oauth = { token: data.access_token, expires: Date.now() + Math.min(3600, Number(data.expires_in) || 3600) * 1000 };
    return this.oauth.token;
  }
  async complete() {
    const token = await this.accessToken();
    await this.json(this.guardianBase, '/api/v1/fpn/activate', { method: 'POST', authorization: 'Bearer ' + token });
    await this.proxyPass(true);
    return { step: 'done', message: 'Mozilla account ready.', email: this.session.email, usage: this.usage };
  }
  async proxyPass(force = false) {
    if (!force && this.pass && this.pass.expires > Date.now() + 120000) return this.pass.token;
    const token = await this.accessToken();
    const { data, headers } = await this.json(this.guardianBase, '/api/v1/fpn/token', { authorization: 'Bearer ' + token });
    this.usage = parseUsage(headers);
    if (!this.usage?.unlimited && this.usage?.remaining <= 0) throw new ServiceError('Monthly quota exhausted.', 429, 'quota_exceeded');
    if (typeof data.token !== 'string' || data.token.split('.').length !== 3) throw new ServiceError('Invalid proxy credential.', 502, 'invalid_response');
    let claims;
    try { claims = JSON.parse(Buffer.from(data.token.split('.')[1], 'base64url').toString('utf8')); }
    catch { throw new ServiceError('Invalid proxy credential.', 502, 'invalid_response'); }
    if (!Number.isFinite(claims.exp) || !Number.isFinite(claims.nbf) || claims.exp <= claims.nbf) throw new ServiceError('Invalid proxy lifetime.', 502, 'invalid_response');
    const responseTime = Date.parse(headers.get('date') || '');
    const now = Number.isFinite(responseTime) ? responseTime : Date.now();
    const remaining = claims.exp * 1000 - now;
    if (remaining <= 0) throw new ServiceError('Mozilla returned an expired proxy credential.', 502, 'invalid_response');
    this.pass = { token: data.token, expires: Date.now() + Math.min(3600000, remaining) };
    return this.pass.token;
  }
  async refreshUsage() {
    const token = await this.accessToken();
    const { headers } = await this.json(this.guardianBase, '/api/v1/fpn/token', { method: 'HEAD', authorization: 'Bearer ' + token });
    this.usage = parseUsage(headers);
    return this.usage;
  }
  restore(record) {
    if (!record || !/^[a-f0-9]{64}$/i.test(record.token || '')) throw new ServiceError('Saved account is invalid.', 400, 'input');
    this.session = { token: record.token, email: String(record.email || ''), uid: String(record.uid || ''), verified: true };
    this.oauth = this.pass = null;
  }
  forget() { this.session = this.oauth = this.pass = this.usage = null; this.cookies = []; }
}

module.exports = { MozillaClient, ServiceError, authPassword, parseUsage, guardianFailure, solveFastly };
