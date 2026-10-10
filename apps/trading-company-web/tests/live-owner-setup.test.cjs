const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { readOwnerSetup, fullSpotMandate, ownerMutation, pairOwner } = require('./load-component.cjs')(path.join(__dirname, '../app/ownerSetupClient.ts'));

test('remembered control pairing sends explicit consent once and never arms trading', async () => {
  const calls=[];
  const fetcher=async(url,options)=>{calls.push([url,options]);return url.endsWith('/owner/csrf')?Response.json({token:'csrf'}):new Response(null,{status:204});};
  assert.equal((await pairOwner('https://localhost:3000','A'.repeat(64),true,true,fetcher)).ok,true);
  assert.deepEqual(calls.map(c=>c[0]),['https://localhost:7443/owner/csrf','https://localhost:7443/owner/pair']);
  assert.deepEqual(JSON.parse(calls[1][1].body),{code:'A'.repeat(64),rememberBrowser:true,rememberControl:true});
  assert.equal(calls[1][1].headers['X-CSRF-TOKEN'],'csrf'); assert.equal(calls[1][1].credentials,'include');
  calls.length=0;
  for (const [origin,view,control] of [['http://localhost:3000',true,true],['https://localhost:3000',false,true]])
    assert.equal((await pairOwner(origin,'A'.repeat(64),view,control,fetcher)).ok,false);
  assert.equal(calls.length,0);
  const failing=async(url)=>{calls.push(url);if(url.endsWith('/owner/csrf'))return Response.json({token:'csrf'});throw Error('ambiguous');};
  assert.equal((await pairOwner('https://localhost:3000','A'.repeat(64),true,true,failing)).ok,false); assert.equal(calls.length,2);
});

test('pairing form exposes persistent control as an explicit unchecked option',()=>{
  const React=require('react'),{renderToStaticMarkup}=require('react-dom/server');
  const {OwnerPairingForm}=require('./load-component.cjs')(path.join(__dirname,'../app/OwnerPairingForm.tsx'));
  const render=props=>renderToStaticMarkup(React.createElement(OwnerPairingForm,{code:'',busy:false,rememberBrowser:true,rememberControl:false,onCodeChange(){},onRememberBrowserChange(){},onRememberControlChange(){},onPair(){},...props}));
  const html=render({}); const inputs=[...html.matchAll(/<input[^>]*type="checkbox"[^>]*>/g)].map(m=>m[0]);
  assert.equal(inputs.length,2); assert.ok(inputs[0].includes('checked')); assert.ok(!inputs[1].includes('checked'));
  assert.match(html,/จำสิทธิ์ควบคุมบนเครื่องนี้ 30 วัน/); assert.match(html,/Pair Owner Browser.cmd/); assert.match(html,/type="password"/);
  assert.match(html,/การจับคู่ไม่เปิดการเทรด/);
  const noView=render({rememberBrowser:false,rememberControl:true}); const last=[...noView.matchAll(/<input[^>]*type="checkbox"[^>]*>/g)].at(-1)[0];
  assert.ok(last.includes('disabled')); assert.ok(!last.includes('checked'));
});

test('a viewing-only owner can reverify in trading settings without automatically enabling trading',async()=>{
  const fs=require('node:fs'),ts=require('typescript'),React=require('react');
  const state=[],effects=[];let cursor=0;
  const hooks={...React,useState(initial){const i=cursor++;if(!(i in state))state[i]=initial;return[state[i],value=>{state[i]=typeof value==='function'?value(state[i]):value;}];},
    useRef(initial){const i=cursor++;state[i]??={current:initial};return state[i];},useEffect(effect){if(!effects.length)effects.push(effect);}};
  const file=path.join(__dirname,'../app/LiveOwnerSetup.tsx'),module={exports:{}};
  const compiled=ts.transpileModule(fs.readFileSync(file,'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,jsx:ts.JsxEmit.ReactJSX}}).outputText;
  new Function('require','module','exports',compiled)(name=>name==='react'?hooks:name.startsWith('.')?
    require('./load-component.cjs')(path.join(path.dirname(file),name)+(name==='./OwnerPairingForm'?'.tsx':'.ts')):require(name),module,module.exports);
  const render=()=>{cursor=0;return module.exports.LiveOwnerSetup({});};
  const find=(element,predicate)=>{if(!element||typeof element!=='object')return; if(predicate(element))return element;
    for(const child of [element.props?.children].flat(Infinity)){const match=find(child,predicate);if(match)return match;}};
  const fixture={...base(),canControl:false,provisioned:true,baselineEstablished:true,reconciled:true,snapshotVersion:1,mandateVersion:1,
    observedAt:'2026-10-10T02:00:00Z',mandate:{...fullSpotMandate(base()),version:1}};
  const savedFetch=global.fetch,savedWindow=global.window,calls=[];let paired=false;
  global.window={location:{origin:'https://localhost:3000'}};
  global.fetch=async(url,options)=>{calls.push([url,options]);if(url.endsWith('/live/setup'))return Response.json({...fixture,canControl:paired});
    if(url.endsWith('/owner/csrf'))return Response.json({token:'csrf'});if(url.endsWith('/owner/pair')){paired=true;return new Response(null,{status:204});}
    throw Error('unexpected financial request');};
  try {
    render(); const cleanup=effects[0](); await new Promise(setImmediate);
    const pairing=find(render(),e=>e.type?.name==='OwnerPairingForm'); assert.ok(pairing); assert.equal(pairing.props.busy,false);
    pairing.props.onCodeChange('A'.repeat(64));pairing.props.onRememberControlChange(true);
    find(render(),e=>e.type?.name==='OwnerPairingForm').props.onPair();await new Promise(setImmediate);
    const tree=render();assert.equal(find(tree,e=>e.type?.name==='OwnerPairingForm'),undefined);
    const arm=find(tree,e=>e.type==='button'&&e.props.children==='เปิดสิทธิ์ซื้อขายเงินจริง');assert.ok(arm.props.disabled);
    assert.deepEqual(calls.map(c=>new URL(c[0]).pathname),['/live/setup','/owner/csrf','/owner/pair','/live/setup']);
    assert.equal(JSON.parse(calls[2][1].body).rememberControl,true);cleanup();
  } finally {global.fetch=savedFetch;global.window=savedWindow;}
});

const accountId = { value: '11111111-1111-4111-8111-111111111111' };
const base = () => ({ configured: true, canControl: true, reasons: [], accountId, brokerAccountReference: 'bound', verifiedSymbols: ['BTC-THB', 'ETH-THB'], availableCapital: 12500, fullSpotCapitalAllowed: true, minimumFeeReserveRate: .001, provisioned: false, mandate: null, snapshotVersion: null, mandateVersion: null, baselineEstablished: false, needsDailyBaseline: false, armed: false, stopped: false, reconciled: false, observedAt: null });

test('malformed setup metadata refuses consent and missing or zero capital cannot build policy', () => {
  const invalid = base(); invalid.accountId = '11111111-1111-4111-8111-111111111111';
  assert.equal(readOwnerSetup(invalid), undefined);
  const missing = base(); delete missing.availableCapital;
  assert.equal(readOwnerSetup(missing), undefined);
  const zero = base(); zero.availableCapital = 0;
  assert.ok(readOwnerSetup(zero));
  assert.equal(fullSpotMandate(missing), undefined);
  assert.equal(fullSpotMandate(zero), undefined);
});

test('full spot policy uses actual capital, object account id, verified symbols, and daily rollover', () => {
  const policy = fullSpotMandate(base());
  assert.deepEqual(policy, { accountId, expectedVersion: 0, useFullPortfolio: true, capitalAllocation: 12500, maximumOrderNotional: 12500, maximumDailyTurnover: 12500, maximumDailyLoss: 12500, maximumDrawdown: 1, maximumPortfolioExposure: 1, maximumOpenPositions: 2, maximumSlippage: .02, feeReserveRate: .001, allowedSymbols: ['BTC-THB', 'ETH-THB'], expiresAt: null, automaticDailyRollover: true });
});

test('setup and existing mandate accept canonical verified THB spot markets up to the defensive limit', () => {
  for (const verifiedSymbols of [
    ['BTC-THB', 'ETH-THB', 'DOGE-THB', 'XRP-THB', '1-THB', `${'A'.repeat(15)}-THB`],
    Array.from({ length: 1000 }, (_, i) => `C${i}-THB`),
  ]) {
    const setup = { ...base(), verifiedSymbols };
    assert.deepEqual(readOwnerSetup(setup), setup);
    const policy = fullSpotMandate(setup);
    assert.deepEqual(policy.allowedSymbols, verifiedSymbols);
    assert.equal(policy.maximumOpenPositions, verifiedSymbols.length);
    const provisioned = { ...setup, provisioned: true, snapshotVersion: 1, mandateVersion: 1,
      observedAt: '2026-10-09T00:00:00Z', mandate: { ...policy, version: 1 } };
    assert.deepEqual(readOwnerSetup(provisioned), provisioned);
  }
});
test('verified and allowed symbols reject malformed, duplicate, null and oversized input', () => {
  const invalidSymbols = [null, 1, '', '-THB', 'THB-THB', 'DOGE-USDT', 'doge-THB', 'DOGE-thb',
    'DÖGE-THB', 'DOGE_THB', 'DOGE-THB?x=1', 'DOGE-THB/..', '../DOGE-THB', `${'A'.repeat(16)}-THB`,
    ' DOGE-THB', 'DOGE-THB ', 'DOGE-THB\n', 'DOGE-THB\r', 'DOGE-THB\u2028'];
  const invalidLists = [null, {}, [], ['DOGE-THB', 'DOGE-THB'],
    Array.from({ length: 1001 }, (_, i) => `C${i}-THB`), ...invalidSymbols.map(symbol => [symbol])];
  for (const verifiedSymbols of invalidLists) {
    const setup = { ...base(), verifiedSymbols };
    assert.equal(readOwnerSetup(setup), undefined);
    assert.equal(fullSpotMandate(setup), undefined);
    const provisioned = { ...base(), provisioned: true, snapshotVersion: 1, mandateVersion: 1,
      observedAt: '2026-10-09T00:00:00Z', mandate: { ...fullSpotMandate(base()), version: 1, allowedSymbols: verifiedSymbols } };
    assert.equal(readOwnerSetup(provisioned), undefined);
  }
});

test('mutations use fixed HTTPS origin, CSRF, allowlist, and one POST without retry', async () => {
  const calls = [];
  const fetcher = async (url, options) => { calls.push([url, options]); if (url.endsWith('/owner/csrf')) return Response.json({ token: 'csrf' }); throw new Error('ambiguous'); };
  assert.equal((await ownerMutation('http://localhost:3000', '/live/provision', {} , fetcher)).status, 0);
  assert.equal(calls.length, 0);
  await assert.rejects(() => ownerMutation('https://localhost:3000', '/live/provision', { accountId }, fetcher));
  assert.equal(calls.length, 2);
  assert.equal(calls[1][0], 'https://localhost:7443/live/provision');
  assert.equal(calls[1][1].method, 'POST');
  assert.equal(calls[1][1].headers['X-CSRF-TOKEN'], 'csrf');
});
test('capital changes and a new Stop require renewed displayed owner consent',()=>{
  const {sameOwnerConsent}=require('./load-component.cjs')(path.join(__dirname,'../app/ownerSetupClient.ts'));
  const current=base();
  assert.equal(sameOwnerConsent(current,{...current,snapshotVersion:5}),true);
  for(const changed of [{availableCapital:13000},{stopped:true},{mandateVersion:2},{brokerAccountReference:'other'},{minimumFeeReserveRate:.003},{verifiedSymbols:['BTC-THB']}])assert.equal(sameOwnerConsent(current,{...current,...changed}),false);
});

test('full portfolio mandates tolerate equity ticks while legacy mandates do not',()=>{
  const {sameOwnerConsent}=require('./load-component.cjs')(path.join(__dirname,'../app/ownerSetupClient.ts'));
  const full={...base(),provisioned:true,mandate:{...fullSpotMandate(base()),version:1,useFullPortfolio:true},mandateVersion:1};
  assert.equal(sameOwnerConsent(full,{...full,availableCapital:999}),true);
  assert.equal(sameOwnerConsent(full,{...full,brokerAccountReference:'other'}),false);
  assert.equal(sameOwnerConsent(full,{...full,mandate:{...full.mandate,useFullPortfolio:false}}),false);
  assert.equal(sameOwnerConsent({...full,mandate:{...full.mandate,useFullPortfolio:false}},full),false);
  assert.equal(sameOwnerConsent(base(),{...base(),availableCapital:999}),false);
  assert.equal(sameOwnerConsent({...base(),availableCapital:12500},{...base(),mandate:{...fullSpotMandate(base()),version:1},mandateVersion:1}),false);
  assert.equal(readOwnerSetup({...full,mandate:{...full.mandate,useFullPortfolio:'yes'}}),undefined);
});

test('legacy setup grants full portfolio with one mandate mutation and no automatic trading actions',async()=>{
  const fs=require('node:fs'),ts=require('typescript'),React=require('react');
  const state=[],effects=[];let cursor=0;
  const hooks={...React,useState(initial){const i=cursor++;if(!(i in state))state[i]=initial;return[state[i],value=>{state[i]=typeof value==='function'?value(state[i]):value;}]},useRef(initial){const i=cursor++;state[i]??={current:initial};return state[i]},useEffect(effect){if(!effects.length)effects.push(effect)}};
  const file=path.join(__dirname,'../app/LiveOwnerSetup.tsx'),module={exports:{}};
  const compiled=ts.transpileModule(fs.readFileSync(file,'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,jsx:ts.JsxEmit.ReactJSX}}).outputText;
  new Function('require','module','exports',compiled)(name=>name==='react'?hooks:name.startsWith('.')?require('./load-component.cjs')(path.join(path.dirname(file),name)+(name==='./OwnerPairingForm'?'.tsx':'.ts')):require(name),module,module.exports);
  const render=()=>{cursor=0;return module.exports.LiveOwnerSetup({})};
  const find=(element,predicate)=>{if(!element||typeof element!=='object')return;if(predicate(element))return element;for(const child of [element.props?.children].flat(Infinity)){const match=find(child,predicate);if(match)return match}};
  const legacy={...base(),provisioned:true,baselineEstablished:true,reconciled:true,snapshotVersion:1,mandateVersion:1,observedAt:'2026-10-10T02:00:00Z',mandate:{...fullSpotMandate(base()),version:1,useFullPortfolio:false}};
  const calls=[];let current=legacy;const savedFetch=global.fetch,savedWindow=global.window;global.window={location:{origin:'https://localhost:3000'}};
  global.fetch=async(url,options)=>{calls.push([url,options]);if(url.endsWith('/live/setup'))return Response.json(current);if(url.endsWith('/owner/csrf'))return Response.json({token:'csrf'});if(url.endsWith('/live/mandate')){current={...current,mandate:{...fullSpotMandate(current),version:2},mandateVersion:2};return new Response(null,{status:204})}throw Error('unexpected request')};
  try {render();const cleanup=effects[0]();await new Promise(setImmediate);const button=find(render(),e=>e.type==='button'&&e.props.children==='ใช้ 100% ของพอร์ต');assert.ok(button);button.props.onClick();await new Promise(setImmediate);assert.deepEqual(calls.map(c=>new URL(c[0]).pathname),['/live/setup','/live/setup','/owner/csrf','/live/mandate','/live/setup']);assert.equal(JSON.parse(calls[3][1].body).useFullPortfolio,true);assert.equal(calls.some(c=>String(c[0]).endsWith('/live/arm')||String(c[0]).endsWith('/live/reconcile')),false);cleanup()} finally {global.fetch=savedFetch;global.window=savedWindow}
});
