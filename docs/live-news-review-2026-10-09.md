# Dated news in Live company meetings

The company previously received price/history evidence and saved protocol research but no current news collector. Live research now includes bounded, dated feed headlines and excerpts. These reach the existing meeting roles and saved reports; no extra news summarizer, paid provider or model call was added.

## Evidence and coverage

Public-only smoke verification at **2026-10-09 07:36:45 UTC** returned a valid **12-item / 9,211-byte** news brief. All three configured feeds responded successfully:

- [CoinDesk RSS](https://www.coindesk.com/arc/outboundfeeds/rss): reporting, not independent confirmation. The canonical URL has no trailing slash; the slash version redirects. [Publisher feed documentation](https://www.coindesk.com/coindesk-news/2021/09/17/coindesk-rss).
- [Ethereum blog RSS](https://blog.ethereum.org/en/feed.xml): official protocol commentary, not proof of security or profitability.
- [Federal Reserve monetary releases](https://www.federalreserve.gov/feeds/press_monetary.xml): official macro context. Publication time does not establish the event date. [Publisher feed directory](https://www.federalreserve.gov/feeds/feeds.htm).

Bitkub announcement collection remains explicitly **UNAVAILABLE**. Its [public support index](https://support.bitkub.com/en/solutions?Id=151000093916) uses an undocumented guest UI transport: the category response exceeds 5 MB, folder timestamps have no timezone, and backend LastModifiedDate differs from the article's displayed Updated_At__c. This release does not guess freshness, fetch every historical article or claim that missing announcements mean no news.

The smoke reader used 337 public THB exchange-spot metadata symbols solely for headline association, including inactive metadata. This is **not** the execution universe or an authority expansion. Runtime research uses public ticker observations and the existing owner scope. Explicit native tickers can match beyond the existing document catalog; single-letter/numeric symbols require markers. Regression checks prevent `U.S.` from becoming `S-THB` and an unmarked price `42` from becoming `42-THB`.

## Flow and limits

The existing research scan polls at most once per 15 minutes per scope while the company service runs. Each response is bounded to 2 MiB and 1,000 entries. XML entities/external resolution are prohibited, redirects and cookies disabled, and article links are publisher-specific HTTPS URLs. Source timestamps require explicit timezones. Only entries within seven days **as of the scan** are considered; PUBLISHED, UPDATED and retrieval times stay distinct.

Original bytes, fetch provenance and brief versions are content-addressed and retained. Unchanged selected evidence and coverage keep the same change key. A material news change reaches the next eligible hourly research review through the existing five-role path; an unchanged news/price checkpoint does not repeat model work. Meetings remain subject to the existing scheduling and quota rules.

Packets contain at most 12 headlines, 280 characters per excerpt and 12 KiB of news within the unchanged 24 KiB research bound. Selection favors shortlisted/held symbols and publisher diversity; clipping is deterministic. This does not cover every publisher, every coin, full articles or verified news impact. Feed omissions are not evidence that an event never occurred. Headlines remain untrusted inputs and cannot issue commands or authorize orders.

Live prompts explicitly seek Aggressive entries and portfolio rotation within the existing owner limits. Roles compare cash opportunity cost, price/volume confirmation and net costs, then give entry price, quantity, target, stop and expiry. Uncertainty must be explained and sized; it does not automatically justify all-HOLD or fabricated confidence. News does not bypass historical evidence quality, RiskEngine, account binding or owner authority.

An individual feed failure records UNAVAILABLE. News-only archive failure also records unavailable coverage while preserving valid scout/document research; it does not globally pause otherwise valid trading. No transaction, activation, financial configuration change or profit claim is part of this implementation.

## Review and verification

Codex inspected the actual production and test diffs. An independent second pass found news archive I/O could discard valid market research; that coupling was fixed with a regression test. Cache publication occurs only after the brief is saved. Existing historical reports retain their recorded content and remain readable without the optional news field.

- Headless deterministic trading suite: **1,022 passed**, no skipped tests. Includes source/time/hash/URL validation, XML entities, malformed inputs, source outage, immutable archives, polling, news changes reaching the next review, unchanged document model quota and retained financial limits.
- Frontend suite: **117 passed**; TypeScript check and production web build passed. Historical-date validation, escaped excerpts, malicious links, duplicates, coverage states and legacy reports verified.
- Source-component UI passes: **1440×900, 768×1024, 390×844**; no horizontal page/panel overflow. Phone excerpt disclosure works, with a 44-pixel target. These were synthetic meeting previews using public feed evidence, not actual company decisions.
- Company and owner images built successfully. Only the web service was updated; HTTPS root returned **200**.

At verification the company container was **Exited (0)**, while owner/web/ledger were running. The prepared owner and company images require the owner's existing **Enable Auto Trading.cmd** startup/activation flow before this news pipeline operates in that account. The financial owner and company were not restarted, and stored mandates, execution allowlist, owner limits and real orders were not changed.

Public smoke logs/raw fixtures and build/test logs remain under ignored `work/`; no credentials, private account state or raw news archive is committed.
