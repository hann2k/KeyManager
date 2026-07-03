"""소비 앱용 KeyManager 클라이언트 SDK — TCP 버전(설계 §7).

C# ``KeyManager.Client.KeyManagerClient``의 파이썬 포팅. 와이어 프로토콜(프레이밍,
HMAC 정규화, 봉투 봉인 키 유도, AES-256-GCM)이 바이트 단위로 동일해야 같은 서버와
통신할 수 있으므로, 아래 상수/알고리즘은 ``KeyManager.Protocol``과 정확히 맞춘다.

의존성: ``cryptography`` (AES-256-GCM). HMAC/SHA-256/TLS는 표준 라이브러리.
"""

from __future__ import annotations

import base64
import hashlib
import hmac
import json
import os
import socket
import ssl
import struct
import threading
import time
from typing import Dict, List, Mapping, Optional

from cryptography.hazmat.primitives.ciphers.aead import AESGCM

# ---- 프로토콜 상수 (KeyManager.Protocol.ProtocolConstants와 일치) ---------------

PROTOCOL_VERSION = 1
DEFAULT_TCP_PORT = 9713
TIME_STEP_SECONDS = 30
MAX_FRAME_BYTES = 1 << 24  # 16 MiB

_AUTH_LABEL = "auth"
_ENV_LABEL = "env"

# VaultOps
_OP_FETCH_ENVELOPE = "fetchEnvelope"

_GROUP_SEPARATOR = ":"


class KeyManagerError(Exception):
    """서버가 요청을 거부했거나 통신·복호화에 실패했을 때."""


# ---- 전송 계층 암호 (KeyManager.Protocol.TransportCrypto와 일치) ----------------


def _build_canonical(label: str, *chunks: bytes) -> bytes:
    """각 필드를 [4바이트 big-endian 길이 ‖ 내용]으로 이어붙여 경계 혼동을 막는다.

    첫 인자(label)는 문자열, 나머지는 바이트 청크. C# ``BuildCanonical``과 동일.
    """
    out = bytearray()
    out += struct.pack(">I", len(label.encode("utf-8")))
    out += label.encode("utf-8")
    for c in chunks:
        out += struct.pack(">I", len(c))
        out += c
    return bytes(out)


def _compute_time_step(now_unix: Optional[float] = None) -> int:
    if now_unix is None:
        now_unix = time.time()
    return int(now_unix) // TIME_STEP_SECONDS


def _compute_auth_code(seed: bytes, nonce: bytes, time_step: int, op: str, key: str) -> bytes:
    """요청을 인증하고 (op, key)까지 바인딩하는 코드.

    HMAC-SHA256(S, "auth" ‖ nonce ‖ timeStep(8B BE) ‖ op ‖ key).
    """
    message = _build_canonical(
        _AUTH_LABEL,
        nonce,
        struct.pack(">q", time_step),  # Int64 big-endian
        op.encode("utf-8"),
        (key or "").encode("utf-8"),
    )
    return hmac.new(seed, message, hashlib.sha256).digest()


def _derive_envelope_key(seed: bytes) -> bytes:
    """봉투 봉인/해제 키(AES-256). envKey = HMAC(S, "env"). nonce 무관."""
    return hmac.new(seed, _build_canonical(_ENV_LABEL), hashlib.sha256).digest()


def _aes_gcm_open(key: bytes, iv: bytes, tag: bytes, ciphertext: bytes) -> bytes:
    """AES-256-GCM 복호화(AAD 없음). 인증 실패 시 예외.

    cryptography의 AESGCM은 ciphertext‖tag를 함께 받는다.
    """
    return AESGCM(key).decrypt(iv, ciphertext + tag, None)


# ---- 길이 프리픽스 프레이밍 (KeyManager.Protocol.Framing와 일치) ----------------


def _read_exactly(sock: socket.socket, count: int) -> bytes:
    buf = bytearray()
    while len(buf) < count:
        chunk = sock.recv(count - len(buf))
        if not chunk:
            raise KeyManagerError("상대가 연결을 닫았습니다.")
        buf += chunk
    return bytes(buf)


def _read_message(sock: socket.socket) -> dict:
    header = _read_exactly(sock, 4)
    (length,) = struct.unpack(">i", header)
    if length < 0 or length > MAX_FRAME_BYTES:
        raise KeyManagerError(f"잘못된 프레임 길이: {length}")
    body = _read_exactly(sock, length)
    return json.loads(body.decode("utf-8"))


def _write_message(sock: socket.socket, message: Mapping) -> None:
    body = json.dumps(message, separators=(",", ":")).encode("utf-8")
    if len(body) > MAX_FRAME_BYTES:
        raise KeyManagerError(f"메시지가 너무 큽니다: {len(body)} bytes")
    sock.sendall(struct.pack(">i", len(body)) + body)


# ---- TLS TOFU thumbprint pin (KeyManager.Protocol.TlsSupport와 일치) ------------


def _default_pin_path(host: str) -> str:
    """기본 pin 파일 경로: <설정 디렉터리>/pinned-<host>.txt (설계 §7).

    Windows는 %APPDATA%\\KeyManager, 그 외는 $XDG_CONFIG_HOME/keymanager(기본 ~/.config).
    """
    if os.name == "nt":
        base = os.path.join(
            os.environ.get("APPDATA") or os.path.expanduser("~"), "KeyManager"
        )
    else:
        base = os.path.join(
            os.environ.get("XDG_CONFIG_HOME") or os.path.expanduser("~/.config"),
            "keymanager",
        )
    safe_host = "".join(c if (c.isalnum() or c in ".-_") else "_" for c in host)
    return os.path.join(base, f"pinned-{safe_host}.txt")


def _read_pin(pin_path: str) -> Optional[str]:
    try:
        with open(pin_path, "r", encoding="utf-8") as f:
            s = f.read().strip()
        return s or None
    except OSError:
        return None


def _write_pin(pin_path: str, thumbprint: str) -> None:
    try:
        d = os.path.dirname(pin_path)
        if d:
            os.makedirs(d, exist_ok=True)
        with open(pin_path, "w", encoding="utf-8") as f:
            f.write(thumbprint)
    except OSError:
        # pin 저장 실패는 치명적이지 않음(다음 접속 때 재각인 시도).
        pass


def _thumbprint(cert_der: bytes) -> str:
    """인증서 DER의 SHA-256 thumbprint를 소문자 hex로. C# ComputeThumbprint와 동일."""
    return hashlib.sha256(cert_der).hexdigest()


class KeyManagerClient:
    """소비 앱용 클라이언트 SDK — TCP 버전.

    최초 조회 시 서버에 TCP+TLS로 붙어 자기 봉투를 fetch·복호화해 인스턴스에 캐시한다.
    이후 조회는 네트워크 없이 로컬에서. 마스터 키(Kd)는 절대 다루지 않고, 봉투는 시드 S에서
    유도한 envKey로만 연다. API(``get``/``list``/``get_group``)는 서버 왕복 없이 캐시를 읽는다.

    스레드 안전: fetch는 락으로 1회만 수행되고 결과가 캐시된다.
    """

    def __init__(
        self,
        client_name: str,
        seed: bytes,
        host: str,
        port: int = DEFAULT_TCP_PORT,
        pin_file_path: Optional[str] = None,
        connect_timeout: float = 5.0,
    ):
        """
        :param client_name: 등록된 소비 앱 이름.
        :param seed: 등록 시 받은 공유 시드 S(원시 바이트).
        :param host: 서버 호스트(예: 127.0.0.1).
        :param port: TCP 포트(기본 9713).
        :param pin_file_path: TOFU thumbprint pin 파일. None이면 OS별 기본 경로.
        :param connect_timeout: 연결/핸드셰이크 타임아웃(초).
        """
        if not client_name:
            raise ValueError("client_name 필요")
        if not seed:
            raise ValueError("seed 필요")
        if not host:
            raise ValueError("host 필요")
        self._client_name = client_name
        self._seed = bytes(seed)
        self._host = host
        self._port = port
        self._pin_file_path = pin_file_path or _default_pin_path(host)
        self._connect_timeout = connect_timeout

        self._lock = threading.Lock()
        self._entries: Optional[Dict[str, str]] = None

    @classmethod
    def from_base64_seed(
        cls,
        client_name: str,
        base64_seed: str,
        host: str,
        port: int = DEFAULT_TCP_PORT,
        pin_file_path: Optional[str] = None,
        connect_timeout: float = 5.0,
    ) -> "KeyManagerClient":
        """base64로 보관한 시드로 생성하는 편의 생성자."""
        return cls(
            client_name,
            base64.b64decode(base64_seed),
            host,
            port,
            pin_file_path,
            connect_timeout,
        )

    # ---- 공개 API ------------------------------------------------------

    def get(self, key: str) -> str:
        """키 평문 값을 가져온다. 권한 없으면(봉투에 없으면) 예외."""
        entries = self._ensure_fetched()
        if key in entries:
            return entries[key]
        raise KeyManagerError(f"키를 찾을 수 없거나 권한이 없습니다: {key}")

    def list(self) -> List[str]:
        """이 클라이언트가 접근 가능한 키 이름 목록(정렬)."""
        entries = self._ensure_fetched()
        return sorted(entries.keys())

    def get_group(self, group_name: str) -> Dict[str, str]:
        """콜론(:) 그룹 prefix 아래의 모든 {전체이름: 값}을 반환(봉투에 이미 권한 필터링됨)."""
        if not group_name:
            raise ValueError("group_name 필요")
        entries = self._ensure_fetched()
        return {
            name: value
            for name, value in entries.items()
            if _in_group(group_name, name)
        }

    def refresh(self) -> None:
        """캐시를 버리고 다음 조회 때 봉투를 강제로 재fetch한다."""
        with self._lock:
            self._entries = None

    # ---- 봉투 fetch(최초 1회, 캐시) ------------------------------------

    def _ensure_fetched(self) -> Dict[str, str]:
        if self._entries is not None:
            return self._entries
        with self._lock:
            if self._entries is not None:  # 다른 대기 스레드가 먼저 채웠을 수 있음
                return self._entries
            self._entries = self._fetch_envelope()
            return self._entries

    def _fetch_envelope(self) -> Dict[str, str]:
        try:
            raw = socket.create_connection(
                (self._host, self._port), timeout=self._connect_timeout
            )
        except OSError as ex:
            raise KeyManagerError(
                f"서버에 연결할 수 없습니다({self._host}:{self._port})."
            ) from ex

        try:
            ssock = self._tls_wrap(raw)
        except KeyManagerError:
            raw.close()
            raise
        except Exception as ex:
            raw.close()
            raise KeyManagerError("TLS 핸드셰이크 실패(인증서 pin 불일치 가능).") from ex

        try:
            ssock.settimeout(self._connect_timeout)

            # ServerHello → nonce
            try:
                hello = _read_message(ssock)
            except KeyManagerError:
                raise
            except Exception as ex:
                raise KeyManagerError("서버 핸드셰이크 수신 실패.") from ex

            if hello.get("Protocol") != PROTOCOL_VERSION:
                raise KeyManagerError(f"프로토콜 버전 불일치: {hello.get('Protocol')}")
            nonce = base64.b64decode(hello["Nonce"])

            # fetchEnvelope 요청 (authCode의 key arg = clientName)
            time_step = _compute_time_step()
            auth_code = _compute_auth_code(
                self._seed, nonce, time_step, _OP_FETCH_ENVELOPE, self._client_name
            )
            req = {
                "Op": _OP_FETCH_ENVELOPE,
                "Client": self._client_name,
                "TimeStep": time_step,
                "AuthCode": base64.b64encode(auth_code).decode("ascii"),
            }
            try:
                _write_message(ssock, req)
            except KeyManagerError:
                raise
            except Exception as ex:
                raise KeyManagerError("요청 전송 실패.") from ex

            try:
                resp = _read_message(ssock)
            except KeyManagerError:
                raise
            except Exception as ex:
                raise KeyManagerError("서버 응답 수신 실패.") from ex
        finally:
            try:
                ssock.close()
            except OSError:
                pass

        if not resp.get("Ok"):
            raise KeyManagerError(resp.get("Error") or "거부되었습니다.")
        if resp.get("Iv") is None or resp.get("Tag") is None or resp.get("Ct") is None:
            raise KeyManagerError("봉투 응답이 비어 있습니다.")

        # envKey = HMAC(S, "env")로 봉투 복호화 → EnvelopeContent
        env_key = _derive_envelope_key(self._seed)
        try:
            plaintext = _aes_gcm_open(
                env_key,
                base64.b64decode(resp["Iv"]),
                base64.b64decode(resp["Tag"]),
                base64.b64decode(resp["Ct"]),
            )
        except Exception as ex:
            raise KeyManagerError("봉투 복호화 실패(시드 불일치?).") from ex

        content = json.loads(plaintext.decode("utf-8"))
        entries = content.get("Entries") or {}
        return dict(entries)

    def _tls_wrap(self, raw: socket.socket) -> ssl.SSLSocket:
        """self-signed 서버와 TLS 후 TOFU thumbprint pin 검증.

        체인/호스트명 검증은 끄고(self-signed), thumbprint pin만 신뢰 근거로 삼는다.
        """
        ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
        ctx.check_hostname = False
        ctx.verify_mode = ssl.CERT_NONE

        # SNI(server_hostname)는 서버가 단일 인증서라 값 자체는 무의미하지만 C#과 맞춰 host를 넣는다.
        # IP 리터럴이면 일부 파이썬에서 거부되므로 실패 시 SNI 없이 재시도.
        try:
            ssock = ctx.wrap_socket(raw, server_hostname=self._host)
        except (ValueError, ssl.SSLError):
            ssock = ctx.wrap_socket(raw)

        der = ssock.getpeercert(binary_form=True)
        if not der:
            raise KeyManagerError("서버 인증서를 받지 못했습니다.")
        remote = _thumbprint(der)

        pinned = _read_pin(self._pin_file_path)
        if pinned is None:
            # TOFU: 최초 접속 → 각인.
            _write_pin(self._pin_file_path, remote)
        elif not hmac.compare_digest(pinned, remote):
            raise KeyManagerError(
                "서버 인증서 thumbprint가 pin과 불일치합니다(중간자 가능). "
                f"pin={self._pin_file_path}"
            )
        return ssock


def _in_group(prefix: str, name: str) -> bool:
    """이름이 prefix 그룹(자신 또는 prefix: 하위)에 속하는가(세그먼트 경계, 와일드카드 없음)."""
    return name == prefix or name.startswith(prefix + _GROUP_SEPARATOR)
