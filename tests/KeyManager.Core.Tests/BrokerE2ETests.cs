using KeyManager.Client;
using KeyManager.Core;

namespace KeyManager.Core.Tests;

/// <summary>실제 Named Pipe + Client SDK로 end-to-end 검증.</summary>
public class BrokerE2ETests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"km-e2e-{Guid.NewGuid():n}.json");
    private readonly string _pipe = $"KeyManager.test.{Guid.NewGuid():n}";
    private VaultStore _store = null!;
    private PipeBrokerServer _server = null!;
    private byte[] _seed = null!;

    public Task InitializeAsync()
    {
        _store = VaultStore.CreateNew(_path, "pw", TimeSpan.FromMinutes(5));
        _store.Unlock("pw");
        _store.AddOrUpdateSecret("openai", "sk-secret-123");
        _store.AddOrUpdateSecret("github", "ghp_xyz");
        _seed = _store.AddClient("app1", new[] { "openai" }); // github 권한 없음

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

    [Fact]
    public async Task Get_ReturnsDecryptedValue()
    {
        var client = new KeyManagerClient("app1", _seed, _pipe);
        string value = await client.GetAsync("openai");
        Assert.Equal("sk-secret-123", value);
    }

    [Fact]
    public async Task List_ReturnsOnlyAllowedAndExisting()
    {
        var client = new KeyManagerClient("app1", _seed, _pipe);
        var keys = await client.ListAsync();
        Assert.Equal(new[] { "openai" }, keys); // github은 권한 없어 제외
    }

    [Fact]
    public async Task Get_UnauthorizedKey_Rejected()
    {
        var client = new KeyManagerClient("app1", _seed, _pipe);
        await Assert.ThrowsAsync<KeyManagerException>(() => client.GetAsync("github"));
    }

    [Fact]
    public async Task Get_WrongSeed_Rejected()
    {
        byte[] wrongSeed = new byte[32]; // 전부 0 — 잘못된 시드
        var client = new KeyManagerClient("app1", wrongSeed, _pipe);
        await Assert.ThrowsAsync<KeyManagerException>(() => client.GetAsync("openai"));
    }

    [Fact]
    public async Task Get_UnknownClient_Rejected()
    {
        var client = new KeyManagerClient("ghost", _seed, _pipe);
        await Assert.ThrowsAsync<KeyManagerException>(() => client.GetAsync("openai"));
    }

    [Fact]
    public async Task Get_WhenLocked_Rejected()
    {
        _store.Lock();
        try
        {
            var client = new KeyManagerClient("app1", _seed, _pipe);
            await Assert.ThrowsAsync<KeyManagerException>(() => client.GetAsync("openai"));
        }
        finally
        {
            _store.Unlock("pw"); // 다른 테스트 영향 없게 복구
        }
    }
}
