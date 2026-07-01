# KeyManager

[English](README.md) · **한국어**

로컬 시크릿 브로커. API 키 등 비밀 데이터를 암호화 저장하고, 인가된 앱에게 허용된 값만
안전하게 배급한다. 저장소에는 두 단계가 담겨 있다: **1단계** — **Named Pipe** 로컬 단일 트레이 에이전트,
**2단계** — **TCP/TLS**로 상주 서버 + 비상주 관리 GUI로 분리(다른 머신·컨테이너·WSL의 소비 앱도 접속 가능).
설계 근거는 [docs/개발목적.md](docs/개발목적.md), 2단계 구현 계약은
[docs/tcp-architecture.md](docs/tcp-architecture.md) 참고.

## 구조

| 프로젝트 | 타깃 | 역할 |
|---|---|---|
| `src/KeyManager.Protocol` | net10.0 | 공유 와이어 프로토콜·전송/봉투 암호(authCode, envKey, AES-GCM) |
| `src/KeyManager.Core` | net10.0-windows | KDF·vault 저장·봉투 생성·Named Pipe 서버 |
| `src/KeyManager.Server` | net10.0-windows | **2단계** — 상주 TCP/TLS 금고 서버(트레이, 읽기전용) |
| `src/KeyManager.MasterGui` | net10.0-windows | **2단계** — 비상주 관리 GUI(pull→unlock→편집→push) |
| `src/KeyManager.Client` | net10.0 | 소비 앱용 SDK (`KeyManagerClient`) |
| `src/KeyManager.App` | net10.0-windows | **1단계** — WinForms 트레이 에이전트(Named Pipe, 올인원) |
| `samples/KeyManager.SampleClient` | net10.0 | 콘솔 소비 앱 예시 |
| `tests/KeyManager.Core.Tests` | net10.0-windows | 단위 + 프로토콜 E2E 테스트 |

## 보안 모델 (요약)

- **저장(①)**: 마스터 암호 → Argon2id(메모리-하드; 구 PBKDF2 vault는 자동 인식·암호 변경 시 이관) → `Kd`. 이름·값·시드·권한목록 전부 AES-256-GCM. `Kd`는 unlock~lock 동안만 상주(수동 잠금 시 소거). 유휴 자동 잠금은 기본 비활성.
- **전송(②)**: 연결마다 서버가 challenge nonce 발급. 소비 앱은 공유 시드 `S`로 `authCode`(인증)와 `sessionKey`(전송 암호)를 각자 유도. `S`·`sessionKey`·`Kd`는 와이어로 흐르지 않음.
- 소비 앱은 **자기 권한(allowedKeys) 내의 키만**, 전송 암호만 풀어 평문 획득. 마스터 키는 에이전트 밖으로 나가지 않음.

## 빌드 & 테스트

```sh
dotnet build KeyManager.slnx
dotnet test tests/KeyManager.Core.Tests
```

## 사용법 (GUI) — 1단계 (Named Pipe, 로컬)

> 원래의 단일 에이전트·로컬 전용 빌드(`KeyManager.App`)다. 여러 머신용 TCP 구성은
> [2단계 — TCP 버전](#2단계--tcp-버전)으로 건너뛴다.

### 0) 시작 — 트레이 상주
`dotnet run --project src/KeyManager.App` 로 실행한다. 최초 실행 시 마스터 암호를 설정하고, 시작 시 해제가 끝나면 관리 창이 자동으로 뜬다. 평소엔 **트레이(작업표시줄 우측 아이콘 영역)에 방패 아이콘**으로 상주한다.
- 최초 실행: **마스터 암호 설정** (분실 시 복구 불가).
- 이후 실행: **잠금 해제**(같은 암호).
- 트레이 아이콘: **더블클릭** = 관리 창 열기, **우클릭** = `열기` / `잠금`(해제) / `종료`.

### 1) 키 저장 — 관리 창 "키" 탭
1. 관리 창 → **"키" 탭**
2. **추가** → 이름(예: `openai`) + 값(실제 API 키). 입력 중 "값 표시"로 평문 확인 가능(저장 후엔 다시 못 봄).
3. 저장하면 목록엔 **이름만** 표시된다(값 비노출 — 정상). 콜론(`:`)으로 계층을 주면 트리로 묶여 보인다.
- **값 변경** / **키 삭제** / **그룹 삭제**: 항목 선택 후 버튼. `●` 마커는 값을 가진 키를 뜻한다.

### 2) 소비 앱 등록 — 관리 창 "클라이언트" 탭
1. **"클라이언트" 탭** → **등록**
2. 클라이언트 이름(예: `myapp`) + **이 앱이 접근할 키를 체크**(허용목록). 그룹 노드를 체크하면 그 하위 전체 권한.
3. 등록 직후 **시드(base64)가 1회만** 표시된다 → **"클립보드로 복사"** 해서 소비 앱에 보관.
   > ⚠️ 창을 닫으면 시드를 다시 볼 수 없다.
- **권한 편집**: 클라이언트 선택 → 허용 키를 추가/제거(시드는 유지).
- **삭제**: 클라이언트 제거.

### 3) 잠금 / 종료
- **잠금**: 우클릭 → `잠금`(수동). 잠긴 동안 모든 요청 거부. (유휴 자동 잠금은 비활성 — 소비 앱이 수시로 키를 꺼내 쓰므로.)
- **종료**: 우클릭 → `종료`.

> 흐름 요약: **마스터 암호 → "키" 탭에서 키 추가 → "클라이언트" 탭에서 앱 등록(시드 복사) → 소비 앱이 그 시드로 조회.**

## 2단계 — TCP 버전

2단계는 단일 에이전트를 **런타임 3종**으로 분리해 다른 머신·컨테이너·WSL의 소비 앱도
붙을 수 있게 한다. 복호화 모델은 **C1(봉투)** — 서버는 마스터키를 절대 갖지 않는다.

```
[MasterGui] --pull/push (admin 토큰 A, TLS)-->  [Server (상주)]  <--봉투 fetch (시드 S, TLS)-- [소비 앱 + Client SDK]
   ▲ Kd 보유, 금고 편집                      금고 blob + 봉투[] 보관           ▲ 자기 봉투만 받아
   └ 저장 시 금고 전체 + 재생성 봉투 push       (Kd 없음: 아무것도 못 엶)            S로 복호화
```

| 구성 | 종류 | 역할 |
|---|---|---|
| **KeyManager.Server** | 상주 | TCP/TLS 금고 서버. `Kd` 암호 금고 blob + 소비앱별 봉투 보관. 트레이 아이콘, 읽기전용. 기본 포트 **9713**. |
| **KeyManager.MasterGui** | 비상주 | 관리 GUI. 실행 중에만 `Kd` 보유·편집·pull/push. 창 닫으면 프로세스 종료. |
| **KeyManager.Client** | 라이브러리 | 소비 앱 SDK. TLS로 자기 봉투를 받아 시드 `S`로 로컬 복호화. |

### 서버 실행 (상주)
`KeyManager.Server.exe` 실행(또는 `dotnet run --project src/KeyManager.Server`). TCP/TLS 포트 9713을
리슨하고, **최초 실행 시 admin 토큰 `A`를 1회 생성·출력**한다 — 복사해서 보관하고 마스터 GUI에 붙여넣는다.
이후 트레이에 상주한다:
- **우클릭** → **"Key 목록 보기"**(읽기전용 창 — [보안 모델](#보안-모델-2단계) 참고), **"종료"**.
- 마스터 암호 없음: 서버는 아무것도 복호화하지 못하므로 암호가 필요 없고, 부팅 직후 바로 서비스한다.

### 마스터 GUI 실행 (비상주)
`KeyManager.MasterGui.exe` 실행(또는 `dotnet run --project src/KeyManager.MasterGui`).
1. **최초 접속 설정**: 서버 **host**, **port**(9713), 서버가 출력한 **admin 토큰 `A`** 입력. 다음 실행을 위해 기억된다.
2. **마스터 암호**: 서버에 금고가 없으면 **생성**, 있으면 기존 암호로 **잠금 해제**. `Kd`는 GUI 실행 중 메모리에만 존재하고 저장하지 않는다.
3. 1단계 GUI와 동일하게 키·클라이언트를 **편집**(키/클라이언트/Language 탭). 저장 시 영향받은 소비앱 봉투를 재생성하고 금고 전체 + 봉투를 서버로 **자동 push**.
4. **창 닫기** → 프로세스 종료(트레이·상주 없음).

마스터 암호 **변경**도 지원 — 금고 전체를 재암호화한 뒤 push한다.

### 소비 앱 접속
SDK를 서버 엔드포인트로 향하게 한다(DLL 참조는 [소비 앱 섹션](#소비-앱에서-사용하기) 참고):
```csharp
var km = KeyManagerClient.FromBase64Seed("myapp", base64Seed, host, port); // 예: "192.168.0.10", 9713
string apiKey = await km.GetAsync("openai");
```
API 형태(`GetAsync` / `ListAsync` / `GetGroupAsync`)는 1단계와 **동일** — 생성자에 `host`/`port`(+선택 TLS pin
파일 경로)만 추가된다. 클라이언트는 봉투를 1회 fetch해 캐시하며, `RefreshAsync()`로 강제 재fetch한다.

**TLS TOFU 핀 고정** — 서버는 자체 서명 인증서를 제시한다. 최초 접속 시 클라이언트가 thumbprint를
고정(기본 `%APPDATA%\KeyManager\pinned-<host>.txt`)하고, 이후 불일치면 거부한다. 마스터 GUI도 동일.

### 보안 모델 (2단계)

- **Zero-knowledge 서버**: 서버는 `Kd`도, 시드 `S`도 없다. 금고는 불투명한 `Kd` 암호 blob으로만 보관하고, 소비앱마다 **봉투** 1개 — 그 앱의 허용 `{전체이름:값}` 묶음을 그 앱 시드 `S`에서 유도한 키로 봉인 — 를 함께 보관한다. **권한 강제는 마스터 GUI의 봉투 생성 시점**이라, 서버는 키별 ACL을 몰라도 된다.
- **서버의 "Key 목록"은 메타데이터만 보여주고 키 이름은 절대 안 보여준다.** 서버는 아무것도 열 수 없으므로, 읽기전용 창은 등록된 **봉투(소비앱) 이름 목록**, 각 봉투의 **키 개수·UpdatedAt**, 금고 존재 여부(항목 수·최종 push 시각)만 나열한다. 실제 키 이름 표시는 서버가 할 수 없는 복호화를 요구하므로 의도적으로 제외한다(zero-knowledge 위반이라서).
- **admin 토큰 `A`**는 (디스크 접근이 없는) 원격 공격자로부터 pull/push를 인증한다. 금고는 `Kd` 암호라 위조는 불가하지만, `A`가 덮어쓰기/DoS를 막는다. 같은 머신 디스크 접근 공격은 범위 밖이며, 1단계 위협 모델과 같다.

## 소비 앱에서 사용하기

소비 앱은 **`KeyManager.Client` 라이브러리**를 참조한다. (`KeyManager.Client.dll`은 `KeyManager.Protocol.dll`에
의존하므로 둘 다 출력 폴더에 있어야 한다. 둘 다 `net10.0`이라 Windows가 아닌 .NET 앱에서도 사용 가능.)
`release/sdk/`에 두 DLL이 준비된다.

**A. 같은 솔루션 — 프로젝트 참조**
```xml
<ProjectReference Include="..\KeyManager\src\KeyManager.Client\KeyManager.Client.csproj" />
```

**B. 별도 앱 — DLL 참조**
`KeyManager.Client.dll` + `KeyManager.Protocol.dll`을 복사한 뒤:
```xml
<ItemGroup>
  <Reference Include="KeyManager.Client"><HintPath>libs\KeyManager.Client.dll</HintPath></Reference>
  <Reference Include="KeyManager.Protocol"><HintPath>libs\KeyManager.Protocol.dll</HintPath></Reference>
</ItemGroup>
```

**호출 (두 줄)** — 2단계(TCP): 서버 `host`/`port`를 넘긴다.
```csharp
using KeyManager.Client;

var km = KeyManagerClient.FromBase64Seed("myapp", base64Seed, host, port); // 등록 시 받은 시드; 예: "192.168.0.10", 9713
string apiKey = await km.GetAsync("openai");                               // 평문 값
// var keys = await km.ListAsync();                                        // 접근 가능한 키 목록
```
서버에 닿지 못하거나, 권한이 없거나, TLS 핀이 불일치하면 `KeyManagerException`이 발생한다.

**CLI로 빠르게 확인 (샘플 앱)**
```sh
# SampleClient [--host H] [--port P] <clientName> <base64Seed> <list|get|getGroup> [key]
dotnet run --project samples/KeyManager.SampleClient -- --host 192.168.0.10 --port 9713 myapp <base64Seed> list
dotnet run --project samples/KeyManager.SampleClient -- myapp <base64Seed> get openai
dotnet run --project samples/KeyManager.SampleClient -- myapp <base64Seed> getGroup LsOpenApi
```
엔드포인트 해석: `--host`/`--port` 플래그 > `KM_HOST`/`KM_PORT` 환경변수 > 기본 `127.0.0.1:9713`.

## 그룹(계층) 키 — 콜론(`:`) 규칙

`LsOpenApi:Simulation:AppKey` 처럼 `:`로 계층을 표현하면 그룹으로 다룰 수 있다(저장 구조는 평평한 그대로). 와일드카드(`*`)는 없고 **이름이 곧 그 서브트리**를 뜻한다.

- **권한**: 허용목록에 `LsOpenApi` 를 넣으면 `LsOpenApi` 와 그 하위 전체 허가. `LsOpenApi:Simulation` 이면 그 서브트리만. `LsOpenApi:AppKey` 면 그 잎 하나만. (GUI에선 트리에서 그룹 노드를 체크)
- **매칭은 세그먼트 경계**: `LsOpenApi:Sim` 은 `LsOpenApi:Simulation:*` 을 매칭하지 않음.
- **그룹 추가는 자동 포함**: 그룹으로 허가하면 이후 그 그룹에 추가되는 키도 자동 접근 가능.

**그룹 통째로 가져오기 → `IConfiguration`에 바로 주입**
```csharp
var km   = KeyManagerClient.FromBase64Seed("myapp", base64Seed);
var dict = await km.GetGroupAsync("LsOpenApi");        // 권한 있는 하위 키 전부 {전체이름:값}
var cfg  = new ConfigurationBuilder()
              .AddInMemoryCollection(dict!).Build();   // 콜론 이름이 중첩 섹션으로 매핑됨
var opt  = cfg.GetSection("LsOpenApi").Get<LsOpenApiOptions>();
```
넓게 요청해도 권한 밖 키는 반환되지 않는다(요청 prefix ∩ 허가 범위).

## 배포

```sh
powershell ./publish.ps1
```
`release/`에 모은다:
- `release/KeyManager.Server/` — 2단계 상주 TCP/TLS 서버. `KeyManager.Server.exe` 실행(최초 실행 시 admin 토큰 `A` 1회 출력).
- `release/KeyManager.MasterGui/` — 2단계 비상주 관리 GUI. `KeyManager.MasterGui.exe` 실행.
- `release/KeyManager.App/` — 1단계 Named Pipe 에이전트(로컬, 참고용 보존). `KeyManager.App.exe` 실행.
- `release/sdk/` — 소비 앱이 참조할 `KeyManager.Client.dll` + `KeyManager.Protocol.dll`.

프레임워크 의존 빌드라 대상 PC에 .NET 10 런타임이 필요하다.

## 1단계(MVP) 범위

로컬 전용. 클라우드 sync(zero-knowledge 하이브리드)·백업/복구는 2단계 예정
([docs/개발목적.md](docs/개발목적.md) §11, §15).

## 다음 플랜 (로드맵)

1단계는 Named Pipe 로컬 전용이다. 진행 상태:

1. **TCP 버전 — 구현/진행 중.** TCP/TLS 분리(`KeyManager.Server` 상주 서버 + `KeyManager.MasterGui` 비상주 관리 GUI)로 다른 머신·컨테이너·WSL의 소비 앱이 접속 가능. C1 봉투 모델(zero-knowledge 서버)·TOFU 핀 고정 TLS·admin 토큰 인증·시간 기반 코드의 시계 오차 허용을 사용. [docs/tcp-architecture.md](docs/tcp-architecture.md) 참고.
2. **클라우드 버전 — 향후.** zero-knowledge 하이브리드(문서 §11). 클라우드는 **암호문 금고만 동기화**하고 복호화는 로컬에 유지. sync 백엔드·충돌/버전 관리·백업/복구(복구 코드) 추가.
