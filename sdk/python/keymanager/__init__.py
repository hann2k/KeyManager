"""KeyManager 소비 앱용 Python SDK (TCP 버전).

C# ``KeyManager.Client``와 동일한 와이어 프로토콜을 구현한다. 등록 시 받은
공유 시드 S로 서버에 TCP+TLS로 붙어 자기 봉투(envelope)를 fetch·복호화해
인스턴스에 캐시한다. 마스터 키(Kd)는 절대 다루지 않는다.

    from keymanager import KeyManagerClient

    km = KeyManagerClient.from_base64_seed("myapp", "AAAA...", host="127.0.0.1")
    print(km.get("openai"))
    print(km.list())
    print(km.get_group("LsOpenApi"))
"""

from .client import (
    DEFAULT_TCP_PORT,
    PROTOCOL_VERSION,
    KeyManagerClient,
    KeyManagerError,
)

__all__ = [
    "KeyManagerClient",
    "KeyManagerError",
    "DEFAULT_TCP_PORT",
    "PROTOCOL_VERSION",
]

__version__ = "1.0.0"
