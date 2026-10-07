const test = require('node:test'), assert = require('node:assert/strict'), path = require('node:path');
const React = require('react'), {renderToStaticMarkup} = require('react-dom/server');
const {LiveTeamReports} = require('./load-component.cjs')(path.join(__dirname, '../app/LiveTeamReports.tsx'));
const scope = {mode:1, accountId:'1'.repeat(32)};
const meeting = day => ({businessDate:day, attemptId:day === '2026-10-06' ? 'a'.repeat(8)+'-aaaa-4aaa-8aaa-'+ 'a'.repeat(12) : 'b'.repeat(8)+'-bbbb-4bbb-8bbb-'+'b'.repeat(12), status:'HOLD', accountContext:scope, summary:null});

test('history preserves selection, avoids empty-day queries, and cancels stale replies without a render loop', async t => {
  // Exercise the real component's state/effects with controlled timers and HTTP replies.
  // No DOM package, wall-clock wait, owner session or actual network is needed.
  const state=[], refs=[], effects=[], jobs=new Map(), calls=[], replies=[];
  let s=0, r=0, e=0, dirty=true, tree, renders=0, enabled=true, id=0;
  t.mock.method(React, 'useState', initial => {
    const i=s++; if (!(i in state)) state[i]=initial;
    return [state[i], value => { const next=typeof value === 'function' ? value(state[i]) : value; if (!Object.is(next,state[i])) {state[i]=next; dirty=true;} }];
  });
  t.mock.method(React, 'useRef', initial => refs[r++] ??= {current:initial});
  t.mock.method(React, 'useEffect', (run,deps) => {
    const i=e++, old=effects[i];
    if (!old || deps.some((d,j)=>!Object.is(d,old.deps[j]))) { old?.cleanup?.(); effects[i]={run,deps,pending:true}; }
  });
  t.mock.method(global, 'setTimeout', (run,delay) => {jobs.set(++id,{run,delay}); return id;});
  t.mock.method(global, 'clearTimeout', i => jobs.delete(i));
  const previousWindow=global.window;
  global.window={location:{origin:'https://localhost:3000'}};
  t.after(()=>{ for (const effect of effects) effect.cleanup?.(); global.window=previousWindow; });
  t.mock.method(global, 'fetch', url => {
    calls.push(url); return new Promise(resolve=>replies.push(()=>resolve(Response.json({accountContext:scope,
      availableDates:['2026-10-06','2026-10-04'], selectedDate:url.includes('?date=') ? url.split('=')[1] : null,
      meetings:[meeting(url.includes('2026-10-04') ? '2026-10-04' : '2026-10-06')]}))));
  });
  async function flush() {
    for (let pass=0; pass<8; pass++) {
      if (dirty) {
        dirty=false; s=r=e=0; assert.ok(++renders<25,'component must not loop'); tree=LiveTeamReports({enabled});
        for (const effect of effects) if (effect.pending) {effect.pending=false; effect.cleanup=effect.run();}
      }
      await new Promise(setImmediate);
    }
  }
  function view(node=tree) {
    if (node?.type?.name==='LiveMeetingView') return node;
    for (const child of React.Children.toArray(node?.props?.children)) {const found=view(child); if (found) return found;}
  }
  function poll() {const entry=[...jobs].find(([,job])=>job.delay===30000); assert.ok(entry); jobs.delete(entry[0]); entry[1].run();}
  await flush(); assert.equal(calls.length,1);
  replies.shift()(); await flush(); assert.equal(calls.length,1,'initial date must not issue a duplicate fetch');
  assert.equal(view().props.selectedDate,'2026-10-06');
  view().props.onDateSelect('2026-10-05'); await flush();
  assert.equal(calls.length,1,'known empty date must not be queried');
  assert.ok(renderToStaticMarkup(tree).includes('ยังไม่มีรายงานในวันที่เลือก'));
  poll(); await flush(); assert.ok(!calls.at(-1).includes('?date='),'empty-date poll refreshes only the index/default feed');
  replies.shift()(); await flush(); assert.equal(view().props.selectedDate,'2026-10-05');
  view().props.onDateSelect('2026-10-04'); await flush(); assert.ok(calls.at(-1).endsWith('?date=2026-10-04'));
  replies.shift()(); await flush(); assert.equal(view().props.selectedDate,'2026-10-04');
  poll(); await flush(); // Leave this old day's request in flight, then change selection.
  view().props.onDateSelect('2026-10-06'); await flush();
  replies.shift()(); await flush(); assert.equal(view().props.selectedDate,'2026-10-06'); assert.equal(view().props.pending,true);
  replies.shift()(); await flush(); assert.equal(view().props.pending,false); assert.equal(view().props.meetings[0].businessDate,'2026-10-06');
  enabled=false; dirty=true; await flush(); assert.equal(tree,null); assert.equal(jobs.size,0);
});
