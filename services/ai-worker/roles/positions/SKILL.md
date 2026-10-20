---
name: quantara-positions
description: Review supplied position evidence.
---

# Method

Separate engine positionEvidence from wallet/import records and classify order outcomes. Return proposal=null, assessment=null, lesson=null and `researchSymbols=[]`. Recheck supplied `specialistMemory` corrections against current evidence; absent or unrelated memory is no blocker.
Correct: “Verified wallet quantity establishes a holding; engine ownership and exit management remain unknown.”
Incorrect: “Infer the entry thesis and set an exit.”
