# Live report UI recovery — 2026-10-10

The owner saw a report-read error with three loading bars. The company had completed its meeting and the ledger held the reports. `CoinResearchView` still allowed only the older public source hosts, so the exact KUB V5.2 paper URL accepted by the updated receiver caused `readMeetings` to reject the entire feed.

The lead reproduced the rejection on the actual saved latest report and with a deterministic regression. The frontend now accepts only `https://www.kubchain.com/docs/EN_KUB_Chain_WhitePaper_V5.2.pdf` as the additional source. Query strings, fragments, alternate files/hosts, credentials and HTTP remain rejected. This changes display validation only; execution authority is unchanged.

Failed report reads now show a readable alert and a native retry button instead of loading bars. Retry uses the existing bounded GET, cancels the previous polling effect, preserves saved reports/date/round selection and leaves one 30-second polling timer. Loading still uses the existing skeleton and reserved space. The lead independently reviewed the worker bridge's direct fallback UI patch and added the URL correction, retry styling, cached-report retry and regression checks.

Verification:

- The KUB regression failed before the URL correction and passed afterward, including rejection of seven unsafe or unapproved URL variants.
- All **28** relevant report/history/news/learning UI checks passed, zero failures or skips. Type checking and the production Docker build passed.
- All **31** reports across **5** saved dates in the actual default feed passed the corrected reader, with no rejected individual reports. The deployed web image's reader also accepted the same feed.
- A bounded browser pass used clearly labeled synthetic UI data and the actual component/CSS at desktop 1440×1000, tablet 820×1180 and phone 390×844. Failed states had no skeleton bars; tablet/phone had no horizontal overflow and the retry target was 47.1875 px high. This is component visual proof; the diagnostic browser did not have the owner's authenticated report session.
- Only `owner-web` was replaced. Its image matches the new built image, and its local page returned HTTP 200. The actual account stayed disarmed, Stop false and reconciled. No financial activation, order, cancellation, owner limits, binding or RiskEngine change was performed.

The owner's existing browser needs one page refresh to load the corrected client bundle. This repair restores report display; it does not claim live execution has been activated.
