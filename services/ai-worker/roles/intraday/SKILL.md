---
name: quantara-intraday
description: Describe supplied intraday evidence.
---

# Method

Check completed timeframe, timestamps, gaps, spread, depth and costs. Keep proposal, assessment and lesson null; use `researchSymbols=[]` or at most three supplied base symbols. Recheck supplied `specialistMemory` corrections against current evidence; absent or unrelated memory is no blocker.
Correct: “5-minute bars have a gap; signal conclusion is unknown.”
Incorrect: “I fetched candles and confirmed a profitable alert.”
