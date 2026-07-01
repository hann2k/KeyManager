# KeyManager

**English** · [한국어](README.kr.md)

A local secret broker. It stores secrets (API keys, etc.) encrypted and hands back
only the values an authorized app is allowed to see, safely. Two stages ship in the repo:
**stage 1** — a single local tray agent over a **Named Pipe**; **stage 2** — a **TCP/TLS**
split into a resident server + a non-resident admin GUI, so consumers on other machines,
containers, or WSL can connect. See [docs/developmentpurpose.md](docs/developmentpurpose.md)
for the design rationale and [docs/tcp-architecture.md](docs/tcp-architecture.md) for the
stage-2 contract.

## Layout

| Project | Target | Role |
|---|---|---|
| `src/KeyManager.Protocol` | net10.0 | Shared wire protocol + transport/envelope crypto (authCode, envKey, AES-GCM) |
| `src/KeyManager.Core` | net10.0-windows | KDF, vault storage, envelope building, Named Pipe server |
| `src/KeyManager.Server` | net10.0-windows | **Stage 2** — resident TCP/TLS vault host (tray, read-only) |
| `src/KeyManager.MasterGui` | net10.0-windows | **Stage 2** — non-resident admin GUI (pull→unlock→edit→push) |
| `src/KeyManager.Client` | net10.0 | Consumer SDK (`KeyManagerClient`) |
| `src/KeyManager.App` | net10.0-windows | **Stage 1** — WinForms tray agent (Named Pipe, all-in-one) |
| `samples/KeyManager.SampleClient` | net10.0 | Console consumer example |
| `tests/KeyManager.Core.Tests` | net10.0-windows | Unit + protocol E2E tests |

## Security model (summary)

- **At rest (①)**: master password → Argon2id (memory-hard; legacy PBKDF2 vaults are auto-detected and migrated on password change) → `Kd`. Names, values, seeds, and allow-lists are all AES-256-GCM. `Kd` is resident only while unlocked (wiped on manual lock); idle auto-lock is disabled by default.
- **In transit (②)**: the server issues a fresh challenge nonce per connection. The consumer derives `authCode` (authentication) and `sessionKey` (transport encryption) from the shared seed `S` on its own. `S`, `sessionKey`, and `Kd` never travel over the wire.
- A consumer can read **only the keys within its allow-list**, and only ever removes the transport layer to obtain plaintext. The master key never leaves the agent.

## Build & test

```sh
dotnet build KeyManager.slnx
dotnet test tests/KeyManager.Core.Tests
```

## Usage (GUI) — stage 1 (Named Pipe, local)

> This is the original single-agent, local-only build (`KeyManager.App`). For the
> multi-machine TCP setup, skip to [Stage 2 — TCP version](#stage-2--tcp-version).

### 0) Start — lives in the tray
Run `dotnet run --project src/KeyManager.App`. On first launch you set a master password; once the startup unlock completes the management window opens automatically. Otherwise it stays resident as a **shield icon in the system tray**.
- First launch: **set the master password** (no recovery if lost).
- Later launches: **unlock** with the same password.
- Tray icon: **double-click** = open the management window, **right-click** = `Open` / `Lock` (Unlock) / `Exit`.

### 1) Store keys — "Keys" tab
1. Management window → **"Keys" tab**
2. **Add** → name (e.g. `openai`) + value (the real API key). Tick "Show value" while typing to verify the plaintext (you can't view it again after saving).
3. After saving, the list shows **names only** (values hidden — by design). Use a colon (`:`) for hierarchy and entries group into a tree.
- **Change value** / **Delete key** / **Delete group**: select an item, then the button. The `●` marker means a node that holds a value.

### 2) Register a consumer app — "Clients" tab
1. **"Clients" tab** → **Register**
2. Client name (e.g. `myapp`) + **check the keys this app may access** (allow-list). Checking a group node grants its whole subtree.
3. Right after registering, the **seed (base64) is shown once** → **"Copy to clipboard"** and store it in the consumer app.
   > ⚠️ Once you close the dialog the seed cannot be shown again.
- **Edit permissions**: select a client → add/remove allowed keys (the seed is kept).
- **Delete**: remove the client.

### 3) Lock / Exit
- **Lock**: right-click → `Lock` (manual). While locked, every request is rejected. (Idle auto-lock is disabled — consumers fetch keys frequently.)
- **Exit**: right-click → `Exit`.

> Flow at a glance: **set master password → add keys in the "Keys" tab → register an app in the "Clients" tab (copy the seed) → the consumer queries with that seed.**

## Stage 2 — TCP version

Stage 2 splits the single agent into **three runtime pieces** so consumers can live on
other machines/containers/WSL. Decryption model is **C1 (envelope)**: the server never
holds the master key.

```
[MasterGui] --pull/push (admin token A, TLS)-->  [Server (resident)]  <--fetch envelope (seed S, TLS)-- [consumer app + Client SDK]
   ▲ holds Kd, edits the vault              stores vault blob + envelopes            ▲ receives only its own envelope,
   └ pushes full vault + regenerated          (no Kd: it can open nothing)             opens it with S
     envelopes on save
```

| Piece | Kind | Role |
|---|---|---|
| **KeyManager.Server** | resident | TCP/TLS vault host. Keeps the `Kd`-encrypted vault blob + per-app envelopes. Tray icon, read-only. Default port **9713**. |
| **KeyManager.MasterGui** | non-resident | Admin GUI. Holds `Kd` while running, edits, pulls/pushes. Closes → process exits. |
| **KeyManager.Client** | library | Consumer SDK. Fetches its envelope over TLS and decrypts it locally with seed `S`. |

### Run the Server (resident)
Run `KeyManager.Server.exe` (or `dotnet run --project src/KeyManager.Server`). It listens on
TCP/TLS port 9713 and, **on first run, generates and prints the admin token `A` once** — copy
and save it; you paste it into the Master GUI. It then lives in the tray:
- **right-click** → **"Key list"** (a read-only window — see [security model](#security-model-stage-2)) and **"Exit"**.
- No master password: the server can decrypt nothing, so it needs none and serves immediately after boot.

### Run the Master GUI (non-resident)
Run `KeyManager.MasterGui.exe` (or `dotnet run --project src/KeyManager.MasterGui`).
1. **First-run connection setup**: enter the server **host**, **port** (9713), and the **admin token `A`** printed by the server. These are remembered for next time.
2. **Master password**: if the server has no vault yet, you **create** one; otherwise you **unlock** with the existing password. `Kd` lives only in memory while the GUI runs — it is never stored.
3. **Edit** keys and clients exactly like the stage-1 GUI (Keys / Clients / Language tabs). On save, the GUI rebuilds the affected per-app envelopes and **auto-pushes** the full vault + envelopes to the server.
4. **Close the window** → the process exits (no tray, no residency).

Master-password **change** is supported too: it re-encrypts the whole vault and pushes.

### Connect a consumer app
Point the SDK at the server endpoint (see the [consumer section](#using-it-from-a-consumer-app) for referencing the DLLs):
```csharp
var km = KeyManagerClient.FromBase64Seed("myapp", base64Seed, host, port); // e.g. "192.168.0.10", 9713
string apiKey = await km.GetAsync("openai");
```
The API shapes (`GetAsync` / `ListAsync` / `GetGroupAsync`) are **unchanged** from stage 1 — only the
constructor gains `host`/`port` (and an optional TLS pin-file path). The client fetches its envelope
once and caches it; `RefreshAsync()` forces a re-fetch.

**TLS TOFU pinning** — the server presents a self-signed certificate. On first connect the client
pins its thumbprint (default `%APPDATA%\KeyManager\pinned-<host>.txt`); any later mismatch is rejected.
The Master GUI pins the same way.

### Security model (stage 2)

- **Zero-knowledge server**: the server has no `Kd` and no seed `S`. It stores the vault only as an opaque `Kd`-encrypted blob, plus one **envelope** per consumer app — a bundle of that app's allowed `{fullName: value}` pairs, sealed with a key derived from that app's seed `S`. **Permissions are enforced when the Master GUI builds the envelope**, so the server never needs to know per-key ACLs.
- **The server's "Key list" shows metadata only, never key names.** Because the server cannot open anything, the read-only window lists the registered **envelope (app) names**, each envelope's **key count** and **UpdatedAt**, and whether a vault exists (item count, last push time). Showing real key names would require decryption the server cannot do — so it is deliberately excluded (that would break zero-knowledge).
- **Admin token `A`** authenticates pull/push against remote attackers (no disk access). The vault is `Kd`-encrypted so it can't be forged, but `A` prevents overwrite/DoS. A same-machine disk-access attacker is out of scope, matching the stage-1 threat model.

## Using it from a consumer app

A consumer references the **`KeyManager.Client` library**. (`KeyManager.Client.dll` depends on
`KeyManager.Protocol.dll`, so both must be in the output folder. Both target `net10.0`, so they work
from non-Windows .NET apps too.) `release/sdk/` ships both DLLs.

**A. Same solution — project reference**
```xml
<ProjectReference Include="..\KeyManager\src\KeyManager.Client\KeyManager.Client.csproj" />
```

**B. Separate app — DLL reference**
Copy `KeyManager.Client.dll` + `KeyManager.Protocol.dll`, then:
```xml
<ItemGroup>
  <Reference Include="KeyManager.Client"><HintPath>libs\KeyManager.Client.dll</HintPath></Reference>
  <Reference Include="KeyManager.Protocol"><HintPath>libs\KeyManager.Protocol.dll</HintPath></Reference>
</ItemGroup>
```

**Call it (two lines)** — stage 2 (TCP): pass the server `host`/`port`.
```csharp
using KeyManager.Client;

var km = KeyManagerClient.FromBase64Seed("myapp", base64Seed, host, port); // seed from registration; e.g. "192.168.0.10", 9713
string apiKey = await km.GetAsync("openai");                               // plaintext value
// var keys = await km.ListAsync();                                        // accessible key names
```
If the server is unreachable, the key isn't permitted, or the TLS pin doesn't match, a `KeyManagerException` is thrown.

**Quick check via CLI (sample app)**
```sh
# SampleClient [--host H] [--port P] <clientName> <base64Seed> <list|get|getGroup> [key]
dotnet run --project samples/KeyManager.SampleClient -- --host 192.168.0.10 --port 9713 myapp <base64Seed> list
dotnet run --project samples/KeyManager.SampleClient -- myapp <base64Seed> get openai
dotnet run --project samples/KeyManager.SampleClient -- myapp <base64Seed> getGroup LsOpenApi
```
Endpoint resolution: `--host`/`--port` flags > `KM_HOST`/`KM_PORT` env > default `127.0.0.1:9713`.

## Grouped (hierarchical) keys — the colon (`:`) convention

Express hierarchy with `:` (e.g. `LsOpenApi:Simulation:AppKey`) to treat keys as a group (storage stays flat). There is no wildcard (`*`) — **a name itself means its whole subtree.**

- **Permissions**: putting `LsOpenApi` in the allow-list grants `LsOpenApi` and everything under it. `LsOpenApi:Simulation` grants only that subtree. `LsOpenApi:AppKey` grants just that leaf. (In the GUI, check the group node in the tree.)
- **Matching is on segment boundaries**: `LsOpenApi:Sim` does NOT match `LsOpenApi:Simulation:*`.
- **New keys are auto-included**: granting a group automatically covers keys later added under it.

**Fetch a whole group → feed `IConfiguration` directly**
```csharp
var km   = KeyManagerClient.FromBase64Seed("myapp", base64Seed);
var dict = await km.GetGroupAsync("LsOpenApi");        // all permitted sub-keys as {fullName: value}
var cfg  = new ConfigurationBuilder()
              .AddInMemoryCollection(dict!).Build();   // colon names map to nested sections
var opt  = cfg.GetSection("LsOpenApi").Get<LsOpenApiOptions>();
```
Even a broad request returns nothing outside your permissions (requested prefix ∩ allowed scope).

## Packaging

```sh
powershell ./publish.ps1
```
Assembles `release/`:
- `release/KeyManager.Server/` — stage-2 resident TCP/TLS server. Run `KeyManager.Server.exe` (prints admin token `A` once on first run).
- `release/KeyManager.MasterGui/` — stage-2 non-resident admin GUI. Run `KeyManager.MasterGui.exe`.
- `release/KeyManager.App/` — stage-1 Named Pipe agent (local, kept for reference). Run `KeyManager.App.exe`.
- `release/sdk/` — `KeyManager.Client.dll` + `KeyManager.Protocol.dll` for consumers to reference.

This is a framework-dependent build, so the target PC needs the .NET 10 runtime.

## Phase 1 (MVP) scope

Local only. Cloud sync (zero-knowledge hybrid) and backup/recovery are planned for
phase 2 ([docs/developmentpurpose.md](docs/developmentpurpose.md) §11, §15).

## Roadmap

Stage 1 is local-only over Named Pipe. Status:

1. **TCP version — implemented / in progress.** A TCP/TLS split (`KeyManager.Server` resident host + `KeyManager.MasterGui` non-resident admin) so consumers on other machines, containers, or WSL can connect. Uses the C1 envelope model (zero-knowledge server), TOFU-pinned TLS, admin-token auth, and clock-skew tolerance for the time-based codes. See [docs/tcp-architecture.md](docs/tcp-architecture.md).
2. **Cloud version — future.** The zero-knowledge hybrid (docs §11): the cloud syncs **only the encrypted vault** while decryption stays local. Adds a sync backend, conflict/version handling, and backup/recovery (recovery key).
