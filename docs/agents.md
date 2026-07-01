# Development agents

This repo is worked on with specialized Claude Code subagents that divide the work by domain,
plus a main (orchestrator) agent. Subagent definitions live in [`.claude/agents/`](../.claude/agents/)
and are version-controlled; the rest of `.claude/` (personal settings, memory) is git-ignored.

## Roles

| Agent | Owns | Use it for |
|---|---|---|
| **backend** | `src/KeyManager.Core`, `src/KeyManager.Protocol`, `src/KeyManager.Client`, `tests/`, plus the non-UI backbone of the stage-2 server/admin transport | Crypto (KDF/AES-GCM/HMAC), vault storage, Named Pipe broker, wire protocol, client SDK, and their tests. Stage 2: `src/KeyManager.Server` non-UI classes (`ServerStore`, `TcpVaultServer`, `ServerHost`), `src/KeyManager.Core/MasterServerConnection.cs`, the new Protocol wire messages/TLS support, `VaultStore.BuildServerPush` |
| **winform** | `src/KeyManager.App` (UI), `src/KeyManager.MasterGui`, and the `src/KeyManager.Server` tray UI | Tray app forms, dialogs, layout, high-DPI, TreeView, event wiring, `TrayContext` lifecycle. Stage 2: the non-resident admin GUI (`MasterAppContext`, `ConnectingForm`, `ConnectionSetupForm`, `MasterSettings`) and the Server tray UI (`ServerTrayContext`, `ServerKeyListForm`, `AdminTokenForm`). MasterGui reuses App's forms via linked compile items (single source, no duplication) |
| **i18n** | `src/KeyManager.App/Loc.cs` + language flow | User-facing strings, keeping English/Korean in sync, adding a language, the language selector. `Loc.cs` now also covers the `srv.*`/`mg.*` keys |
| **misc** | build/packaging, samples, docs | `publish.ps1`, `*.csproj`, `.gitignore`, `SampleClient`, `README*`, `docs/`. `publish.ps1` now emits Server + MasterGui + App + sdk |
| **main** (orchestrator) | — | Planning, delegating to the agents above, then **build → publish → commit** and documenting changes |

**Stage 1 vs. stage 2.** Stage 1 is the local single agent over a Named Pipe (`src/KeyManager.App`).
Stage 2 is the TCP/TLS split into a resident server (`src/KeyManager.Server`) + a non-resident admin GUI
(`src/KeyManager.MasterGui`). The stage-2 implementation contract is
[tcp-architecture.md](tcp-architecture.md) (KR) / [tcp-architecture.en.md](tcp-architecture.en.md) (EN).

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

Stage-2 **C1 envelope model**: the TCP server is zero-knowledge (holds no `Kd`); the Master GUI
generates per-consumer envelopes sealed with each consumer's seed `S`; the server stores and serves
opaque blobs only.

## Orchestration + release flow (main agent)

1. Plan and delegate domain work to the specialized agents.
2. Build and test: `dotnet build KeyManager.slnx` + `dotnet test tests/KeyManager.Core.Tests`.
3. Package: `powershell ./publish.ps1` (updates the committed `release/`).
4. Commit (and push on request), documenting what changed.
