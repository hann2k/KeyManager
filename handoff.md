# KeyManager — 핸드오프 문서

> 다음 세션/에이전트를 위한 인수인계. 현재 상태 + 다음 작업(“TCP 버전” 아키텍처 분리)을 정리한다.
> 설계 배경: [docs/개발목적.md](docs/개발목적.md) · [docs/developmentpurpose.md](docs/developmentpurpose.md)

---

## 1. 프로젝트 개요

로컬 시크릿 브로커. 비밀 데이터를 암호화 저장하고, 인가된 로컬 앱이 질의하면 해당 값만 안전하게 배급한다.
복호화 능력은 에이전트에만 있고, 소비 앱·클라우드는 평문을 갖지 않는다(zero-knowledge 지향).

## 2. 현재 상태 (완료)

### 2.1 아키텍처 (현재 = 단일 상주 앱)
지금은 **하나의 트레이 앱**(`KeyManager.App`)이 전부를 겸한다:
금고 보관 + `Kd` 보유 + **Named Pipe 브로커** + 관리 GUI + 트레이 상주.

### 2.2 완료 기능
- **암호화 금고**: 항목별 AES-256-GCM(이름·값·설명 모두 암호화). KDF는 **Argon2id**(구 PBKDF2 vault 호환, 암호 변경 시 자동 이관). salt/파라미터만 저장, `Kd`는 유도.
- **VaultStore**: unlock/lock(`Kd`는 해제 동안만 상주), 유휴 자동잠금 **비활성**(수동 잠금만), 키 CRUD, 클라이언트 등록/권한편집/삭제, 키·그룹 설명, 그룹 삭제, **마스터 암호 변경**(전체 재암호화·원자적).
- **Named Pipe 브로커**(`PipeBrokerServer` + `BrokerService`): §9 프로토콜(`getKey`/`listKeys`/`getGroup`), ACL 현재 사용자 제한, 클라 PID 확인.
- **Protocol**: 와이어 메시지, 프레이밍(길이 프리픽스 JSON), 전송 암호(authCode/sessionKey, 도메인 분리).
- **Client SDK**(`KeyManagerClient`): `GetAsync`/`ListAsync`/`GetGroupAsync`.
- **그룹(콜론) 키**: `KeyAccess` 세그먼트 경계 매칭, 와일드카드 없음, 그룹 권한.
- **WinForms 트레이 앱**: 상주·더블클릭 관리창·시작 시 자동 표시. 키 탭(트리 + `●` 마커, 추가/값변경/설명/키삭제/그룹삭제), 클라이언트 탭(등록/권한편집/삭제, 시드 1회 표시), Language 탭. 마스터 암호 생성/해제/변경 + **경고 문구**. **다국어**(Loc, 영어 기본+한국어), 최초 실행 언어 선택(settings.json). 종료는 `Environment.Exit`.
- **설명**: 키·그룹 모두 암호화, 트리 툴팁.
- **테스트 43개**(암호·vault·프로토콜 E2E·그룹·수명주기·설명·암호변경·Argon2 이관).
- **배포**: `publish.ps1` → `release/`(에이전트 + `sdk/`), SampleClient 제외.
- **개발 에이전트**: `.claude/agents/`(backend/winform/i18n/misc) + [docs/agents.md](docs/agents.md).

### 2.3 주요 파일 맵
```
src/KeyManager.Protocol/  ProtocolConstants, WireMessages, Framing, TransportCrypto
src/KeyManager.Core/      Crypto/{KeyDerivation(+Pbkdf2+resolver), Argon2idKeyDerivation, AeadBox},
                          Model/VaultFile, VaultStore, KeyAccess, BrokerService, PipeBrokerServer
src/KeyManager.Client/    KeyManagerClient
src/KeyManager.App/       Program, AppPaths, AppSettings, Loc, TrayContext, MainForm, Dialogs,
                          MasterPasswordForm, ChangeMasterPasswordForm, DescriptionForm,
                          LanguageSelectForm, KeyTreeBuilder
samples/KeyManager.SampleClient/
tests/KeyManager.Core.Tests/
docs/  README.md / README.kr.md  publish.ps1
```

## 3. 빌드 / 테스트 / 배포
```sh
dotnet build KeyManager.slnx
dotnet test tests/KeyManager.Core.Tests      # 43 통과
powershell ./publish.ps1                      # release/ 재생성
```
- .NET 10 (net10.0 / net10.0-windows). Argon2: NuGet `Konscious.Security.Cryptography.Argon2`(Core).
- 실행 중이면 파일 잠금 → publish 전 `KeyManager.App` 프로세스 종료 필요(스크립트가 처리).

## 4. Git 상태
- 브랜치 `master`, 원격 `origin` = github.com/hann2k/KeyManager.git
- **미푸시 커밋 3개**(마스터 암호 변경 / Argon2id / subagent+경고문구). 필요 시 push.
- `.gitignore`: `release/`는 커밋 대상. `.claude/*` 제외하되 `.claude/agents/`만 추적. `bin/`,`obj/` 제외.

---

## 5. 다음 작업 — “TCP 버전” (아키텍처 분리)

로드맵 1번. 현재의 **단일 상주 앱**을 **두 컴포넌트로 분리**한다:
편집 담당 **마스터 GUI**(비상주) + 금고 보관 담당 **TCP 서버**(상주).

> **복호화 모델 확정 = C (봉투 / envelope).** 서버는 마스터키(`Kd`)를 갖지 않는다.
> 복호화는 소비 앱이 자기 시드 `S`로 수행하되, 권한(allowedKeys)은 유지된다.
> - 마스터 GUI(=`Kd` 보유)가 금고를 편집한 뒤, **소비 앱마다 "그 앱의 허용 키만 모아 그 앱의 `S`로 봉인한 봉투"**를 만든다.
> - 서버는 (a) 마스터 금고(`Kd` 암호 — 서버는 못 엶) + (b) 소비 앱별 봉투를 **보관·전달만** 한다.
> - 소비 앱은 자기 봉투를 받아 `S`로 열어 **자기 몫만** 얻는다. **마스터키는 안 가짐.**
> - **권한 강제 = 봉투 생성 시점(마스터 GUI)** — 서버는 키별 권한을 몰라도 된다.
> - 마스터 GUI의 추가 복잡도는 여기 하나: **push할 때 영향받은 소비 앱 봉투를 재생성**.

### 5.1 마스터 GUI (비상주, 관리 전용)
- **실행할 때만 작동. 창 X 누르면 종료. 메모리 상주 없음**(트레이 대기 X).
- **Named Pipe 로컬 시절의 관리 GUI를 전부 포함**(키/클라이언트 편집 UI).
- 동작:
  - **등록**: 신규 키를 **암호화**해서
  - **수정**: 수정된 키를 **암호화**해서
  - **삭제**: 금고에서 삭제하고
  - **공통**: 위 작업 후 **금고 전체를 서버로 전달**
- 즉 마스터 GUI가 `Kd`(마스터 암호)를 갖고 편집·암호화한 뒤, **금고 전체를 서버로 push**.

### 5.2 TCP 서버 (상주, 금고 보관 전용)
- **Named Pipe 로컬 시절의 “키 List GUI”만 포함** — 뭐가 있는지 목록만 확인(읽기 전용).
- **암호화된 금고 역할만** 한다.
- 마스터 GUI나 소비 앱이 **요청하면 금고를 내줌**.
- **등록/수정/삭제는 여기서 안 함 — 마스터 GUI를 통해서만**.
- 트레이 우클릭 메뉴:
  - **Key 목록 보기**
  - **종료**

### 5.3 신규 프로토콜
- **금고 송수신**(vault send/receive): 마스터 GUI ↔ 서버(및 서버 → 소비 앱) 간 **암호화된 금고 전체**를 주고받는 프로토콜.

### 5.4 재사용 가능한 기존 자산
- `VaultStore`/`AeadBox`/KDF: 마스터 GUI의 편집·암호화에 그대로.
- 관리 폼들(`MainForm`, `Dialogs`, `MasterPasswordForm`, `ChangeMasterPasswordForm`, `KeyTreeBuilder`, `Loc` 등): 마스터 GUI로 이동.
- 읽기 전용 “키 List”: 트리에서 이름만 보여주던 로직 재사용(TCP 서버 트레이 창).
- `ISecretTransport` 추상화(설계 §3/§7): TCP 전송 어댑터를 여기에 얹기.

---

## 6. 결정 사항 (모두 확정)

### 복호화 모델 = C1 봉투
- **복호화** = 소비 앱(자기 `S`). 서버는 마스터키 없음.
- **마스터키(`Kd`)** = 마스터 GUI 실행 시 **마스터 암호 입력으로 활성화**. **GUI에 저장하지 않음**(salt/params만 저장, `Kd`는 실행 중 메모리에만) — 현 원칙 유지.
- **권한(allowedKeys) 강제** = 봉투 생성 시점(마스터 GUI).
- **봉투 포맷 = C1**: 소비 앱별로 "그 앱의 허용 키 값 묶음"을 그 앱의 `S`로 통째 봉인. (값 중복 허용 — 데이터 적음)
- **기존 §9 per-key 브로커(`getKey`)** = "금고/봉투 송수신"으로 대체.

### 전송 / 인증
- **TCP + TLS, 네트워크 개방**(다른 머신 접근 가능).
- **소비 앱**: 정해진 **자기 봉투만** 수신. 시드 `S` 기반 `authCode`로 서버에 인증.
- **마스터 GUI**: 서버에 push/pull 시 마스터 측 인증 필요.

### 마스터 GUI 편집 흐름
서버에서 마스터 금고 **pull → `Kd`로 복호화 → 편집(등록/수정/삭제) → (마스터 금고 + 재생성 봉투) push**.

### 구현 시 유의 (다음 세션)
- **TLS 인증서 프로비저닝**(자체 서명 등)과 소비 앱/마스터 GUI의 서버 신뢰 방법.
- **push 권한**: 마스터 금고는 `Kd` 암호라 위조는 불가하지만, 덮어쓰기/DoS 방지용 마스터 측 인증 필요.
- **프로토콜 3종**: (a) 마스터 GUI ↔ 서버 금고 pull/push, (b) 소비 앱 → 서버 봉투 fetch, (c) 서버 트레이용 키 목록 조회(읽기전용).

> 진행: backend가 금고/봉투 송수신 + TCP/TLS 서버 + 봉투 생성 로직, winform이 마스터 GUI 분리와 서버 트레이(키 List 읽기전용), i18n이 문자열, misc가 프로젝트 분리(새 서버·GUI 프로젝트)/빌드 담당.

---

## 7. 개발 진행 방식
[docs/agents.md](docs/agents.md)의 도메인별 subagent(backend/winform/i18n/misc)로 분담, 메인이 오케스트레이션 + build→publish→commit + 문서화. (subagent는 세션 시작 시 로드되므로, 새로 만든 정의를 쓰려면 세션 재시작 필요.)
