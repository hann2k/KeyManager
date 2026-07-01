# KeyManager — TCP 버전 아키텍처 계약 (구현 스펙)

> 로드맵 1번 "TCP 버전"의 **구현 계약(contract)**. 모든 subagent(backend/winform/i18n/misc)는
> 이 문서를 단일 기준으로 삼는다. 배경: [handoff.md](../handoff.md), [docs/개발목적.md](개발목적.md).
> 복호화 모델 = **C1 봉투(envelope)** — 서버는 마스터키(`Kd`)를 갖지 않는다.

---

## 1. 컴포넌트 (프로세스 3종 + 라이브러리)

| 프로젝트 | 타깃 | 종류 | 역할 |
|---|---|---|---|
| `src/KeyManager.Protocol` | net10.0 | lib | 와이어 메시지·프레이밍·전송/봉투 암호 (양측 공유) |
| `src/KeyManager.Core` | net10.0-windows | lib | `VaultStore`·KDF·AeadBox·**봉투 생성**. 마스터 GUI가 사용 |
| `src/KeyManager.Server` | net10.0-windows | **WinExe** | **상주** TCP/TLS 서버. 암호화 금고+봉투 보관·전달. 읽기전용 트레이 |
| `src/KeyManager.MasterGui` | net10.0-windows | **WinExe** | **비상주** 관리 GUI. `Kd` 보유, 편집, 서버로 pull/push |
| `src/KeyManager.Client` | net10.0 | lib | 소비 앱 SDK. 봉투 fetch → `S`로 복호화 |
| `samples/KeyManager.SampleClient` | net10.0 | exe | 콘솔 소비 앱 예시 |

`src/KeyManager.App`(구 Named Pipe 단일 앱)은 **1단계(로컬) 유물로 보존**하되, TCP 버전 흐름에는 참여하지 않는다.
(README에서 1단계=Named Pipe, 2단계=TCP로 계속 구분.)

### 런타임 관계
```
[MasterGui] --pull/push(admin auth, TLS)-->  [Server(상주)]  <--fetchEnvelope(S auth, TLS)-- [소비앱 + Client SDK]
   ▲ Kd 보유, 편집                              금고 blob + 봉투[] 보관               ▲ 자기 봉투만 받아 S로 복호화
   └ 금고 전체 + 재생성 봉투 push               (Kd 없음: 아무것도 못 엶)
```

---

## 2. 복호화 모델 C1 — 봉투(envelope)

- **금고(master vault)**: 기존 `VaultFile` JSON 그대로. 이름·값·시드·권한 전부 `Kd` 암호. 서버는 이걸 **불투명 blob**으로만 보관.
- **봉투(envelope)**: 소비 앱마다 1개. *그 앱의 허용 키(allowedKeys 전개 결과)의 `{전체이름: 값}` 묶음*을 그 앱의 시드 `S`에서 유도한 키로 봉인.
- **권한 강제 시점 = 봉투 생성(마스터 GUI)**. 서버는 키별 권한을 몰라도 된다 — 봉투에 이미 허용된 것만 들어 있다.
- **소비 앱**: 자기 봉투를 받아 `S`로 열어 `{이름:값}` 획득. 마스터키는 절대 안 가짐.
- **값 중복 허용**: 여러 앱이 같은 키를 허가받으면 각 봉투에 값이 중복 저장(데이터 적으므로 무방).

---

## 3. 암호 / 키 유도 (`KeyManager.Protocol.TransportCrypto` 확장)

기존 `authCode`/`sessionKey`(도메인 분리) 규칙을 유지하고 라벨을 추가한다.

| 키 | 유도식 | 용도 |
|---|---|---|
| `authCode` | `HMAC(secret, "auth" ‖ nonce ‖ timeStep ‖ op ‖ arg)` | 요청 인증. `secret`=소비앱 `S` 또는 admin 토큰 `A` |
| `envKey` | `HMAC(S, "env")` | **봉투 봉인/해제 키**(AES-256-GCM). nonce 무관, 안정적 |

- `authCode`의 `arg` = fetchEnvelope면 clientName, pull/push면 op 문자열 자체(또는 빈 문자열).
- 봉투 봉인은 `TransportCrypto.Seal(envKey, plaintext)`(랜덤 IV) → 매 push마다 새 IV. 소비 앱은 `TransportCrypto.Open(envKey, iv, tag, ct)`.
- **와이어로 안 흐름**: `S`, `A`, `envKey`, `Kd`, 값 평문. **흐름(노출 OK)**: nonce, timeStep, authCode, 봉투 암호문, 금고 blob(전부 `Kd` 암호).

### 시계 오차 허용
로컬이 아니므로 `timeStep` 창을 **±2 step(±60초)** 로 넓힌다. `ProtocolConstants`에 `AllowedTimeStepSkew = 2` 추가.

---

## 4. 서버 저장 포맷 (서버 디스크, `%APPDATA%\KeyManager\server-store.json`)

```jsonc
ServerStore {
  Version: 1,
  AdminAuthKey: "base64(A)",       // admin 토큰 A(32B). 서버 최초 실행 시 생성, push 인증에 사용
  MasterVaultJson: "…",            // 마스터 금고 VaultFile JSON(Kd 암호). 서버는 못 엶
  Envelopes: [ EnvelopeRecord ]
}
EnvelopeRecord {
  Client: "myapp",                 // 평문(서버 라우팅용 식별자)
  Iv, Tag, Ct: "base64…",          // EnvelopeContent를 envKey로 봉인
  UpdatedAt: "2026-..."            // ISO-8601
}
EnvelopeContent {                  // 봉인 전 평문(JSON) — 서버는 절대 못 봄
  Entries: { "LsOpenApi:AppKey": "값", ... }
}
```

- `A`는 대칭키라 서버가 검증하려면 보유해야 한다 → **원격 공격자**(디스크 접근 없음)를 막는 게 목적. 같은 머신 디스크 접근 공격은 범위 밖(§14 개발목적 한계와 동일).
- TLS 인증서(자체서명) + 개인키도 서버가 보관: `%APPDATA%\KeyManager\server-cert.pfx`.

---

## 5. 프로토콜 (TCP + TLS 위, 길이 프리픽스 JSON 프레이밍 재사용)

TLS 핸드셰이크 후, 기존 `Framing`(4B big-endian 길이 + UTF-8 JSON) 위에서:

```
[연결+TLS]  서버 → ServerHello { Protocol, Nonce(base64 16B) }
[요청]      클라 → VaultRequest { Op, Client?, TimeStep, AuthCode, Push? }
[응답]      서버 → VaultResponse { Ok, Error?, ...op별 필드 }
```

### Op 3종
| Op | 인증 secret | 요청 필드 | 성공 응답 |
|---|---|---|---|
| `fetchEnvelope` | 소비앱 `S` | `Client` | `{ Iv, Tag, Ct }` (그 앱 봉투) |
| `pullVault` | admin `A` | — | `{ MasterVaultJson }` |
| `pushVault` | admin `A` | `Push:{ MasterVaultJson, Envelopes[] }` | `{ Ok }` |

- fetchEnvelope: 서버가 `Client`로 봉투 조회. `authCode`는 `S` 없인 못 만드므로 그 자체가 인증. 서버는 등록 소비앱별 `S`를 **모른다** → **authCode를 검증할 수 없다**. ⇒ 아래 주의 참조.

> **fetchEnvelope 인증 설계 주의(backend 결정 필요):** 서버는 `S`를 모른다(zero-knowledge).
> 따라서 서버는 authCode를 검증하지 못한다. 두 가지 선택:
> (1) **TLS만으로 채널 보호 + 봉투 자체가 `envKey`로 암호** → 서버는 인증 없이 봉투 blob을 내주되,
>     `S` 없는 자는 열지 못한다(기밀성 유지). 열람 인증이 곧 복호 가능성. **← 기본 채택.**
> (2) 마스터 GUI가 push 시 **소비앱별 authKey 검증자**(`HMAC(S,"fetchauth")` 등)를 함께 올려 서버가 대조.
>     열람 자체를 막고 싶을 때. 1단계에선 (1)로 충분(봉투는 어차피 `S` 없인 무의미).
> ⇒ **채택: (1).** fetchEnvelope는 `Client`만 받고 봉투 blob을 반환. 기밀성은 `envKey`가 보장.
> (DoS/열거 방지는 TLS + 서버 트레이의 존재로 완화. 강한 열람 인증은 2단계 옵션.)

- pull/push: `A` 기반 `authCode`를 서버가 보유한 `A`로 재계산·대조(±2 step, nonce 1회성).

### 프레임 크기
금고+봉투가 커질 수 있으므로 `MaxFrameBytes`를 **16 MiB**로 상향.

---

## 6. 봉투 생성 (`KeyManager.Core`)

`VaultStore`(unlock 상태)에 서버 push 페이로드를 만드는 메서드 추가:

```csharp
// 반환: (마스터 금고 JSON, 소비앱별 봉투 목록). Kd로 값 복호화 → S로 재봉인.
public ServerPushData BuildServerPush();

public sealed record ServerPushData(string MasterVaultJson, IReadOnlyList<EnvelopeRecord> Envelopes);
```

로직(각 클라이언트마다):
1. `S`, `allowedKeys`(grants) 복호화.
2. 모든 시크릿 이름 순회 → `KeyAccess.IsAllowed(grants, name)`이면 값 복호화해 `Entries[name]=value`.
3. `envKey = TransportCrypto.DeriveEnvelopeKey(S)`; `EnvelopeContent{Entries}` JSON 직렬화 → `TransportCrypto.Seal(envKey, ...)`.
4. `EnvelopeRecord{Client=name, Iv, Tag, Ct, UpdatedAt=now}`.
`MasterVaultJson = JsonSerializer.Serialize(_file)`(현재 `Kd` 암호 금고 그대로).

`EnvelopeRecord`/`EnvelopeContent`/`ServerPushData`는 **Protocol**에 두어 서버·클라·코어가 공유.

---

## 7. Client SDK 변경 (`KeyManager.Client`)

Named Pipe per-key 왕복 → **TCP/TLS 봉투 fetch 후 로컬 조회**로 변경.

```csharp
var km = KeyManagerClient.FromBase64Seed("myapp", base64Seed, host, port); // 엔드포인트 추가
string v   = await km.GetAsync("openai");        // 봉투 1회 fetch(캐시) 후 Entries[key]
var keys   = await km.ListAsync();               // Entries.Keys
var dict   = await km.GetGroupAsync("LsOpenApi");// Entries 중 InGroup 필터
```

- 최초 호출 시 봉투를 fetch·복호화해 인스턴스에 캐시(같은 인스턴스 재요청은 네트워크 없이). `RefreshAsync()`로 강제 재fetch.
- API 시그니처(Get/List/GetGroup)는 **유지** → 소비 앱 코드 불변. 생성자에 `host`/`port`(+선택 인증서 pin) 추가.
- 서버 미실행/키 없음/권한 없음 → `KeyManagerException`.

### TLS 신뢰 (TOFU)
서버 자체서명 인증서를 최초 접속 시 thumbprint 고정(pin) 저장. 이후 불일치면 거부. Master GUI도 동일.
(1단계 기본: pin 파일 `%APPDATA%\KeyManager\pinned-<host>.txt`. 옵션으로 검증 완화 플래그.)

---

## 8. Master GUI (`KeyManager.MasterGui`, 비상주)

- **트레이 없음.** 실행 → 서버 접속·pull → 마스터 암호 입력(unlock) → 편집 → 저장 시 push → 창 X = **프로세스 종료**(`Environment.Exit`).
- 기존 관리 폼 **재사용**: `MainForm`(키/클라이언트/Language 탭), `Dialogs`, `MasterPasswordForm`, `ChangeMasterPasswordForm`, `DescriptionForm`, `KeyTreeBuilder`, `Loc`, `AppSettings`.
  재사용 방식(링크 파일 vs 공유 classlib)은 winform이 선택하되 **App/MasterGui 폼 중복 로직 금지**.
- 편집 흐름: 서버에서 `MasterVaultJson` pull → 로컬 임시 파일에 기록 → `VaultStore.Load` → `Unlock` → 편집(등록/수정/삭제/클라 등록/권한) → **저장 액션에서 `BuildServerPush()` → pushVault**.
- 첫 설정: 서버에 금고가 없으면(빈 pull) 마스터 암호 **생성** 후 push. admin 토큰 `A`·서버 주소는 `AppSettings`(MasterGui용)에 보관.
- 마스터 암호 변경(`ChangeMasterPassword`)도 지원 → 전체 재봉인 후 push.

## 9. Server 트레이 (`KeyManager.Server`, 상주)

- 트레이 상주. 아이콘 우클릭 메뉴 **딱 2개**: **"Key 목록 보기"**, **"종료"**.
- "Key 목록 보기" = **읽기전용 목록 창**. 서버는 `Kd`가 없으므로 **키 이름을 못 본다**. 표시 내용(기본):
  등록된 **봉투(소비앱) 이름 목록**, 각 봉투의 **키 개수·UpdatedAt**, 금고 존재/항목 수·최종 push 시각.
  (실제 키 이름 표시는 zero-knowledge 위반 → 기본 제외. 요구 시 매니페스트 방식 별도 결정.)
- 서버 프로세스가 TCP/TLS 리슨(기본 포트 `9713`)·`ServerStore` 로드/저장. unlock 개념 없음(암호 불필요) → 부팅 후 바로 서비스 가능.

---

## 10. 상수 (`ProtocolConstants` 추가)
```csharp
public const int    DefaultTcpPort       = 9713;
public const string EnvLabel             = "env";
public const int    AllowedTimeStepSkew  = 2;      // ±2 step
public const int    AdminTokenLengthBytes = 32;
// MaxFrameBytes: 1<<24 (16 MiB)로 상향
```

## 11. 작업 분담
- **backend**: Protocol(메시지/상수/`TransportCrypto` 봉투키·Seal 공유), Core(`BuildServerPush`), Server 네트워크·저장·서빙, Client SDK TCP/봉투 재작성, 테스트.
- **winform**: MasterGui(폼 재사용·pull/편집/push 흐름), Server 트레이(읽기전용 목록 창·트레이 2메뉴).
- **i18n**: 신규 문자열(서버 트레이·MasterGui 접속/서버설정 관련) 영/한.
- **misc**: 새 프로젝트 2개 csproj·slnx 등록, `publish.ps1`(서버+MasterGui+sdk), SampleClient 엔드포인트 인자, README/문서.

## 12. 열린 결정
- 서버 "Key 목록": **메타데이터만(기본)**. 실제 키 이름 원하면 매니페스트로 전환(누출 감수).
- fetchEnvelope 열람 인증: **(1) TLS+envKey 기밀성만(기본)**. 강한 열람 인증은 2단계.
