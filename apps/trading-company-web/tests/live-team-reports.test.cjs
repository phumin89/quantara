const test=require('node:test'), assert=require('node:assert/strict'), path=require('node:path');
const {readMeetings,readLiveMeetings,LiveMeetingView}=require('./load-component.cjs')(path.join(__dirname,'../app/LiveTeamReports.tsx'));
const {readResearch}=require('./load-component.cjs')(path.join(__dirname,'../app/CoinResearchView.tsx'));
const React=require('react'),{renderToStaticMarkup}=require('react-dom/server');
const scope={mode:1,accountId:'1'.repeat(32)};
const role=(name)=>({role:name,summary:name==='portfolio-final'?'เก็บเงินสดไว้รอข้อมูลครบ':'เหตุผลจากข้อมูลที่ตรวจแล้ว',providerModel:'fixture',inputTokens:2,outputTokens:2,artifactId:'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',evidenceHash:'a'.repeat(64),dataCutoff:'2026-10-05'});
const fixture=()=>({accountContext:scope,meetings:[{businessDate:'2026-10-06',attemptId:'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',status:'HOLD',accountContext:scope,summary:{modelCalls:8,inputTokens:16,outputTokens:16,reports:[role('portfolio-final'),role('risk-critic'),role('portfolio-initial')]}}]});
test('meeting parser accepts legacy and same-day review slots, sorts latest hour first, and selects by attempt id',()=>{
 const x=fixture(); x.meetings.push({...x.meetings[0],reviewHour:14,attemptId:'cccccccc-cccc-4ccc-8ccc-cccccccccccc'}); x.meetings.push({...x.meetings[0],businessDate:'2026-10-05',reviewHour:null,attemptId:'dddddddd-dddd-4ddd-8ddd-dddddddddddd'});
 const parsed=readMeetings(x); assert.deepEqual(parsed.meetings.map(m=>m.attemptId),['cccccccc-cccc-4ccc-8ccc-cccccccccccc','bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb','dddddddd-dddd-4ddd-8ddd-dddddddddddd']);
 const html=renderToStaticMarkup(React.createElement(LiveMeetingView,{meetings:parsed.meetings,selected:'cccccccc-cccc-4ccc-8ccc-cccccccccccc',onSelect(){}})); assert.ok(html.includes('ทบทวน 14:00'));
 for (const hour of [-1,24,1.5]) { const bad=fixture(); bad.meetings[0].reviewHour=hour; assert.equal(readMeetings(bad),undefined); }
 const duplicate=fixture(); duplicate.meetings.push({...duplicate.meetings[0],reviewHour:null,attemptId:'cccccccc-cccc-4ccc-8ccc-cccccccccccc'}); assert.equal(readMeetings(duplicate),undefined);
});
test('meeting parser rejects malformed summaries, duplicate roles and cross-account reports',()=>{
 for(const mutate of [x=>x.meetings[0].summary.reports.push(role('risk-critic')),x=>x.meetings[0].summary.reports[0].summary={},x=>x.meetings[0].accountContext={...scope,accountId:'2'.repeat(32)},x=>x.meetings[0].summary.reports=null,x=>x.meetings[0].businessDate='2026-02-30',x=>x.meetings[0].attemptId='',x=>x.meetings.push({...x.meetings[0]})]){const x=fixture();mutate(x);assert.equal(readMeetings(x),undefined);}
 assert.equal(readMeetings(fixture()).meetings.length,1);
});
test('saved view leads with final conclusion, has dates and readable optional role history',()=>{
 const m=readMeetings(fixture()).meetings;const html=renderToStaticMarkup(React.createElement(LiveMeetingView,{meetings:m,selected:'',onSelect(){}}));
 assert.ok(html.includes('คงพอร์ต'));assert.ok(html.includes('<select'));assert.ok(html.indexOf('เก็บเงินสด')<html.indexOf('อ่านเหตุผลจากทีม'));assert.ok(html.includes('ข้อเสนอแรกก่อนตรวจความเสี่ยง'));assert.ok(!html.includes('risk-critic'));
 m[0].status='ORDER_PENDING';assert.ok(renderToStaticMarkup(React.createElement(LiveMeetingView,{meetings:m,selected:'',onSelect(){}})).includes('ยังรอยืนยันผลจากตลาด'));
});
test('report reads require fixed HTTPS and never confuse unauthorized with an empty report',async()=>{
 const calls=[];const fetcher=async(url,options)=>{calls.push({url,options});return Response.json(fixture());};
 assert.equal(await readLiveMeetings('http://localhost:3000',fetcher),undefined);assert.equal(calls.length,0);
 assert.ok(await readLiveMeetings('https://localhost:3000',fetcher));assert.equal(calls[0].url,'https://localhost:7443/live/meetings');assert.equal(calls[0].options.credentials,'include');
 assert.equal(await readLiveMeetings('https://localhost:3000',async()=>new Response('',{status:401})),undefined);
});
test('research is optional, strict, and rendered after the conclusion',()=>{
 const now='2026-10-07T00:00:00Z'; const brief={observedAt:'2026-10-06T02:00:00Z',changeKey:'a'.repeat(64),watchlist:[{symbol:'BTC-THB',bid:100,ask:101,quoteVolume24h:1000,change24hPercent:1,spreadPercent:1,depthQuoteNotional:500,executionAllowed:false,held:false}],sources:[{id:'doc-'+'b'.repeat(64),symbol:'BTC-THB',title:'Bitcoin project',url:'https://bitcoin.org/',contentHash:'b'.repeat(64),retrievedAt:'2026-10-06T01:00:00Z'}],findings:[{symbol:'BTC-THB',summary:'ตรวจหลักฐานโครงการ',claims:[{statement:'โครงการเผยแพร่ข้อมูลสาธารณะที่ตรวจสอบได้',assessment:'PROJECT_CLAIM',sourceId:'doc-'+'b'.repeat(64),quote:'ข้อมูลสาธารณะจากเอกสารโครงการที่เผยแพร่'}]}],gaps:['ยังไม่มีข้อมูลสภาพคล่อง']};
 const x=fixture(); x.meetings[0].businessDate='2026-10-06'; x.meetings[0].summary.research=brief; const parsed=readMeetings(x,Date.parse(now)); assert.ok(parsed); const html=renderToStaticMarkup(React.createElement(LiveMeetingView,{meetings:parsed.meetings,selected:'',onSelect(){}})); assert.ok(html.indexOf('เก็บเงินสด')<html.indexOf('เหรียญและหลักฐานที่ทีมตรวจ')); assert.ok(html.includes('ข้อมูลจากโครงการ')); assert.ok(html.includes('bitcoin.org'));
 for(const mutate of [v=>v.sources[0].url='https://evil.example/x',v=>v.findings[0].claims[0].sourceId='doc-'+'c'.repeat(64),v=>v.watchlist[0].symbol='btc-thb',v=>v.gaps=Array(13).fill('gap')]) { const bad=structuredClone(brief); mutate(bad); assert.equal(readResearch(bad),undefined); }
 assert.equal(readResearch(null),undefined); assert.deepEqual(readResearch(brief,Date.parse(now)),brief);
});
test('research keeps historical saved reports and accepts negative changes',()=>{
 const old={observedAt:'2026-09-10T02:00:00Z',changeKey:'a'.repeat(64),watchlist:[{symbol:'BTC-THB',bid:100,ask:101,quoteVolume24h:1000,change24hPercent:-2,spreadPercent:1,depthQuoteNotional:500,executionAllowed:false,held:false}],sources:[{id:'doc-'+'b'.repeat(64),symbol:'BTC-THB',title:'Bitcoin project',url:'https://bitcoin.org/',contentHash:'b'.repeat(64),retrievedAt:'2026-09-09T02:00:00Z'}],findings:[{symbol:'BTC-THB',summary:'หลักฐานย้อนหลัง',claims:[{statement:'เอกสารอ้างอิงโครงการ',assessment:'PROJECT_CLAIM',sourceId:'doc-'+'b'.repeat(64),quote:'ข้อความอ้างอิงย้อนหลังจากเอกสารโครงการ'}]}],gaps:[]};
 assert.ok(readResearch(old,Date.parse('2026-10-07T00:00:00Z'))); const x=fixture(); x.meetings[0].businessDate='2026-09-10'; x.meetings[0].summary.research=old; x.meetings[0].summary.reports.forEach(r=>r.dataCutoff='2026-09-09'); const parsed=readMeetings(x,Date.parse('2026-10-07T00:00:00Z')); assert.ok(parsed); const html=renderToStaticMarkup(React.createElement(LiveMeetingView,{meetings:parsed.meetings,selected:'',onSelect(){}})); assert.ok(html.includes('หลักฐานย้อนหลัง'));
});
test('research provenance and meeting date are enforced at feed boundary',()=>{
 const base={observedAt:'2026-10-06T02:00:00Z',changeKey:'a'.repeat(64),watchlist:[],sources:[{id:'doc-'+'b'.repeat(64),symbol:'BTC-THB',title:'Bitcoin project',url:'https://bitcoin.org/',contentHash:'b'.repeat(64),retrievedAt:'2026-10-06T01:00:00Z'}],findings:[],gaps:[]};
 for(const mutate of [v=>{v.sources[0].retrievedAt='2026-10-06T03:00:00Z';return v;},v=>{v.sources[0].retrievedAt='2026-10-03T01:00:00Z';return v;}]) { const candidate=mutate(structuredClone(base)); assert.ok(readResearch(base,Date.parse('2026-10-07T00:00:00Z'))); assert.equal(readResearch(candidate,Date.parse('2026-10-07T00:00:00Z')),undefined); }
 const x=fixture(); x.meetings[0].businessDate='2026-10-07'; x.meetings[0].summary.research=base; assert.equal(readMeetings(x),undefined); const malformed=fixture(); malformed.meetings[0].summary.research={...base,watchlist:[{symbol:'btc-thb'}]}; assert.equal(readMeetings(malformed),undefined);
});
