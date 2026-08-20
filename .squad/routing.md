# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Scope, priorities, release decisions | Mara | Architecture choices, sequencing, risk triage, release gating |
| Public API, DI, package surface, .NET library work | Ivo | `S1MiniClient`, `TranscriptNormalizer`, `AddTranscriptNormalizer`, NuGet packaging |
| ONNX Runtime GenAI, prompts, inference safety, model behaviors | Kade | Guardrailing temperature, tokenizer/decode assumptions, prompt format, model eval troubleshooting |
| Tests, edge cases, regressions, evals | Nia | Failures, missing coverage, eval scripts, transcript normalization edge cases |
| README, docs, release notes, user guidance | Sera | Installation docs, transcript normalization docs, release README verification |
| Session logging | Scribe | Automatic — never needs routing |
| Backlog, queue health, keep-alive | Ralph | Monitoring, follow-up, work tracking |
| RAI review | Rai | Safety checks, credential exposure, harmful content, fairness concerns |
| Verification and devil's advocate | Fact Checker | Claim checking, pre-mortem, assumptions, external validation |
| Code review | Mara | Final review gate for architecture or release-readiness questions |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Mara |
| `squad:{name}` | Pick up issue and complete the work | Named member |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, the **Lead** triages it — analyzing content, assigning the right `squad:{member}` label, and commenting with triage notes.
2. When a `squad:{member}` label is applied, that member picks up the issue in their next session.
3. Members can reassign by removing their label and adding another member's label.
4. The `squad` label is the "inbox" — untriaged issues waiting for Lead review.

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If a feature is being built, spawn the tester to write test cases from requirements simultaneously.
7. **Issue-labeled work** — when a `squad:{member}` label is applied to an issue, route to that member. The Lead handles all `squad` (base label) triage.
8. **Repository invariants** — any change touching the runtime guardrails, model prompt contract, or zero-length decode assumptions must route to Kade and Nia before merge.
