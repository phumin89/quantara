const test=require('node:test'), assert=require('node:assert/strict'), path=require('node:path');
const {readMeetings,readLiveMeetings,LiveMeetingView}=require('./load-component.cjs')(path.join(__dirname,'../app/LiveTeamReports.tsx'));
const React=require('react'),{renderToStaticMarkup}=require('react-dom/server');
const scope={mode:1,accountId:'1'.repeat(32)};
const role=(name)=>({role:name,summary:name==='portfolio-final'?'เก็บเงินสดไว้รอข้อมูลครบ':'เหตุผลจากข้อมูลที่ตรวจแล้ว',providerModel:'fixture',inputTokens:2,outputTokens:2,artifactId:'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',evidenceHash:'a'.repeat(64),dataCutoff:'2026-10-05'});
const fixture=()=>({accountContext:scope,meetings:[{businessDate:'2026-10-06',attemptId:'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',status:'HOLD',accountContext:scope,summary:{modelCalls:8,inputTokens:16,outputTokens:16,reports:[role('portfolio-final'),role('risk-critic'),role('portfolio-initial')]}}]});
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
