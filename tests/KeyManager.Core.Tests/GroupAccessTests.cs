using KeyManager.Client;
using KeyManager.Core;

namespace KeyManager.Core.Tests;

public class KeyAccessUnitTests
{
    [Theory]
    [InlineData("LsOpenApi", "LsOpenApi", true)]                       // 자신
    [InlineData("LsOpenApi", "LsOpenApi:AppKey", true)]               // 하위
    [InlineData("LsOpenApi", "LsOpenApi:Simulation:AppKey", true)]    // 깊은 하위
    [InlineData("LsOpenApi:Simulation", "LsOpenApi:AppKey", false)]   // 형제 그룹
    [InlineData("LsOpenApi:Sim", "LsOpenApi:Simulation:AppKey", false)] // 부분 문자열 매칭 금지(세그먼트 경계)
    [InlineData("LsOpenApi:AppKey", "LsOpenApi:AppKeyX", false)]      // 부분 매칭 금지
    public void Covers_RespectsSegmentBoundary(string grant, string key, bool expected)
        => Assert.Equal(expected, KeyAccess.Covers(grant, key));
}

/// <summary>그룹 조회·권한 E2E.</summary>
public class GroupE2ETests : IAsyncLifetime
{
    private static readonly string[] AllKeys =
    [
        "LsOpenApi:Simulation:WebSocketUrl",
        "LsOpenApi:Simulation:BaseUrl",
        "LsOpenApi:Simulation:AppSecret",
        "LsOpenApi:Simulation:AppKey",
        "LsOpenApi:Environment",
        "LsOpenApi:BaseUrl",
        "LsOpenApi:AppSecret",
        "LsOpenApi:AppKey",
        "LsOpenApi:AccountNumber",
    ];

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"km-grp-{Guid.NewGuid():n}.json");
    private readonly string _pipe = $"KeyManager.grp.{Guid.NewGuid():n}";
    private VaultStore _store = null!;
    private PipeBrokerServer _server = null!;

    public Task InitializeAsync()
    {
        _store = VaultStore.CreateNew(_path, "pw", TimeSpan.Zero);
        _store.Unlock("pw");
        foreach (var k in AllKeys) _store.AddOrUpdateSecret(k, $"val::{k}");
        _server = new PipeBrokerServer(new BrokerService(_store), _pipe);
        _server.Start();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _server.StopAsync();
        _store.Dispose();
        try { File.Delete(_path); } catch { }
    }

    private KeyManagerClient Client(string name) =>
        new(name, GetSeed(name), _pipe);

    private readonly Dictionary<string, byte[]> _seeds = new();
    private byte[] GetSeed(string name) => _seeds[name];

    private KeyManagerClient Register(string name, params string[] grants)
    {
        _seeds[name] = _store.AddClient(name, grants);
        return Client(name);
    }

    [Fact]
    public async Task GroupGrant_GetWholeGroup()
    {
        var km = Register("full", "LsOpenApi");
        var g = await km.GetGroupAsync("LsOpenApi");
        Assert.Equal(9, g.Count);
        Assert.Equal("val::LsOpenApi:AppKey", g["LsOpenApi:AppKey"]);
    }

    [Fact]
    public async Task GroupGrant_GetSubGroup()
    {
        var km = Register("full", "LsOpenApi");
        var g = await km.GetGroupAsync("LsOpenApi:Simulation");
        Assert.Equal(4, g.Count);
        Assert.All(g.Keys, k => Assert.StartsWith("LsOpenApi:Simulation:", k));
    }

    [Fact]
    public async Task PartialPrefix_ReturnsEmpty()
    {
        var km = Register("full", "LsOpenApi");
        var g = await km.GetGroupAsync("LsOpenApi:Sim"); // 세그먼트 경계 → 매칭 없음
        Assert.Empty(g);
    }

    [Fact]
    public async Task GroupGrant_AllowsSingleGet()
    {
        var km = Register("full", "LsOpenApi");
        Assert.Equal("val::LsOpenApi:AppKey", await km.GetAsync("LsOpenApi:AppKey"));
    }

    [Fact]
    public async Task SubGroupGrant_LimitsWiderRequest()
    {
        // Simulation 서브트리만 허가 → 상위 그룹을 요청해도 4개만.
        var km = Register("simonly", "LsOpenApi:Simulation");
        var g = await km.GetGroupAsync("LsOpenApi");
        Assert.Equal(4, g.Count);
        Assert.All(g.Keys, k => Assert.StartsWith("LsOpenApi:Simulation:", k));
        // 상위 키 단일 조회는 거부
        await Assert.ThrowsAsync<KeyManagerException>(() => km.GetAsync("LsOpenApi:AppKey"));
    }
}
