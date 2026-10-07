const test = require('node:test'), assert = require('node:assert/strict'), path = require('node:path');
const { readLearning, CompanyLearningView } = require('./load-component.cjs')(path.join(__dirname, '../app/CompanyLearningView.tsx'));
const { readMeetings } = require('./load-component.cjs')(path.join(__dirname, '../app/LiveTeamReports.tsx'));
const React = require('react'), { renderToStaticMarkup } = require('react-dom/server');
const names = ['ceo','chief-of-staff','market-research','security-analysis','quant-research','portfolio-initial','risk-critic','portfolio-final'];
const scope = {mode:1,accountId:'1'.repeat(32)}, day='2026-10-07';
const fixture=()=>({businessDate:day,accountContext:{...scope},modelCalls:1,roles:names.map(role=>({role,status:'ACCEPTED',reportsReviewed:1,lesson:{artifactId:'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',quote:'Saved report quotation with <script> content',lesson:'บทเรียนจากหลักฐานเดิม',nextPractice:'ตรวจสมมติฐานกับข้อมูลใหม่'}}))});
test('learning parser enforces account date canonical roles and bounded evidence',()=>{
 assert.ok(readLearning(fixture(),scope,day));
 for(const mutate of [x=>x.accountContext={...scope,accountId:'2'.repeat(32)},x=>x.accountContext.mode=0,x=>x.businessDate='2026-10-08',x=>x.businessDate='2026-09-29',x=>x.businessDate='2026-02-30',x=>x.roles.reverse(),x=>x.roles[0].lesson.quote='too short',x=>x.roles[0].lesson.nextPractice='x'.repeat(251),x=>x.roles[0].lesson.artifactId='',x=>x.roles[0].reportsReviewed=0,x=>x.modelCalls=2,x=>x.roles[0].status='PROMOTED']){
  const value=fixture();mutate(value);assert.equal(readLearning(value,scope,day),undefined);
 }
});
test('saved learning view is optional, escaped, and never claims demonstrated skill',()=>{
 const x=fixture();x.roles[1]={role:names[1],status:'NO_HISTORY',reportsReviewed:0,lesson:null};
 const html=renderToStaticMarkup(React.createElement(CompanyLearningView,{learning:x,scope,meetingDate:day,labels:{ceo:'CEO'}})).replace(/<!--.*?-->/g,'');
 assert.ok(html.includes('7/8'));assert.ok(html.includes('ยังไม่ใช่ผลวัดประสิทธิภาพ'));assert.ok(html.includes('รอบถัดไป'));assert.ok(html.includes('ดูหลักฐานบทเรียน'));assert.ok(html.includes('&lt;script&gt;'));assert.ok(!html.includes('<script>'));assert.ok(!html.includes('<details open'));
 const m={businessDate:day,accountContext:scope,attemptId:'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',status:'HOLD',summary:{modelCalls:0,inputTokens:0,outputTokens:0,reports:[],learning:x}};
 assert.ok(readMeetings({accountContext:scope,meetings:[m]}));
 m.summary.learning.accountContext={...scope,accountId:'2'.repeat(32)};assert.equal(readMeetings({accountContext:scope,meetings:[m]}),undefined);
});
