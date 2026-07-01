# Development agents

This repo is worked on with specialized Claude Code subagents that divide the work by domain,
plus a main (orchestrator) agent. Subagent definitions live in [`.claude/agents/`](../.claude/agents/)
and are version-controlled; the rest of `.claude/` (personal settings, memory) is git-ignored.

## Roles

| Agent | Owns | Use it for |
|---|---|---|
| **backend** | `src/KeyManager.Core`, `src/KeyManager.Protocol`, `src/KeyManager.Client`, `tests/` | Crypto (KDF/AES-GCM/HMAC), vault storage, Named Pipe broker, wire protocol, client SDK, and their tests |
| **winform** | `src/KeyManager.App` (UI) | Tray app forms, dialogs, layout, high-DPI, TreeView, event wiring, `TrayContext` lifecycle |
| **i18n** | `src/KeyManager.App/Loc.cs` + language flow | User-facing strings, keeping English/Korean in sync, adding a language, the language selector |
| **misc** | build/packaging, samples, docs | `publish.ps1`, `*.csproj`, `.gitignore`, `SampleClient`, `README*`, `docs/` |
| **main** (orchestrator) | — | Planning, delegating to the agents above, then **build → publish → commit** and documenting changes |

## Boundaries (who does NOT touch what)

- **backend** never touches WinForms UI or the Loc table.
- **winform** never implements crypto/storage/protocol; it calls `VaultStore` and uses `Loc.T(...)` for all text.
- **i18n** only owns string content; it does not change layout/logic (beyond swapping a literal for `Loc.T`).
- **misc** does not implement crypto, GUI forms, or the Loc table.

When a change spans domains (e.g. a new feature with UI + storage + strings), the main agent splits it:
backend adds the storage/API, winform wires the UI, i18n adds the strings, then main builds and commits.

## Security invariants (all agents respect)

See [developmentpurpose.md](developmentpurpose.md) §4/§9/§14. In short: two-key separation (`Kd`
at rest vs per-client seed `S`), everything AES-256-GCM at rest, secrets never on the wire, no
secrets in source, atomic re-encryption on master-password change.

## Orchestration + release flow (main agent)

1. Plan and delegate domain work to the specialized agents.
2. Build and test: `dotnet build KeyManager.slnx` + `dotnet test tests/KeyManager.Core.Tests`.
3. Package: `powershell ./publish.ps1` (updates the committed `release/`).
4. Commit (and push on request), documenting what changed.
