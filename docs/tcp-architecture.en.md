# KeyManager — TCP Version Architecture Contract (Implementation Spec)

**English** · [한국어](tcp-architecture.md)

> The **implementation contract** for roadmap item 1, "TCP version". All subagents (backend/winform/i18n/misc)
> treat this document as the single source of truth. Background: [handoff.md](../handoff.md), [docs/developmentpurpose.md](developmentpurpose.md).
> Decryption model = **C1 envelope** — the server never holds the master key (`Kd`).

---

## 1. Components (processes + libraries)

| Project | Target | Kind | Role |
|---|---|---|---|
| `src/KeyManager.Protocol` | net10.0 | lib | Wire messages, framing, transport/envelope crypto (shared by both sides) |
| `src/KeyManager.Core` | net10.0-windows | lib | `VaultStore`, KDF, AeadBox, **envelope generation**. Used by the Master GUI |
| `src/KeyManager.ServerCore` | net10.0-windows | lib | Server runtime core (`ServerStore`, `TcpVaultServer`, `ServerHost`, `ServerSettings`, `StopSignal`, `VaultServerService`). **References only Protocol → zero-knowledge preserved**. Namespace stays `KeyManager.Server` |
| `src/KeyManager.Server` | net10.0-windows | **Exe** | **Headless console/service host.** A `BackgroundService` runs the TCP/TLS server on top of the Generic Host (`Microsoft.Extensions.Hosting` + `UseWindowsService()`). Prints the admin token to the console on first run, logs listening status, shuts down gracefully on Ctrl+C / SCM stop. (Not yet installed as a Windows service — host refactor only.) |
| `src/KeyManager.Tray` | net10.0-windows | **WinExe** | Tray companion app. On startup launches `KeyManager.Server.exe` from the same folder (if not already running), shows the one-time admin-token window on first run, displays live server status. Holds the moved forms `ServerKeyListForm` & `AdminTokenForm`. "Exit" gracefully stops the server via a named event (`StopSignal`) |
| `src/KeyManager.MasterGui` | net10.0-windows | **WinExe** | **Non-resident** admin GUI. Holds `Kd`, edits, pulls/pushes to the server |
| `src/KeyManager.Client` | net10.0 | lib | Consumer-app SDK. Fetches the envelope → decrypts with `S` |
| `samples/KeyManager.SampleClient` | net10.0 | exe | Console consumer-app example |

> **Server-set layout:** `KeyManager.Server.exe` (the host) and `KeyManager.Tray.exe` (the companion UI) are published into the **same output folder**.
> The tray launches the server from its own folder, and both read `server-settings.json` from `AppContext.BaseDirectory`.
> (`publish.ps1` places both executables into `release/KeyManager.Server/`.)

`src/KeyManager.App` (the former single Named Pipe app) is **kept as a stage-1 (local) legacy artifact**, but does not participate in the TCP-version flow.
(The README continues to distinguish stage 1 = Named Pipe, stage 2 = TCP.)

### Runtime relationships
```
[MasterGui] --pull/push(admin auth, TLS)-->  [Server(resident)]  <--fetchEnvelope(S auth, TLS)-- [consumer app + Client SDK]
   ▲ holds Kd, edits                            stores vault blob + envelope[]        ▲ receives only its own envelope, decrypts with S
   └ pushes whole vault + regenerated envelopes (no Kd: can open nothing)
```

---

## 2. Decryption model C1 — envelope

- **Master vault**: the existing `VaultFile` JSON as-is. Names, values, seeds, grants are all `Kd`-encrypted. The server stores this only as an **opaque blob**.
- **Envelope**: one per consumer app. *That app's allowed keys (the expanded result of allowedKeys) as a `{full name: value}` bundle*, sealed with a key derived from that app's seed `S`.
- **Grant enforcement happens at envelope generation (Master GUI)**. The server need not know per-key grants — the envelope already contains only what is allowed.
- **Consumer app**: receives its own envelope, opens it with `S` to obtain `{name:value}`. It never holds the master key.
- **Value duplication allowed**: if multiple apps are granted the same key, the value is stored redundantly in each envelope (fine, since the data is small).

---

## 3. Crypto / key derivation (`KeyManager.Protocol.TransportCrypto` extension)

Keep the existing `authCode`/`sessionKey` (domain-separation) rules and add labels.

| Key | Derivation | Purpose |
|---|---|---|
| `authCode` | `HMAC(secret, "auth" ‖ nonce ‖ timeStep ‖ op ‖ arg)` | Request authentication. `secret` = consumer `S` or admin token `A` |
| `envKey` | `HMAC(S, "env")` | **Envelope seal/unseal key** (AES-256-GCM). Nonce-independent, stable |

- The `arg` of `authCode` = clientName for fetchEnvelope, the op string itself (or empty string) for pull/push.
- Envelope sealing is `TransportCrypto.Seal(envKey, plaintext)` (random IV) → a fresh IV on every push. The consumer app uses `TransportCrypto.Open(envKey, iv, tag, ct)`.
- **Never on the wire**: `S`, `A`, `envKey`, `Kd`, plaintext values. **On the wire (exposure OK)**: nonce, timeStep, authCode, envelope ciphertext, vault blob (all `Kd`-encrypted).

### Clock-skew tolerance
Since this is not local, widen the `timeStep` window to **±2 steps (±60s)**. Add `AllowedTimeStepSkew = 2` to `ProtocolConstants`.

---

## 4. Server storage format (server disk, `%APPDATA%\KeyManager\server-store.json`)

```jsonc
ServerStore {
  Version: 1,
  AdminAuthKey: "base64(A)",       // admin token A (32B). Generated on the server's first run, used for push auth
  MasterVaultJson: "…",            // master vault VaultFile JSON (Kd-encrypted). The server cannot open it
  Envelopes: [ EnvelopeRecord ]
}
EnvelopeRecord {
  Client: "myapp",                 // plaintext (identifier for server routing)
  Iv, Tag, Ct: "base64…",          // EnvelopeContent sealed with envKey
  UpdatedAt: "2026-..."            // ISO-8601
}
EnvelopeContent {                  // pre-seal plaintext (JSON) — the server never sees it
  Entries: { "LsOpenApi:AppKey": "value", ... }
}
```

- `A` is a symmetric key, so the server must hold it to verify → the goal is to stop a **remote attacker** (no disk access). An attack with disk access to the same machine is out of scope (same as the §14 limits in developmentpurpose).
- The TLS certificate (self-signed) and its private key are also held by the server: `%APPDATA%\KeyManager\server-cert.pfx`.

---

## 5. Protocol (over TCP + TLS, reusing length-prefixed JSON framing)

After the TLS handshake, on top of the existing `Framing` (4B big-endian length + UTF-8 JSON):

```
[connect+TLS]  server → ServerHello { Protocol, Nonce(base64 16B) }
[request]      client → VaultRequest { Op, Client?, TimeStep, AuthCode, Push? }
[response]     server → VaultResponse { Ok, Error?, ...op-specific fields }
```

### The 3 Ops
| Op | Auth secret | Request fields | Success response |
|---|---|---|---|
| `fetchEnvelope` | consumer `S` | `Client` | `{ Iv, Tag, Ct }` (that app's envelope) |
| `pullVault` | admin `A` | — | `{ MasterVaultJson }` |
| `pushVault` | admin `A` | `Push:{ MasterVaultJson, Envelopes[] }` | `{ Ok }` |

- fetchEnvelope: the server looks up the envelope by `Client`. The `authCode` cannot be made without `S`, so it is itself the authentication. The server **does not know** each registered consumer's `S` → it **cannot verify the authCode**. ⇒ See the note below.

> **fetchEnvelope auth design note (backend decision needed):** the server does not know `S` (zero-knowledge).
> Therefore the server cannot verify the authCode. Two options:
> (1) **Protect the channel with TLS only + the envelope itself is encrypted with `envKey`** → the server serves the envelope blob without auth,
>     but whoever lacks `S` cannot open it (confidentiality preserved). Ability to open = the authentication. **← default choice.**
> (2) The Master GUI also uploads a **per-consumer auth verifier** (`HMAC(S,"fetchauth")` etc.) on push, which the server checks.
>     Use this when you want to block the read itself. For stage 1, (1) is sufficient (the envelope is meaningless without `S` anyway).
> ⇒ **Adopted: (1).** fetchEnvelope takes only `Client` and returns the envelope blob. Confidentiality is guaranteed by `envKey`.
> (DoS/enumeration prevention is mitigated by TLS + the presence of the server tray. Strong read authentication is a stage-2 option.)

- pull/push: the server recomputes the `A`-based `authCode` with the `A` it holds and compares (±2 steps, nonce one-time).

### Frame size
The vault + envelopes can grow large, so raise `MaxFrameBytes` to **16 MiB**.

---

## 6. Envelope generation (`KeyManager.Core`)

Add a method to `VaultStore` (in the unlocked state) that builds the server push payload:

```csharp
// Returns: (master vault JSON, per-consumer envelope list). Decrypts values with Kd → re-seals with S.
public ServerPushData BuildServerPush();

public sealed record ServerPushData(string MasterVaultJson, IReadOnlyList<EnvelopeRecord> Envelopes);
```

Logic (for each client):
1. Decrypt `S` and `allowedKeys` (grants).
2. Iterate all secret names → if `KeyAccess.IsAllowed(grants, name)`, decrypt the value and set `Entries[name]=value`.
3. `envKey = TransportCrypto.DeriveEnvelopeKey(S)`; serialize `EnvelopeContent{Entries}` to JSON → `TransportCrypto.Seal(envKey, ...)`.
4. `EnvelopeRecord{Client=name, Iv, Tag, Ct, UpdatedAt=now}`.
`MasterVaultJson = JsonSerializer.Serialize(_file)` (the current `Kd`-encrypted vault as-is).

`EnvelopeRecord`/`EnvelopeContent`/`ServerPushData` live in **Protocol** so the server, client, and core can share them.

---

## 7. Client SDK changes (`KeyManager.Client`)

Change the Named Pipe per-key round trip → **TCP/TLS envelope fetch, then local lookup**.

```csharp
var km = KeyManagerClient.FromBase64Seed("myapp", base64Seed, host, port); // endpoint added
string v   = await km.GetAsync("openai");        // fetch the envelope once (cached), then Entries[key]
var keys   = await km.ListAsync();               // Entries.Keys
var dict   = await km.GetGroupAsync("LsOpenApi");// filter Entries by InGroup
```

- On the first call, fetch and decrypt the envelope and cache it on the instance (re-requests on the same instance need no network). `RefreshAsync()` forces a re-fetch.
- The API signatures (Get/List/GetGroup) are **kept** → consumer-app code is unchanged. The constructor gains `host`/`port` (+ optional cert pin).
- Server not running / key missing / no permission → `KeyManagerException`.

### TLS trust (TOFU)
Pin (fix) the server's self-signed certificate thumbprint on first connect. Reject on any later mismatch. The Master GUI does the same.
(Stage-1 default: pin file `%APPDATA%\KeyManager\pinned-<host>.txt`. Optional flag to relax verification.)

---

## 8. Master GUI (`KeyManager.MasterGui`, non-resident)

- **No tray.** Launch → connect to the server & pull → enter the master password (unlock) → edit → push on save → window X = **process exit** (`Environment.Exit`).
- **Reuse** the existing admin forms: `MainForm` (Keys/Clients/Language tabs), `Dialogs`, `MasterPasswordForm`, `ChangeMasterPasswordForm`, `DescriptionForm`, `KeyTreeBuilder`, `Loc`, `AppSettings`.
  The reuse method (linked files vs. a shared classlib) is winform's choice, but **no duplicated App/MasterGui form logic**.
- Edit flow: pull `MasterVaultJson` from the server → write to a local temp file → `VaultStore.Load` → `Unlock` → edit (add/modify/delete/register client/grants) → **`BuildServerPush()` → pushVault on the save action**.
- First-time setup: if the server has no vault (empty pull), **create** the master password, then push. The admin token `A` and server address are stored in `AppSettings` (for MasterGui).
- Master password change (`ChangeMasterPassword`) is also supported → re-seal everything, then push.

## 9. Server tray companion (`KeyManager.Tray`, WinExe)

- The tray UI is now a **separate companion process** (`KeyManager.Tray`). The server itself is a headless console/service host (`KeyManager.Server`), which the tray **launches and monitors**.
  - On startup it launches `KeyManager.Server.exe` from the same folder if it isn't already running. On first run it shows the admin-token window once.
  - "Exit" **gracefully stops** the server via a named event (`StopSignal`), then exits the tray too.
  - The tray shows **live server status** (listening / stopped).
- The icon's right-click menu still has **exactly 2 items** (unchanged): **"View key list"**, **"Exit"**.
- "View key list" = a **read-only list window** (unchanged). Since the server has no `Kd`, it **cannot see key names**. Display content (default):
  the list of registered **envelope (consumer-app) names**, each envelope's **key count & UpdatedAt**, vault presence / item count / last push time.
  (Showing actual key names would violate zero-knowledge → excluded by default. If required, a manifest approach is a separate decision.)
- The server host process listens on TCP/TLS (default port `9713`) and loads/saves `ServerStore`. There is no unlock concept (no password) → it can serve immediately after boot. Ctrl+C (console), SCM stop, and the tray "Exit" are all graceful-shutdown paths.

---

## 10. Constants (`ProtocolConstants` additions)
```csharp
public const int    DefaultTcpPort       = 9713;
public const string EnvLabel             = "env";
public const int    AllowedTimeStepSkew  = 2;      // ±2 steps
public const int    AdminTokenLengthBytes = 32;
// MaxFrameBytes: raised to 1<<24 (16 MiB)
```

## 11. Work split
- **backend**: Protocol (messages/constants/`TransportCrypto` envelope key & Seal sharing), Core (`BuildServerPush`), Server networking/storage/serving, Client SDK TCP/envelope rewrite, tests.
- **winform**: MasterGui (form reuse, pull/edit/push flow), Server tray (read-only list window, 2-item tray menu).
- **i18n**: new strings (server tray, MasterGui connect/server-settings related) in English/Korean.
- **misc**: register the 2 new projects' csproj/slnx etc., `publish.ps1` (server + MasterGui + sdk), SampleClient endpoint args, README/docs.

## 12. Open decisions
- Server "key list": **metadata only (default)**. If actual key names are wanted, switch to a manifest (accepting the leak).
- fetchEnvelope read auth: **(1) TLS + envKey confidentiality only (default)**. Strong read auth is a stage-2 option.
