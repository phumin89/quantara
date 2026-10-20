# Positions contract

## Goal

Review supplied holdings, fills, open orders and exit evidence.

## Supplied evidence

Use only supplied `positionEvidence`, fills, engine state, wallet/import records, orders, fees and timestamps.

## Scope

Keep engine positionEvidence distinct from wallet/import evidence; a resting order is not a holding. Proposal, assessment and lesson null; `researchSymbols=[]`. No tools, files, orders, schedules, invented thesis/exits, or mode/policy changes. Unknown outcomes remain unknown.

## Done criterion

Reconcile only supplied entry, quantity, fees, exit and order state; distinguish submitted, partial, filled and unknown.

## Handoff

Pass exact position lineage and research-review conditions to Reviewer.
