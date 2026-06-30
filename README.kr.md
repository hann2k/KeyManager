# KeyManager

[English](README.md) · **한국어**

로컬 시크릿 브로커. API 키 등 비밀 데이터를 암호화 저장하고, 인가된 로컬 앱이
Named Pipe로 질의하면 해당 값만 안전하게 배급한다. 설계 근거는 [docs/개발목적.md](docs/개발목적.md) 참고.

## 구조

| 프로젝트 | 타깃 | 역할 |
|---|---|---|
| `src/KeyManager.Protocol` | net10.0 | 공유 와이어 프로토콜·전송 암호(authCode/sessionKey, AES-GCM) |
| `src/KeyManager.Core` | net10.0-windows | KDF·vault 저장·브로커 로직·Named Pipe 서버 |
| `src/KeyManager.Client` | net10.0 | 소비 앱용 SDK (`KeyManagerClient`) |
| `src/KeyManager.App` | net10.0-windows | WinForms 트레이 에이전트(관리 UI) |
| `samples/KeyManager.SampleClient` | net10.0 | 콘솔 소비 앱 예시 |
| `tests/KeyManager.Core.Tests` | net10.0-windows | 단위 + Named Pipe E2E 테스트 |

## 보안 모델 (요약)

- **저장(①)**: 마스터 암호 → Argon2id(현재 1단계는 PBKDF2-SHA256) → `Kd`. 이름·값·시드·권한목록 전부 AES-256-GCM. `Kd`는 unlock~lock 동안만 상주(수동 잠금 시 소거). 유휴 자동 잠금은 기본 비활성.
- **전송(②)**: 연결마다 서버가 challenge nonce 발급. 소비 앱은 공유 시드 `S`로 `authCode`(인증)와 `sessionKey`(전송 암호)를 각자 유도. `S`·`sessionKey`·`Kd`는 와이어로 흐르지 않음.
- 소비 앱은 **자기 권한(allowedKeys) 내의 키만**, 전송 암호만 풀어 평문 획득. 마스터 키는 에이전트 밖으로 나가지 않음.

## 빌드 & 테스트

```sh
dotnet build KeyManager.slnx
dotnet test tests/KeyManager.Core.Tests
```

## 사용법 (GUI)

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

**호출 (두 줄)**
```csharp
using KeyManager.Client;

var km = KeyManagerClient.FromBase64Seed("myapp", base64Seed); // 등록 시 받은 시드
string apiKey = await km.GetAsync("openai");                   // 평문 값
// var keys = await km.ListAsync();                            // 접근 가능한 키 목록
```
에이전트가 잠겨 있거나 권한이 없으면 `KeyManagerException`이 발생한다.

**CLI로 빠르게 확인 (샘플 앱)**
```sh
dotnet run --project samples/KeyManager.SampleClient -- myapp <base64Seed> list
dotnet run --project samples/KeyManager.SampleClient -- myapp <base64Seed> get openai
```

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
- `release/KeyManager.App/` — 에이전트. `KeyManager.App.exe` 실행.
- `release/sdk/` — 소비 앱이 참조할 `KeyManager.Client.dll` + `KeyManager.Protocol.dll`.

프레임워크 의존 빌드라 대상 PC에 .NET 10 런타임이 필요하다.

## 1단계(MVP) 범위

로컬 전용. 클라우드 sync(zero-knowledge 하이브리드)·백업/복구·Argon2id 교체는 2단계 예정
([docs/개발목적.md](docs/개발목적.md) §11, §15).
