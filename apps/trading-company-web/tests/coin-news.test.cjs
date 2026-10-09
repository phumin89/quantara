const test = require('node:test'), assert = require('node:assert/strict'), path = require('node:path');
const { readResearch, CoinResearchView } = require('./load-component.cjs')(path.join(__dirname, '../app/CoinResearchView.tsx'));
const React = require('react'), { renderToStaticMarkup } = require('react-dom/server');
const now = Date.parse('2026-10-09T00:00:00Z');
const fixture = () => ({
  observedAt: '2026-09-10T02:00:00Z', changeKey: 'a'.repeat(64), watchlist: [], sources: [], findings: [], gaps: [],
  news: {
    observedAt: '2026-09-10T01:59:00Z', changeKey: 'b'.repeat(64),
    items: [{ id: 'news-' + 'c'.repeat(64), source: 'CoinDesk', kind: 'REPORT', title: 'Bitcoin market report', url: 'https://www.coindesk.com/markets/2026/09/10/bitcoin', sourceAt: '2026-09-10T01:00:00Z', dateKind: 'PUBLISHED', retrievedAt: '2026-09-10T01:58:00Z', contentHash: 'c'.repeat(64), symbols: ['BTC-THB'], marketWide: false, evidenceText: 'Feed excerpt describing the reported market event.' }],
    coverage: [{ source: 'CoinDesk', status: 'CURRENT' }, { source: 'Bitkub', status: 'UNAVAILABLE' }]
  }
});
const render = research => renderToStaticMarkup(React.createElement(CoinResearchView, { research, businessDate: '2026-09-10' }));
const invalid = mutate => { const value = fixture(); mutate(value.news, value); assert.equal(readResearch(value, now), undefined); assert.equal(render(value), ''); };

test('saved news remains historical, renders source/date/link once, and escapes source text', () => {
  const value = fixture(); value.news.items[0].title = 'Bitcoin <img src=x> report';
  assert.deepEqual(readResearch(value, now), value);
  const html = render(value);
  assert.match(html, /ข่าวล่าสุดของรอบนี้ · 1 รายการ/);
  assert.match(html, /Bitcoin &lt;img src=x&gt; report/);
  assert.doesNotMatch(html, /<img/);
  assert.match(html, /CoinDesk · รายงานข่าว · เผยแพร่ <time dateTime="2026-09-10T01:00:00Z"/);
  assert.match(html, /href="https:\/\/www.coindesk.com\/markets\/2026\/09\/10\/bitcoin" target="_blank" rel="noreferrer"/);
  assert.equal(html.split(value.news.items[0].evidenceText).length - 1, 1);
  assert.match(html, /<details><summary>ข้อความอ้างอิงข่าว<\/summary>/);
  value.news.items[0].dateKind = 'UPDATED';
  assert.match(render(value), /รายงานข่าว · อัปเดต <time/);
  assert.doesNotMatch(render(value), /เผยแพร่/);
});

test('news dates reject future, stale, calendar-invalid and timezone-less evidence', () => {
  for (const mutate of [
    n => n.observedAt = '2026-09-10T02:00:01Z', n => n.observedAt = '2026-09-10T01:43:59Z',
    n => n.items[0].sourceAt = '2026-09-10T02:00:00Z', n => n.items[0].sourceAt = '2026-09-03T01:58:59Z',
    n => n.items[0].retrievedAt = '2026-09-10T01:59:01Z', n => n.items[0].retrievedAt = '2026-09-10T01:42:59Z',
    n => n.items[0].sourceAt = '2026-02-30T01:00:00Z', n => n.items[0].retrievedAt = '2026-09-10T01:58:00',
    n => n.items[0].sourceAt = '2026-09-10T24:00:00Z', n => n.items[0].sourceAt = '2026-09-10T01:00:00+14:01'
  ]) invalid(mutate);
  const boundary = fixture(); boundary.news.items[0].sourceAt = '2026-09-03T01:59:00Z'; boundary.news.items[0].retrievedAt = '2026-09-10T01:43:00Z';
  assert.ok(readResearch(boundary, now));
  boundary.news.items[0].sourceAt = '2026-09-10T08:00:00.0000000+07:00';
  assert.ok(readResearch(boundary, now));
});

test('news links follow each exact public source policy without broadening document links', () => {
  for (const url of ['javascript:alert(1)', 'http://www.coindesk.com/news', 'https://evil.example/news', 'https://www.coindesk.com.evil.example/news', 'https://www.coindesk.com@evil.example/news', 'https://owner:secret@www.coindesk.com/news', 'https://www.coindesk.com:444/news', 'https://www.coindesk.com/news?q=secret', 'https://www.coindesk.com/news?', 'https://www.coindesk.com/news#fragment', 'https://www.coindesk.com/news#', 'https://www.coindesk.com/', 'https://www.coin\ndesk.com/news']) invalid(n => n.items[0].url = url);
  const policies = [
    ['Ethereum', 'https://blog.ethereum.org/en/2026/09/10/update', 'https://blog.ethereum.org/blog/update'],
    ['Federal Reserve', 'https://www.federalreserve.gov/newsevents/pressreleases/monetary20260910a.htm', 'https://www.federalreserve.gov/news/report'],
    ['Bitkub', 'https://support.bitkub.com/en/solutions/articles?id=KM000010405', 'https://support.bitkub.com/en/solutions/articles?id=000010405']
  ];
  for (const [source, good, bad] of policies) {
    const value = fixture(); Object.assign(value.news.items[0], { source, kind: 'OFFICIAL', url: good }); value.news.coverage = [{ source, status: 'CURRENT' }];
    assert.ok(readResearch(value, now)); assert.match(render(value), /ประกาศทางการ/);
    for (const url of [bad, `${good}${good.includes('?') ? '&' : '?'}secret=1`]) { value.news.items[0].url = url; assert.equal(readResearch(value, now), undefined); }
  }
  const document = fixture(); document.sources = [{ id: 'doc-' + 'd'.repeat(64), symbol: 'BTC-THB', title: 'Document', url: document.news.items[0].url, contentHash: 'd'.repeat(64), retrievedAt: document.observedAt }];
  assert.equal(readResearch(document, now), undefined);
});

test('news rejects unsupported or coerced sources, kinds, coverage and provenance', () => {
  for (const mutate of [
    n => n.items[0].source = 'Reuters', n => n.items[0].source = 'toString', n => n.items[0].kind = 'OFFICIAL',
    n => n.items[0].dateKind = 'SEEN', n => n.items[0].dateKind = ['PUBLISHED'], n => n.items[0].marketWide = 'false',
    n => n.items[0].contentHash = 'bad', n => n.items[0].id = 'news-' + 'd'.repeat(64), n => n.changeKey = 'bad',
    n => n.coverage = [], n => n.coverage[0].source = 'Unknown', n => n.coverage[0].status = 'STALE',
    n => n.coverage[0].status = ['CURRENT'], n => n.coverage[0].status = 'UNAVAILABLE', n => n.coverage = [{ source: 'Ethereum', status: 'CURRENT' }],
    n => n.items[0].title = ' ', n => n.items[0].title = 'x'.repeat(181), n => n.items[0].evidenceText = 'x'.repeat(281),
    n => n.items[0].title = 'Headline\nextra', n => n.items[0].evidenceText = 'Evidence\u0085extra'
  ]) invalid(mutate);
});

test('news rejects duplicate ids, links, coverage, symbols and oversized briefs', () => {
  for (const mutate of [
    n => n.items.push({ ...n.items[0], url: 'https://www.coindesk.com/other' }),
    n => n.items.push({ ...n.items[0], id: 'news-' + 'd'.repeat(64), contentHash: 'd'.repeat(64) }),
    n => n.coverage.push({ ...n.coverage[0] }), n => n.items[0].symbols = ['BTC-THB', 'BTC-THB'],
    n => n.items[0].symbols = ['btc-thb'], n => n.items[0].symbols = Array.from({ length: 9 }, (_, i) => `${i}-THB`),
    n => n.items = Array(13).fill(n.items[0]), n => n.coverage = Array(5).fill(n.coverage[0]),
    n => n.extra = 'x'.repeat(12 * 1024)
  ]) invalid(mutate);
});

test('unavailable news sources remain explicit even when no items can be read', () => {
  const value = fixture(); value.news.items = []; value.news.coverage = [{ source: 'Bitkub', status: 'UNAVAILABLE' }, { source: 'CoinDesk', status: 'UNAVAILABLE' }];
  assert.ok(readResearch(value, now));
  const html = render(value);
  assert.match(html, /Bitkub: ไม่พร้อม · CoinDesk: ไม่พร้อม/);
  assert.match(html, /ข่าวล่าสุดของรอบนี้ · 0 รายการ/);
  assert.doesNotMatch(html, /ไม่มีข่าว|ยังไม่มีข่าว|<a /);
});

test('legacy briefs without news still render and explicit null remains optional', () => {
  const value = fixture(); delete value.news;
  assert.deepEqual(readResearch(value, now), value);
  assert.match(render(value), /เหรียญและหลักฐานที่ทีมตรวจ/);
  assert.doesNotMatch(render(value), /ข่าวล่าสุด/);
  value.news = null;
  assert.ok(readResearch(value, now));
});
