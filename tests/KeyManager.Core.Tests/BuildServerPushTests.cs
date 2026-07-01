using System.Text.Json;
using KeyManager.Core;
using KeyManager.Protocol;

namespace KeyManager.Core.Tests;

/// <summary>
/// BuildServerPush(설계 §6, C1 봉투)가 각 소비앱 봉투에 정확히 허용 키만·정확한 값을 담고,
/// 그 앱의 S로 유도한 envKey로 복호화되는지 검증한다.
/// </summary>
public class BuildServerPushTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"km-push-{Guid.NewGuid():n}.json");
    private readonly VaultStore _store;

    public BuildServerPushTests()
    {
        _store = VaultStore.CreateNew(_path, "pw", TimeSpan.FromMinutes(5));
        _store.Unlock("pw");
    }

    public void Dispose()
    {
        _store.Dispose();
        try { File.Delete(_path); } catch { }
    }

    private static Dictionary<string, string> OpenEnvelope(EnvelopeRecord env, byte[] seed)
    {
        byte[] envKey = TransportCrypto.DeriveEnvelopeKey(seed);
        byte[] plaintext = TransportCrypto.Open(envKey,
            Convert.FromBase64String(env.Iv),
            Convert.FromBase64String(env.Tag),
            Convert.FromBase64String(env.Ct));
        var content = JsonSerializer.Deserialize<EnvelopeContent>(plaintext)!;
        return content.Entries;
    }

    [Fact]
    public void Envelope_ContainsExactlyAllowedKeysWithValues()
    {
        _store.AddOrUpdateSecret("openai", "sk-1");
        _store.AddOrUpdateSecret("github", "ghp-2");
        _store.AddOrUpdateSecret("aws", "aws-3");
        byte[] seedA = _store.AddClient("app1", new[] { "openai", "aws" }); // github 제외

        ServerPushData push = _store.BuildServerPush();
        EnvelopeRecord env = push.Envelopes.Single(e => e.Client == "app1");
        var entries = OpenEnvelope(env, seedA);

        Assert.Equal(2, entries.Count);
        Assert.Equal("sk-1", entries["openai"]);
        Assert.Equal("aws-3", entries["aws"]);
        Assert.False(entries.ContainsKey("github"));
    }

    [Fact]
    public void GroupGrant_ExpandsToAllMatchingKeys()
    {
        _store.AddOrUpdateSecret("LsOpenApi:AppKey", "k1");
        _store.AddOrUpdateSecret("LsOpenApi:Simulation:AppKey", "k2");
        _store.AddOrUpdateSecret("Other:X", "x");
        byte[] seed = _store.AddClient("grpapp", new[] { "LsOpenApi" });

        ServerPushData push = _store.BuildServerPush();
        var entries = OpenEnvelope(push.Envelopes.Single(e => e.Client == "grpapp"), seed);

        Assert.Equal(2, entries.Count);
        Assert.Equal("k1", entries["LsOpenApi:AppKey"]);
        Assert.Equal("k2", entries["LsOpenApi:Simulation:AppKey"]);
        Assert.False(entries.ContainsKey("Other:X"));
    }

    [Fact]
    public void WrongSeed_CannotOpenEnvelope()
    {
        _store.AddOrUpdateSecret("openai", "sk-1");
        _store.AddClient("app1", new[] { "openai" });

        ServerPushData push = _store.BuildServerPush();
        EnvelopeRecord env = push.Envelopes.Single(e => e.Client == "app1");

        byte[] wrong = new byte[32];
        byte[] envKey = TransportCrypto.DeriveEnvelopeKey(wrong);
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() =>
            TransportCrypto.Open(envKey,
                Convert.FromBase64String(env.Iv),
                Convert.FromBase64String(env.Tag),
                Convert.FromBase64String(env.Ct)));
    }

    [Fact]
    public void EachClientGetsOwnEnvelope()
    {
        _store.AddOrUpdateSecret("a", "va");
        _store.AddOrUpdateSecret("b", "vb");
        byte[] seedA = _store.AddClient("appA", new[] { "a" });
        byte[] seedB = _store.AddClient("appB", new[] { "b" });

        ServerPushData push = _store.BuildServerPush();
        Assert.Equal(2, push.Envelopes.Count);

        var entA = OpenEnvelope(push.Envelopes.Single(e => e.Client == "appA"), seedA);
        var entB = OpenEnvelope(push.Envelopes.Single(e => e.Client == "appB"), seedB);
        Assert.Equal(new[] { "a" }, entA.Keys);
        Assert.Equal(new[] { "b" }, entB.Keys);
    }

    [Fact]
    public void MasterVaultJson_IsSerializedVaultFile()
    {
        _store.AddOrUpdateSecret("openai", "sk-1");
        ServerPushData push = _store.BuildServerPush();

        // 금고 JSON은 Kd 암호 상태 그대로 — 평문 값이 노출되지 않는다.
        Assert.DoesNotContain("sk-1", push.MasterVaultJson);
        Assert.Contains("\"Entries\"", push.MasterVaultJson);
    }
}
