# KeyManager Python SDK (소비 앱용, TCP 버전)

C# `KeyManager.Client`와 **와이어 프로토콜이 바이트 단위로 동일한** 파이썬 소비 앱 SDK입니다.
파이썬 프로젝트에서 KeyManager 서버에 붙어 자기 앱에 발급된 키를 안전하게 가져올 수 있습니다.

- 등록 시 받은 **공유 시드 S**로 서버에 TCP + TLS(자체서명 + TOFU pin)로 접속
- 자기 **봉투(envelope)** 를 fetch → 시드에서 유도한 `envKey`로 AES-256-GCM 복호화
- 결과를 인스턴스에 캐시 → 이후 조회는 네트워크 없이 로컬에서
- 마스터 키(Kd)는 절대 다루지 않음 (C# SDK와 동일한 신뢰 모델)

## 설치

```bash
pip install cryptography          # 유일한 런타임 의존성
```

패키지로 설치하려면:

```bash
pip install ./sdk/python          # 이 디렉터리
```

또는 `keymanager/` 폴더를 프로젝트에 그대로 복사해 써도 됩니다(순수 파이썬, `cryptography`만 필요).

## 사용법

```python
from keymanager import KeyManagerClient

# 마스터 GUI에서 등록하고 받은 base64 시드를 넣는다.
km = KeyManagerClient.from_base64_seed(
    "myapp",                       # 등록된 소비 앱 이름
    "AAECAwQF...==",               # base64 시드 S
    host="127.0.0.1",              # 서버 호스트
    port=9713,                     # 기본 9713
)

km.get("openai")                   # → "sk-..."  (권한 없으면 KeyManagerError)
km.list()                          # → ["LsOpenApi:base", "openai", ...] (정렬)
km.get_group("LsOpenApi")          # → {"LsOpenApi:base": "...", "LsOpenApi:key": "..."}
km.refresh()                       # 캐시 폐기 → 다음 조회 때 재fetch
```

시드를 원시 바이트로 가지고 있다면 생성자를 직접 씁니다:

```python
km = KeyManagerClient("myapp", seed_bytes, host="127.0.0.1")
```

### 예외

통신 실패 · 서버 거부(권한 없음) · pin 불일치 · 복호화 실패는 모두 `KeyManagerError`로 던집니다.

```python
from keymanager import KeyManagerClient, KeyManagerError
try:
    value = km.get("openai")
except KeyManagerError as e:
    ...
```

## TLS TOFU 신뢰(pin)

서버는 자체서명 인증서를 씁니다. 최초 접속 시 인증서의 SHA-256 thumbprint를 pin 파일에 각인(TOFU)하고,
이후에는 각인값과 일치할 때만 신뢰합니다(불일치 → `KeyManagerError`, 중간자 방어).

기본 pin 파일 경로:

| OS       | 경로                                                    |
|----------|---------------------------------------------------------|
| Windows  | `%APPDATA%\KeyManager\pinned-<host>.txt`                |
| 그 외    | `$XDG_CONFIG_HOME/keymanager/pinned-<host>.txt` (기본 `~/.config`) |

thumbprint 계산 방식은 C# SDK와 동일(인증서 DER의 소문자 hex SHA-256)합니다. 경로를 바꾸려면
`pin_file_path=` 인자를 넘기세요.

## 예제 CLI

`examples/sample_client.py`는 C# `KeyManager.SampleClient`의 파이썬판입니다.

```bash
python examples/sample_client.py myapp AAAA... list
python examples/sample_client.py myapp AAAA... get openai
python examples/sample_client.py --host 192.168.0.10 --port 9713 myapp AAAA... getGroup LsOpenApi
```

엔드포인트 해석 순서: `--host/--port` 플래그 > 환경변수 `KM_HOST`/`KM_PORT` > 기본 `127.0.0.1:9713`.

## 호환성

와이어 프로토콜(버전 1)은 C# `KeyManager.Protocol`과 일치합니다:

- 프레이밍: 4바이트 big-endian 길이 + UTF-8 JSON(PascalCase 필드)
- `authCode = HMAC-SHA256(S, "auth" ‖ nonce ‖ timeStep(8B BE) ‖ op ‖ clientName)`
- `envKey = HMAC-SHA256(S, "env")`, 봉투는 AES-256-GCM(12B IV, 16B tag)
- `timeStep = floor(unixSeconds / 30)`

이 값들은 실제 C# `KeyManager.Protocol` 구현으로 만든 골든 벡터 및 실서버 왕복 테스트로 검증했습니다.
