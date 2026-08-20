# Squad Team

> ElBruno.S1Mini

## Coordinator

| Name | Role | Notes |
|------|------|-------|
| Squad | Coordinator | Routes work, enforces handoffs and reviewer gates. |

## Members

| Name | Role | Charter | Status |
|------|------|---------|--------|
| Mara | Lead | .squad/agents/mara/charter.md | Active |
| Ivo | .NET Engineer | .squad/agents/ivo/charter.md | Active |
| Kade | Model/Inference Engineer | .squad/agents/kade/charter.md | Active |
| Nia | QA/Test Engineer | .squad/agents/nia/charter.md | Active |
| Sera | Docs/Release Engineer | .squad/agents/sera/charter.md | Active |
| Scribe | Session Logger | .squad/agents/scribe/charter.md | Active |
| Ralph | Work Monitor | .squad/agents/ralph/charter.md | Active |
| Rai | RAI Reviewer | .squad/agents/Rai/charter.md | Active |
| Fact Checker | Fact Checker | .squad/agents/fact-checker/charter.md | Active |

## Coding Agent

<!-- copilot-auto-assign: false -->

| Name | Role | Charter | Status |
|------|------|---------|--------|
| @copilot | Coding Agent | — | 🤖 Coding Agent |

### Capabilities

**🟢 Good fit — auto-route when enabled:**
- Bug fixes with clear reproduction steps
- Test coverage (adding missing tests, fixing flaky tests)
- Lint/format fixes and code style cleanup
- Dependency updates and version bumps
- Small isolated features with clear specs
- Boilerplate/scaffolding generation
- Documentation fixes and README updates

**🟡 Needs review — route to @copilot but flag for squad member PR review:**
- Medium features with clear specs and acceptance criteria
- Refactoring with existing test coverage
- API endpoint additions following established patterns
- Migration scripts with well-defined schemas

**🔴 Not suitable — route to squad member instead:**
- Architecture decisions and system design
- Multi-system integration requiring coordination
- Ambiguous requirements needing clarification
- Security-critical changes (auth, encryption, access control)
- Performance-critical paths requiring benchmarking
- Changes requiring cross-team discussion

## Project Context

- **Project:** ElBruno.S1Mini
- **Created:** 2026-08-20
- **Requested by:** Copilot
- **Stack:** .NET 8+, C#, ONNX Runtime GenAI, HuggingFace model packaging, local ASR normalization
- **Primary goal:** Normalize raw ASR transcripts into clean written text with strong eval and release discipline
- **Source prompt:** FIRST_PROMPT.md
