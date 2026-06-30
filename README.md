# KeyManager

**English** · [한국어](README.kr.md)

A local secret broker. It stores secrets (API keys, etc.) encrypted, and when an
authorized local app queries over a Named Pipe it hands back only that value, safely.
See [docs/developmentpurpose.md](docs/developmentpurpose.md) for the design rationale.

## Layout

| Project | Target | Role |
|---|---|---|
| `src/KeyManager.Protocol` | net10.0 | Shared wire protocol + transport crypto (authCode/sessionKey, AES-GCM) |
| `src/KeyManager.Core` | net10.0-windows | KDF, vault storage, broker logic, Named Pipe server |
| `src/KeyManager.Client` | net10.0 | Consumer SDK (`KeyManagerClient`) |
| `src/KeyManager.App` | net10.0-windows | WinForms tray agent (management UI) |
| `samples/KeyManager.SampleClient` | net10.0 | Console consumer example |
| `tests/KeyManager.Core.Tests` | net10.0-windows | Unit + Named Pipe E2E tests |

## Security model (summary)

- **At rest (①)**: master password → Argon2id (PBKDF2-SHA256 in phase 1) → `Kd`. Names, values, seeds, and allow-lists are all AES-256-GCM. `Kd` is resident only while unlocked (wiped on manual lock); idle auto-lock is disabled by default.
- **In transit (②)**: the server issues a fresh challenge nonce per connection. The consumer derives `authCode` (authentication) and `sessionKey` (transport encryption) from the shared seed `S` on its own. `S`, `sessionKey`, and `Kd` never travel over the wire.
- A consumer can read **only the keys within its allow-list**, and only ever removes the transport layer to obtain plaintext. The master key never leaves the agent.

## Build & test

```sh
dotnet build KeyManager.slnx
dotnet test tests/KeyManager.Core.Tests
```

## Usage (GUI)

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

**Call it (two lines)**
```csharp
using KeyManager.Client;

var km = KeyManagerClient.FromBase64Seed("myapp", base64Seed); // the seed from registration
string apiKey = await km.GetAsync("openai");                   // plaintext value
// var keys = await km.ListAsync();                            // accessible key names
```
If the agent is locked or the key isn't permitted, a `KeyManagerException` is thrown.

**Quick check via CLI (sample app)**
```sh
dotnet run --project samples/KeyManager.SampleClient -- myapp <base64Seed> list
dotnet run --project samples/KeyManager.SampleClient -- myapp <base64Seed> get openai
```

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
- `release/KeyManager.App/` — the agent. Run `KeyManager.App.exe`.
- `release/sdk/` — `KeyManager.Client.dll` + `KeyManager.Protocol.dll` for consumers to reference.

This is a framework-dependent build, so the target PC needs the .NET 10 runtime.

## Phase 1 (MVP) scope

Local only. Cloud sync (zero-knowledge hybrid), backup/recovery, and the Argon2id swap are planned for
phase 2 ([docs/developmentpurpose.md](docs/developmentpurpose.md) §11, §15).
