using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using KeyManager.Protocol;

namespace KeyManager.Core;

/// <summary>
/// §9 프로토콜의 서버측 검증·처리. 전송에 독립적이라 Named Pipe든 다른 전송이든 재사용.
///
/// 재전송 방어의 본질은 "연결마다 서버가 발급하는 랜덤 nonce"다. 공격자는 S가 없어
/// 새 nonce에 맞는 authCode를 만들 수 없으므로 캡처한 요청을 다른 연결에 재생할 수 없다.
/// timeStep(±1)과 nonce 1회성 캐시는 보조적 방어.
/// </summary>
public sealed class BrokerService
{
    private readonly VaultStore _store;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _usedNonces = new();

    public BrokerService(VaultStore store) => _store = store;

    public ServerResponse Handle(byte[] issuedNonce, ClientRequest req, string? clientProcessPath)
    {
        if (!_store.IsUnlocked)
            return ServerResponse.Fail("에이전트가 잠금 상태입니다.");

        // nonce 1회성(보조). 서버 발급이라 정상 흐름에선 항상 통과.
        string nonceKey = Convert.ToBase64String(issuedNonce);
        EvictOldNonces();
        if (!_usedNonces.TryAdd(nonceKey, DateTimeOffset.UtcNow))
            return ServerResponse.Fail("거부되었습니다.");

        // timeStep 신선도(±1 step)
        long serverStep = TransportCrypto.ComputeTimeStep(DateTimeOffset.UtcNow);
        if (Math.Abs(serverStep - req.TimeStep) > 1)
            return ServerResponse.Fail("거부되었습니다.");

        // 클라이언트 조회
        if (string.IsNullOrEmpty(req.Client) || !_store.TryGetClient(req.Client, out var client) || client is null)
            return ServerResponse.Fail("거부되었습니다.");

        // (선택) 실행파일 바인딩 검증
        if (!string.IsNullOrEmpty(client.BoundProcessPath))
        {
            if (clientProcessPath is null ||
                !string.Equals(Path.GetFullPath(clientProcessPath), Path.GetFullPath(client.BoundProcessPath), StringComparison.OrdinalIgnoreCase))
                return ServerResponse.Fail("거부되었습니다.");
        }

        // authCode 검증(요청 op/key까지 바인딩)
        byte[] expected = TransportCrypto.ComputeAuthCode(client.Seed, issuedNonce, req.TimeStep, req.Op, req.Key);
        byte[] provided;
        try { provided = Convert.FromBase64String(req.AuthCode); }
        catch { return ServerResponse.Fail("거부되었습니다."); }
        if (!TransportCrypto.ConstantTimeEquals(expected, provided))
            return ServerResponse.Fail("거부되었습니다.");

        // sessionKey = HMAC(S, "enc" ‖ nonce) — 전송하지 않고 여기서 유도
        byte[] sessionKey = TransportCrypto.DeriveSessionKey(client.Seed, issuedNonce);

        return req.Op switch
        {
            "get" => HandleGet(req, client, sessionKey),
            "list" => HandleList(client, sessionKey),
            "getGroup" => HandleGetGroup(req, client, sessionKey),
            _ => ServerResponse.Fail("지원하지 않는 요청입니다."),
        };
    }

    private ServerResponse HandleGet(ClientRequest req, ClientView client, byte[] sessionKey)
    {
        // 그룹 허가도 단일 키 접근을 허용한다(KeyAccess 규칙).
        if (string.IsNullOrEmpty(req.Key) || !KeyAccess.IsAllowed(client.AllowedKeys, req.Key))
            return ServerResponse.Fail("거부되었습니다."); // 권한 없음

        if (!_store.TryGetSecret(req.Key, out string value))
            return ServerResponse.Fail("키를 찾을 수 없습니다.");

        var payload = JsonSerializer.SerializeToUtf8Bytes(new GetResult { Value = value });
        return Seal(sessionKey, payload);
    }

    private ServerResponse HandleList(ClientView client, byte[] sessionKey)
    {
        // 실제 존재하는 키 중 이 클라가 접근 가능한 것들(그룹 허가 반영).
        string[] keys = _store.ListSecretNames()
            .Where(n => KeyAccess.IsAllowed(client.AllowedKeys, n))
            .OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new ListResult { Keys = keys });
        return Seal(sessionKey, payload);
    }

    private ServerResponse HandleGetGroup(ClientRequest req, ClientView client, byte[] sessionKey)
    {
        if (string.IsNullOrEmpty(req.Key))
            return ServerResponse.Fail("그룹명이 필요합니다.");

        // prefix 그룹에 속하면서 ∧ 권한이 있는 키만. 값은 그때그때 복호화.
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in _store.ListSecretNames())
        {
            if (!KeyAccess.InGroup(req.Key, name)) continue;
            if (!KeyAccess.IsAllowed(client.AllowedKeys, name)) continue;
            if (_store.TryGetSecret(name, out string value))
                entries[name] = value;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(new GroupResult { Entries = entries });
        return Seal(sessionKey, payload);
    }

    private static ServerResponse Seal(byte[] sessionKey, byte[] payload)
    {
        var (iv, tag, ct) = TransportCrypto.Seal(sessionKey, payload);
        return new ServerResponse
        {
            Ok = true,
            Iv = Convert.ToBase64String(iv),
            Tag = Convert.ToBase64String(tag),
            Payload = Convert.ToBase64String(ct),
        };
    }

    private void EvictOldNonces()
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddMinutes(-5);
        foreach (var kv in _usedNonces)
            if (kv.Value < cutoff)
                _usedNonces.TryRemove(kv.Key, out _);
    }
}
