const test=require('node:test'), assert=require('node:assert/strict'), path=require('node:path');
const {readMeetings,readLiveMeetings,LiveMeetingView}=require('./load-component.cjs')(path.join(__dirname,'../app/LiveTeamReports.tsx'));
const {readResearch}=require('./load-component.cjs')(path.join(__dirname,'../app/CoinResearchView.tsx'));
const React=require('react'),{renderToStaticMarkup}=require('react-dom/server');
const scope={mode:1,accountId:'1'.repeat(32)};
const role=(name)=>({role:name,summary:name==='portfolio-final'?'เก็บเงินสดไว้รอข้อมูลครบ':'เหตุผลจากข้อมูลที่ตรวจแล้ว',providerModel:'fixture',inputTokens:2,outputTokens:2,artifactId:'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',evidenceHash:'a'.repeat(64),dataCutoff:'2026-10-05'});
const fixture=()=>({accountContext:scope,meetings:[{businessDate:'2026-10-06',attemptId:'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',status:'HOLD',accountContext:scope,summary:{modelCalls:8,inputTokens:16,outputTokens:16,reports:[role('portfolio-final'),role('risk-critic'),role('portfolio-initial')]}}]});

test('registered plans are reported as waiting, rejected plans explain the actual constraint',()=>{
 const x=fixture(); x.meetings[0].status='PLAN_REGISTERED';
 const html=()=>renderToStaticMarkup(React.createElement(LiveMeetingView,{meetings:readMeetings(x).meetings,selected:'',onSelect(){}}));
 assert.match(html(),/บันทึกแผนแล้ว/); assert.doesNotMatch(html(),/ส่งคำสั่งแล้ว/);
 x.meetings[0].status='PLAN_REJECTED'; x.meetings[0].summary.planRejectionReasons=['PLAN_ROUND_TRIP_TURNOVER_EXCEEDED'];
 assert.match(html(),/วงเงินวันนี้ไม่พอทั้งซื้อและขาย/); assert.doesNotMatch(html(),/PLAN_ROUND_TRIP/);
 for(const reasons of [[],['secret=private'],['STOPPED','STOPPED'],Array(21).fill('STOPPED')]){
  x.meetings[0].summary.planRejectionReasons=reasons; assert.equal(readMeetings(x),undefined);
 }
});
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
 assert.ok(html.includes('คงพอร์ต'));assert.ok(html.includes('รอบประชุม · ประชุมประจำวัน'));assert.ok(html.indexOf('เก็บเงินสด')<html.indexOf('อ่านเหตุผลจากทีม'));assert.ok(html.includes('ข้อเสนอแรกก่อนตรวจความเสี่ยง'));assert.ok(!html.includes('risk-critic'));
 m[0].status='ORDER_PENDING';assert.ok(renderToStaticMarkup(React.createElement(LiveMeetingView,{meetings:m,selected:'',onSelect(){}})).includes('ยังรอยืนยันผลจากตลาด'));
});
test('saved advisory reports validate issues and render one truthful status before the conclusion',()=>{
 const x=fixture(); x.meetings[0].status='READ_ONLY'; x.meetings[0].summary={...x.meetings[0].summary,advisoryOnly:true,issues:[{code:'PUBLIC_RESEARCH_UNAVAILABLE'}]};
 const parsed=readMeetings(x); assert.ok(parsed);
 const html=renderToStaticMarkup(React.createElement(LiveMeetingView,{meetings:parsed.meetings,selected:'',onSelect(){}}));
 assert.ok(html.includes('ข้อมูลวิจัยสาธารณะไม่พร้อม')); assert.ok(html.includes('รอบนี้วิเคราะห์เท่านั้น · ไม่ได้ส่งคำสั่ง'));
 assert.equal((html.match(/ไม่ได้ส่งคำสั่ง/g)||[]).length,1); assert.ok(html.indexOf('ข้อมูลวิจัยสาธารณะไม่พร้อม')<html.indexOf('เก็บเงินสด'));
 delete x.meetings[0].summary.advisoryOnly;
 const legacy=renderToStaticMarkup(React.createElement(LiveMeetingView,{meetings:readMeetings(x).meetings,selected:'',onSelect(){}}));
 assert.ok(legacy.includes('รอบนี้วิเคราะห์เท่านั้น · ไม่ได้ส่งคำสั่ง'));
 for(const issues of [[{code:'UNKNOWN'}],[{code:'MARKET_HISTORY_UNAVAILABLE'}]]) { const bad=fixture(); bad.meetings[0].summary.issues=issues; assert.equal(readMeetings(bad),undefined); }
});
test('saved market history issue stays symbol-specific and historical',()=>{
 const x=fixture(); x.meetings[0].status='READ_ONLY'; x.meetings[0].summary={...x.meetings[0].summary,advisoryOnly:true,issues:[{code:'MARKET_HISTORY_UNAVAILABLE',symbol:'SOL-THB'}]};
 const html=renderToStaticMarkup(React.createElement(LiveMeetingView,{meetings:readMeetings(x).meetings,selected:'',onSelect(){}}));
 assert.ok(html.includes('กราฟประวัติตลาดของรอบนี้ไม่ครบ (SOL-THB)')); assert.ok(html.includes('รอบนี้วิเคราะห์เท่านั้น · ไม่ได้ส่งคำสั่ง')); assert.ok(!html.includes('ตลาดทั้งหมดไม่พร้อม')); assert.ok(!html.includes('ส่งคำสั่งแล้ว'));
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
test('single-letter public markets survive the meeting feed and remain research-only',()=>{
 const research={observedAt:'2026-10-06T02:00:00Z',changeKey:'a'.repeat(64),watchlist:[{symbol:'G-THB',bid:10,ask:11,quoteVolume24h:300000,change24hPercent:-1,spreadPercent:10,depthQuoteNotional:500,executionAllowed:false,held:false}],sources:[],findings:[],gaps:[]};
 const x=fixture(); x.meetings[0].summary.research=research; const parsed=readMeetings(x); assert.ok(parsed);
 const html=renderToStaticMarkup(React.createElement(LiveMeetingView,{meetings:parsed.meetings,selected:'',onSelect(){}})); assert.ok(html.includes('G-THB')); assert.ok(html.includes('วิจัยเท่านั้น'));
 for(const symbol of ['-THB','g-THB','ABCDEFGHIJKLMNOP-THB']) assert.equal(readResearch({...research,watchlist:[{...research.watchlist[0],symbol}]}),undefined);
});
test('research provenance and meeting date are enforced at feed boundary',()=>{
 const base={observedAt:'2026-10-06T02:00:00Z',changeKey:'a'.repeat(64),watchlist:[],sources:[{id:'doc-'+'b'.repeat(64),symbol:'BTC-THB',title:'Bitcoin project',url:'https://bitcoin.org/',contentHash:'b'.repeat(64),retrievedAt:'2026-10-06T01:00:00Z'}],findings:[],gaps:[]};
 for(const mutate of [v=>{v.sources[0].retrievedAt='2026-10-06T03:00:00Z';return v;},v=>{v.sources[0].retrievedAt='2026-10-03T01:00:00Z';return v;}]) { const candidate=mutate(structuredClone(base)); assert.ok(readResearch(base,Date.parse('2026-10-07T00:00:00Z'))); assert.equal(readResearch(candidate,Date.parse('2026-10-07T00:00:00Z')),undefined); }
 const x=fixture(); x.meetings[0].businessDate='2026-10-07'; x.meetings[0].summary.research=base; assert.equal(readMeetings(x),undefined); const malformed=fixture(); malformed.meetings[0].summary.research={...base,watchlist:[{symbol:'btc-thb'}]}; assert.equal(readMeetings(malformed),undefined);
});
test('date filtering validates metadata, encodes the request, and never falls back to another day',async()=>{
 const calls=[]; const requested='2026-10-06'; const response={...fixture(),availableDates:['2026-10-06','2026-10-05'],selectedDate:requested};
 const fetcher=async(url)=>{calls.push(url);return Response.json(response);}; const parsed=await readLiveMeetings('https://localhost:3000',fetcher,undefined,requested); assert.equal(parsed.selectedDate,requested); assert.ok(calls[0].includes('?date=2026-10-06'));
 calls.length=0; assert.equal(await readLiveMeetings('https://localhost:3000',fetcher,undefined,'2026-2-1'),undefined); assert.equal(calls.length,0);
 const wrong=async()=>Response.json({...response,selectedDate:null}); assert.equal(await readLiveMeetings('https://localhost:3000',wrong,undefined,requested),undefined);
 const malformed=structuredClone(response); malformed.availableDates=['2026-10-05','2026-10-06']; assert.equal(readMeetings(malformed),undefined);
 const empty=renderToStaticMarkup(React.createElement(LiveMeetingView,{meetings:[],selected:'',onSelect(){},availableDates:['2026-10-06','2026-10-05'],selectedDate:'2026-10-05',onDateSelect(){}})); assert.ok(empty.includes('ยังไม่มีรายงานในวันที่เลือก'));
 const many=structuredClone(fixture()); many.availableDates=Array.from({length:32},(_,i)=>new Date(Date.UTC(2026,9,31-i)).toISOString().slice(0,10)); assert.ok(readMeetings(many));
 const mixed=structuredClone(response); mixed.meetings.push({...mixed.meetings[0],businessDate:'2026-10-05',attemptId:'cccccccc-cccc-4ccc-8ccc-cccccccccccc'}); assert.equal(readMeetings(mixed),undefined);
 const missingIndex=structuredClone(response); missingIndex.availableDates=['2026-10-05']; assert.equal(readMeetings(missingIndex),undefined);
 const emptyResponse={...response,availableDates:['2026-10-06','2026-10-04'],selectedDate:'2026-10-05',meetings:[]};
 assert.ok(readMeetings(emptyResponse)); assert.ok(await readLiveMeetings('https://localhost:3000',async()=>Response.json(emptyResponse),undefined,'2026-10-05'));
 assert.equal(readMeetings({...emptyResponse,selectedDate:'2026-10-06'}),undefined);
 for (const invalid of ['2026-02-30','2026-10-06&account=other']) {calls.length=0; assert.equal(await readLiveMeetings('https://localhost:3000',fetcher,undefined,invalid),undefined); assert.equal(calls.length,0);}
});

test('expanded research reader accepts eight cited coins and only verified public hosts',()=>{
 const symbols=['BTC','ETH','SOL','DOGE','XRP','ADA','USDT','NEAR'].map(s=>s+'-THB');
 const urls=['https://bitcoin.org/bitcoin.pdf','https://ethereum.org/whitepaper/','https://solana.com/docs/core','https://raw.githubusercontent.com/dogecoin/dogecoin/master/doc/FAQ.md','https://xrpl.org/docs/concepts/consensus-protocol','https://raw.githubusercontent.com/cardano-foundation/developer-portal/main/docs/operators/basics/consensus-staking.md','https://tether.to/en/whitepaper/','https://raw.githubusercontent.com/near/docs/master/papers/Nightshade.pdf'];
 const brief={observedAt:'2026-10-07T00:00:00Z',changeKey:'a'.repeat(64),watchlist:[],gaps:[],sources:symbols.map((symbol,i)=>({id:'doc-'+String(i+1).repeat(64),symbol,title:'Official source',url:urls[i],contentHash:String(i+1).repeat(64),retrievedAt:'2026-10-06T23:00:00Z'})),findings:symbols.map((symbol,i)=>({symbol,summary:'รายงานอ้างอิง',claims:[{statement:'ข้อมูลจากโครงการ',assessment:'PROJECT_CLAIM',sourceId:'doc-'+String(i+1).repeat(64),quote:'Exact source quotation of sufficient length'}]}))};
 assert.equal(readResearch(brief,Date.parse(brief.observedAt)).findings.length,8);
 const ninth=structuredClone(brief);ninth.findings.push({...ninth.findings[0],symbol:'DOT-THB'});assert.equal(readResearch(ninth,Date.parse(brief.observedAt)),undefined);
 brief.sources[4].url='https://evil.example/docs';assert.equal(readResearch(brief,Date.parse(brief.observedAt)),undefined);
});
test('native date picker replaces date tabs and round options contain only the chosen day',()=>{
 const first=fixture().meetings[0], review={...first,reviewHour:14,attemptId:'cccccccc-cccc-4ccc-8ccc-cccccccccccc'};
 const old={...first,businessDate:'2026-10-04',attemptId:'dddddddd-dddd-4ddd-8ddd-dddddddddddd',summary:{...first.summary,reports:[{...role('portfolio-final'),summary:'old-day-report',dataCutoff:'2026-10-03'}]}};
 const render=props=>renderToStaticMarkup(React.createElement(LiveMeetingView,{selected:'',onSelect(){},availableDates:['2026-10-06','2026-10-04'],onDateSelect(){},...props}));
 const single=render({meetings:[first],selectedDate:'2026-10-06'}); assert.ok(single.includes('type="date"')); assert.ok(!single.includes('<select')); assert.ok(single.indexOf('type="date"')<single.indexOf('<details'));
 assert.ok(single.includes('min="2026-10-04"')); assert.ok(single.includes('value="2026-10-06"')); assert.doesNotMatch(single,/วันที่มีรายงาน|team-report-dates|team-report-date-buttons/);
 const multiple=render({meetings:[review,first,old],selectedDate:'2026-10-06'}); assert.equal((multiple.match(/<option/g)||[]).length,2); assert.ok(!multiple.includes('old-day-report'));
 const historic=render({meetings:[review,first,old],selectedDate:'2026-10-04'}); assert.ok(historic.includes('old-day-report')); assert.ok(!historic.includes('เก็บเงินสด'));
 const empty=render({meetings:[review,first,old],selectedDate:'2026-10-05'}); assert.ok(empty.includes('ยังไม่มีรายงานในวันที่เลือก')); assert.ok(!empty.includes('old-day-report')); assert.ok(!empty.includes('เก็บเงินสด'));
 const pending=render({meetings:[first],selectedDate:'2026-10-06',pending:true}); assert.ok(pending.includes('เก็บเงินสด')); assert.ok(pending.includes('กำลังอัปเดตรายงาน'));
 const failed=render({meetings:[first],selectedDate:'2026-10-06',failed:true}); assert.ok(failed.includes('เก็บเงินสด')); assert.ok(failed.includes('อัปเดตรายงานไม่สำเร็จ'));
 const otherDay=render({meetings:[first],selectedDate:'2026-10-04',pending:true}); assert.ok(!otherDay.includes('เก็บเงินสด')); assert.ok(otherDay.includes('กำลังอ่านรายงาน'));
});
