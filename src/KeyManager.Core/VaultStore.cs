using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyManager.Core.Crypto;
using KeyManager.Core.Model;

namespace KeyManager.Core;

/// <summary>브로커가 쓰는 소비 앱의 복호화된 뷰.</summary>
public sealed record ClientView(string Name, byte[] Seed, HashSet<string> AllowedKeys, string? BoundProcessPath);

/// <summary>
/// 암호화 vault의 중심. Kd는 unlock~lock 동안만 상주하고 유휴 시 자동 소거(설계 §8).
/// 값은 상주시키지 않고 요청 1건마다 그때그때 복호화한다. 이름은 목록 표시를 위해
/// unlock 시 복호화해 인덱스로 보관(이름은 값이 아니므로 허용).
/// 모든 가변 상태는 단일 락으로 보호(브로커 백그라운드 스레드 + GUI 동시 접근).
/// </summary>
public sealed class VaultStore : IDisposable
{
    private const string VerifierMagic = "KeyManager-verify-v1";

    private static readonly JsonSerializerOptions FileJson = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly IKeyDerivation _kdf;
    private readonly Timer _idleTimer;

    private VaultFile _file;
    private byte[]? _kd;
    private DateTimeOffset _lastActivity;

    // unlock 동안만 유효한 캐시
    private Dictionary<string, VaultEntryRecord>? _entriesByName;
    private Dictionary<string, ClientRecord>? _clientsByName;
    private Dictionary<string, GroupMetaRecord>? _groupMetaByPath;

    public TimeSpan AutoLockAfter { get; set; }

    /// <summary>잠금/해제 등 상태가 바뀔 때 발생(UI 갱신용).</summary>
    public event Action? StateChanged;

    private VaultStore(string path, VaultFile file, IKeyDerivation kdf, TimeSpan autoLockAfter)
    {
        _path = path;
        _file = file;
        _kdf = kdf;
        AutoLockAfter = autoLockAfter;
        _idleTimer = new Timer(_ => CheckIdle(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
    }

    public bool IsUnlocked
    {
        get { lock (_gate) return _kd is not null; }
    }

    public bool FileExists => File.Exists(_path);

    // ---- 생성 / 로드 ----------------------------------------------------

    public static VaultStore CreateNew(string path, string masterPassword, TimeSpan autoLockAfter, IKeyDerivation? kdf = null)
    {
        kdf ??= KeyDerivations.Default;
        var kdfParams = kdf.CreateParameters();
        byte[] kd = kdf.DeriveKey(masterPassword, kdfParams);
        var file = new VaultFile
        {
            Kdf = kdfParams,
            Verifier = AeadBox.SealString(kd, VerifierMagic),
        };
        var store = new VaultStore(path, file, kdf, autoLockAfter);
        store.SaveFile(file);
        CryptographicOperations.ZeroMemory(kd);
        return store;
    }

    public static VaultStore Load(string path, TimeSpan autoLockAfter, IKeyDerivation? kdf = null)
    {
        kdf ??= KeyDerivations.Default;
        byte[] bytes = File.ReadAllBytes(path);
        VaultFile file = JsonSerializer.Deserialize<VaultFile>(bytes, FileJson)
            ?? throw new InvalidDataException("vault 파일을 읽을 수 없습니다.");
        return new VaultStore(path, file, kdf, autoLockAfter);
    }

    // ---- 잠금 / 해제 ----------------------------------------------------

    /// <summary>마스터 암호로 Kd를 유도·검증하고 unlock. 실패 시 false.</summary>
    public bool Unlock(string masterPassword)
    {
        lock (_gate)
        {
            // 파일에 기록된 알고리즘으로 유도(구 PBKDF2 vault도 그대로 해제).
            byte[] kd = KeyDerivations.Resolve(_file.Kdf.Algorithm).DeriveKey(masterPassword, _file.Kdf);
            try
            {
                string magic = AeadBox.OpenString(kd, _file.Verifier);
                if (magic != VerifierMagic)
                {
                    CryptographicOperations.ZeroMemory(kd);
                    return false;
                }
            }
            catch (CryptographicException)
            {
                CryptographicOperations.ZeroMemory(kd); // 잘못된 암호 → 인증 태그 불일치
                return false;
            }

            _kd = kd;
            BuildIndexes(kd);
            Touch();
        }
        StateChanged?.Invoke();
        return true;
    }

    public void Lock()
    {
        bool changed;
        lock (_gate)
        {
            changed = _kd is not null;
            if (_kd is not null) CryptographicOperations.ZeroMemory(_kd);
            _kd = null;
            _entriesByName = null;
            _clientsByName = null;
            _groupMetaByPath = null;
        }
        if (changed) StateChanged?.Invoke();
    }

    private void CheckIdle()
    {
        bool shouldLock;
        lock (_gate)
        {
            shouldLock = _kd is not null
                && AutoLockAfter > TimeSpan.Zero
                && DateTimeOffset.UtcNow - _lastActivity > AutoLockAfter;
        }
        if (shouldLock) Lock();
    }

    private void Touch() => _lastActivity = DateTimeOffset.UtcNow;

    private void BuildIndexes(byte[] kd)
    {
        _entriesByName = new Dictionary<string, VaultEntryRecord>(StringComparer.Ordinal);
        foreach (var e in _file.Entries)
            _entriesByName[AeadBox.OpenString(kd, e.Name)] = e;

        _clientsByName = new Dictionary<string, ClientRecord>(StringComparer.Ordinal);
        foreach (var c in _file.Clients)
            _clientsByName[AeadBox.OpenString(kd, c.Name)] = c;

        _groupMetaByPath = new Dictionary<string, GroupMetaRecord>(StringComparer.Ordinal);
        foreach (var g in _file.Groups)
            _groupMetaByPath[AeadBox.OpenString(kd, g.Path)] = g;
    }

    private byte[] RequireUnlocked()
        => _kd ?? throw new InvalidOperationException("잠금 상태입니다. 먼저 unlock 하세요.");

    /// <summary>
    /// 마스터 암호 변경. 현재 암호를 재검증한 뒤 모든 암호문을 새 Kd(새 salt)로 다시 감싼다.
    /// 소비 앱의 시드 S 값은 그대로라 재설정 불필요(설계 §4의 두 키 분리).
    /// 재암호화본을 메모리에서 완성한 뒤 원자적으로 저장 → 중간 실패 시 기존 vault 유지.
    /// 현재 암호가 틀리면 false.
    /// </summary>
    public bool ChangeMasterPassword(string currentPassword, string newPassword)
    {
        lock (_gate)
        {
            byte[] oldKd = RequireUnlocked();

            // 현재 암호 재검증(도난 세션 오용 방지). 기존 알고리즘으로 유도.
            byte[] check = KeyDerivations.Resolve(_file.Kdf.Algorithm).DeriveKey(currentPassword, _file.Kdf);
            bool valid;
            try { valid = AeadBox.OpenString(check, _file.Verifier) == VerifierMagic; }
            catch (CryptographicException) { valid = false; }
            CryptographicOperations.ZeroMemory(check);
            if (!valid) return false;

            // 새 키 + 파라미터(새 salt)
            var newParams = _kdf.CreateParameters();
            byte[] newKd = _kdf.DeriveKey(newPassword, newParams);

            try
            {
                var newFile = new VaultFile
                {
                    Version = _file.Version,
                    Kdf = newParams,
                    Verifier = AeadBox.SealString(newKd, VerifierMagic),
                    Entries = _file.Entries.Select(e => new VaultEntryRecord
                    {
                        Id = e.Id,
                        Name = Rewrap(oldKd, newKd, e.Name),
                        Value = Rewrap(oldKd, newKd, e.Value),
                        Description = e.Description is null ? null : Rewrap(oldKd, newKd, e.Description),
                        CreatedAt = e.CreatedAt,
                        UpdatedAt = e.UpdatedAt,
                    }).ToList(),
                    Clients = _file.Clients.Select(c => new ClientRecord
                    {
                        Id = c.Id,
                        Name = Rewrap(oldKd, newKd, c.Name),
                        Seed = Rewrap(oldKd, newKd, c.Seed),
                        AllowedKeys = Rewrap(oldKd, newKd, c.AllowedKeys),
                        BoundProcessPath = c.BoundProcessPath,
                        CreatedAt = c.CreatedAt,
                    }).ToList(),
                    Groups = _file.Groups.Select(g => new GroupMetaRecord
                    {
                        Path = Rewrap(oldKd, newKd, g.Path),
                        Description = Rewrap(oldKd, newKd, g.Description),
                    }).ToList(),
                };

                SaveFile(newFile); // 원자적 교체. 실패 시 예외 → 아래 catch에서 정리

                // 커밋: 메모리 상태 교체
                CryptographicOperations.ZeroMemory(oldKd);
                _kd = newKd;
                _file = newFile;
                BuildIndexes(newKd);
                Touch();
            }
            catch
            {
                CryptographicOperations.ZeroMemory(newKd);
                throw;
            }
        }
        StateChanged?.Invoke();
        return true;
    }

    private static SealedData Rewrap(byte[] oldKd, byte[] newKd, SealedData data)
        => AeadBox.Seal(newKd, AeadBox.Open(oldKd, data));

    // ---- 시크릿 항목 ----------------------------------------------------

    /// <summary>이름으로 추가/갱신. 같은 이름이면 값만 교체.</summary>
    public void AddOrUpdateSecret(string name, string value)
    {
        lock (_gate)
        {
            byte[] kd = RequireUnlocked();
            Touch();
            if (_entriesByName!.TryGetValue(name, out var existing))
            {
                existing.Value = AeadBox.SealString(kd, value);
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                var rec = new VaultEntryRecord
                {
                    Id = Guid.NewGuid().ToString("n"),
                    Name = AeadBox.SealString(kd, name),
                    Value = AeadBox.SealString(kd, value),
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                };
                _file.Entries.Add(rec);
                _entriesByName[name] = rec;
            }
            SaveFile(_file);
        }
        StateChanged?.Invoke();
    }

    /// <summary>요청 1건만 그때그때 복호화(설계 §8). 호출자는 사용 후 폐기 책임.</summary>
    public bool TryGetSecret(string name, out string value)
    {
        lock (_gate)
        {
            byte[] kd = RequireUnlocked();
            Touch();
            if (_entriesByName!.TryGetValue(name, out var rec))
            {
                value = AeadBox.OpenString(kd, rec.Value);
                return true;
            }
        }
        value = "";
        return false;
    }

    public bool DeleteSecret(string name)
    {
        bool removed;
        lock (_gate)
        {
            RequireUnlocked();
            Touch();
            if (_entriesByName!.Remove(name, out var rec))
            {
                _file.Entries.RemoveAll(e => e.Id == rec.Id);
                SaveFile(_file);
                removed = true;
            }
            else removed = false;
        }
        if (removed) StateChanged?.Invoke();
        return removed;
    }

    /// <summary>prefix 그룹(자신 + 하위)에 속한 모든 키를 삭제. 삭제된 개수 반환.</summary>
    public int DeleteGroup(string prefix)
    {
        int count;
        lock (_gate)
        {
            RequireUnlocked();
            Touch();
            var toRemove = _entriesByName!.Keys.Where(n => KeyAccess.InGroup(prefix, n)).ToList();
            foreach (var n in toRemove)
                if (_entriesByName.Remove(n, out var rec))
                    _file.Entries.RemoveAll(e => e.Id == rec.Id);
            count = toRemove.Count;

            // 그룹 설명도 함께 정리(고아 방지)
            var metaToRemove = _groupMetaByPath!.Keys.Where(p => KeyAccess.InGroup(prefix, p)).ToList();
            foreach (var p in metaToRemove)
                if (_groupMetaByPath.Remove(p, out var grec))
                    _file.Groups.Remove(grec);

            if (count > 0 || metaToRemove.Count > 0) SaveFile(_file);
        }
        if (count > 0) StateChanged?.Invoke();
        return count;
    }

    /// <summary>prefix 그룹에 속한 키 개수(삭제 전 확인용).</summary>
    public int CountGroup(string prefix)
    {
        lock (_gate)
        {
            RequireUnlocked();
            return _entriesByName!.Keys.Count(n => KeyAccess.InGroup(prefix, n));
        }
    }

    public IReadOnlyList<string> ListSecretNames()
    {
        lock (_gate)
        {
            RequireUnlocked();
            return _entriesByName!.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        }
    }

    // ---- 설명(description) ----------------------------------------------

    public string? GetSecretDescription(string name)
    {
        lock (_gate)
        {
            byte[] kd = RequireUnlocked();
            if (_entriesByName!.TryGetValue(name, out var rec) && rec.Description is not null)
                return AeadBox.OpenString(kd, rec.Description);
            return null;
        }
    }

    public void SetSecretDescription(string name, string? description)
    {
        lock (_gate)
        {
            byte[] kd = RequireUnlocked();
            Touch();
            if (!_entriesByName!.TryGetValue(name, out var rec))
                throw new InvalidOperationException($"존재하지 않는 키: {name}");
            rec.Description = string.IsNullOrEmpty(description) ? null : AeadBox.SealString(kd, description);
            rec.UpdatedAt = DateTimeOffset.UtcNow;
            SaveFile(_file);
        }
        StateChanged?.Invoke();
    }

    public string? GetGroupDescription(string path)
    {
        lock (_gate)
        {
            byte[] kd = RequireUnlocked();
            if (_groupMetaByPath!.TryGetValue(path, out var rec))
                return AeadBox.OpenString(kd, rec.Description);
            return null;
        }
    }

    public void SetGroupDescription(string path, string? description)
    {
        lock (_gate)
        {
            byte[] kd = RequireUnlocked();
            Touch();
            if (string.IsNullOrEmpty(description))
            {
                if (_groupMetaByPath!.Remove(path, out var existing))
                    _file.Groups.Remove(existing);
            }
            else if (_groupMetaByPath!.TryGetValue(path, out var rec))
            {
                rec.Description = AeadBox.SealString(kd, description);
            }
            else
            {
                var rec2 = new GroupMetaRecord
                {
                    Path = AeadBox.SealString(kd, path),
                    Description = AeadBox.SealString(kd, description),
                };
                _file.Groups.Add(rec2);
                _groupMetaByPath[path] = rec2;
            }
            SaveFile(_file);
        }
        StateChanged?.Invoke();
    }

    // ---- 소비 앱(클라이언트) --------------------------------------------

    /// <summary>새 클라이언트 등록. 랜덤 시드 S를 생성해 반환(등록 시 1회만 노출).</summary>
    public byte[] AddClient(string name, IEnumerable<string> allowedKeys, string? boundProcessPath = null)
    {
        byte[] seed = RandomNumberGenerator.GetBytes(Protocol.ProtocolConstants.SeedLengthBytes);
        lock (_gate)
        {
            byte[] kd = RequireUnlocked();
            Touch();
            if (_clientsByName!.ContainsKey(name))
                throw new InvalidOperationException($"이미 존재하는 클라이언트: {name}");

            string allowedJson = JsonSerializer.Serialize(allowedKeys.ToArray());
            var rec = new ClientRecord
            {
                Id = Guid.NewGuid().ToString("n"),
                Name = AeadBox.SealString(kd, name),
                Seed = AeadBox.Seal(kd, seed),
                AllowedKeys = AeadBox.SealString(kd, allowedJson),
                BoundProcessPath = boundProcessPath,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _file.Clients.Add(rec);
            _clientsByName[name] = rec;
            SaveFile(_file);
        }
        StateChanged?.Invoke();
        return seed;
    }

    /// <summary>시드 S는 그대로 두고 접근 허용 키 목록만 교체.</summary>
    public void UpdateClientAllowedKeys(string name, IEnumerable<string> allowedKeys)
    {
        lock (_gate)
        {
            byte[] kd = RequireUnlocked();
            Touch();
            if (!_clientsByName!.TryGetValue(name, out var rec))
                throw new InvalidOperationException($"존재하지 않는 클라이언트: {name}");
            string allowedJson = JsonSerializer.Serialize(allowedKeys.ToArray());
            rec.AllowedKeys = AeadBox.SealString(kd, allowedJson);
            SaveFile(_file);
        }
        StateChanged?.Invoke();
    }

    public bool DeleteClient(string name)
    {
        bool removed;
        lock (_gate)
        {
            RequireUnlocked();
            Touch();
            if (_clientsByName!.Remove(name, out var rec))
            {
                _file.Clients.RemoveAll(c => c.Id == rec.Id);
                SaveFile(_file);
                removed = true;
            }
            else removed = false;
        }
        if (removed) StateChanged?.Invoke();
        return removed;
    }

    public IReadOnlyList<string> ListClientNames()
    {
        lock (_gate)
        {
            RequireUnlocked();
            return _clientsByName!.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>브로커 검증용. 클라이언트의 시드·허용키를 복호화해 반환.</summary>
    public bool TryGetClient(string clientName, out ClientView? client)
    {
        lock (_gate)
        {
            byte[] kd = RequireUnlocked();
            Touch();
            if (_clientsByName!.TryGetValue(clientName, out var rec))
            {
                byte[] seed = AeadBox.Open(kd, rec.Seed);
                string allowedJson = AeadBox.OpenString(kd, rec.AllowedKeys);
                string[] allowed = JsonSerializer.Deserialize<string[]>(allowedJson) ?? [];
                client = new ClientView(clientName, seed,
                    new HashSet<string>(allowed, StringComparer.Ordinal), rec.BoundProcessPath);
                return true;
            }
        }
        client = null;
        return false;
    }

    // ---- 저장 -----------------------------------------------------------

    private void SaveFile(VaultFile file)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(file, FileJson);
        string tmp = _path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        // 원자적 교체(가능하면)
        if (File.Exists(_path)) File.Replace(tmp, _path, null);
        else File.Move(tmp, _path);
    }

    public void Dispose()
    {
        _idleTimer.Dispose();
        Lock();
    }
}
