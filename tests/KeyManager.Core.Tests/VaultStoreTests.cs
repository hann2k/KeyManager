using KeyManager.Core;

namespace KeyManager.Core.Tests;

public class VaultStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"km-test-{Guid.NewGuid():n}.json");

    private VaultStore CreateUnlocked(string pw = "master-pw")
    {
        var store = VaultStore.CreateNew(_path, pw, TimeSpan.FromMinutes(5));
        Assert.True(store.Unlock(pw));
        return store;
    }

    [Fact]
    public void Unlock_RejectsWrongPassword()
    {
        var store = VaultStore.CreateNew(_path, "right", TimeSpan.FromMinutes(5));
        Assert.False(store.Unlock("wrong"));
        Assert.True(store.Unlock("right"));
        Assert.True(store.IsUnlocked);
    }

    [Fact]
    public void Secret_AddGetListDelete()
    {
        using var store = CreateUnlocked();
        store.AddOrUpdateSecret("openai", "sk-123");
        store.AddOrUpdateSecret("github", "ghp_abc");

        Assert.True(store.TryGetSecret("openai", out var v));
        Assert.Equal("sk-123", v);
        Assert.Equal(new[] { "github", "openai" }, store.ListSecretNames());

        store.AddOrUpdateSecret("openai", "sk-999"); // update
        store.TryGetSecret("openai", out v);
        Assert.Equal("sk-999", v);

        Assert.True(store.DeleteSecret("github"));
        Assert.Equal(new[] { "openai" }, store.ListSecretNames());
    }

    [Fact]
    public void Secret_PersistsAcrossReload()
    {
        using (var store = CreateUnlocked("pw1"))
            store.AddOrUpdateSecret("k", "v");

        var reloaded = VaultStore.Load(_path, TimeSpan.FromMinutes(5));
        Assert.True(reloaded.Unlock("pw1"));
        Assert.True(reloaded.TryGetSecret("k", out var v));
        Assert.Equal("v", v);
        reloaded.Dispose();
    }

    [Fact]
    public void KeyAndGroup_SameName_AreIndependent()
    {
        // aoao 가 값을 가진 키이면서 동시에 그룹(aoao:group:bbb)인 경우.
        using var store = CreateUnlocked();
        store.AddOrUpdateSecret("aoao", "aaa");
        store.AddOrUpdateSecret("aoao:group:bbb", "bbb");

        Assert.Equal(2, store.CountGroup("aoao")); // 키 + 하위

        // 키 값만 삭제 → 그룹 하위는 남음
        Assert.True(store.DeleteSecret("aoao"));
        Assert.False(store.TryGetSecret("aoao", out _));
        Assert.True(store.TryGetSecret("aoao:group:bbb", out var v));
        Assert.Equal("bbb", v);
        Assert.Equal(1, store.CountGroup("aoao")); // 하위만 남음
    }

    [Fact]
    public void DeleteGroup_RemovesWholeSubtree()
    {
        using var store = CreateUnlocked();
        store.AddOrUpdateSecret("aoao", "aaa");
        store.AddOrUpdateSecret("aoao:group:bbb", "bbb");
        store.AddOrUpdateSecret("other", "x");

        int n = store.DeleteGroup("aoao");
        Assert.Equal(2, n);
        Assert.Equal(new[] { "other" }, store.ListSecretNames());
    }

    [Fact]
    public void Lock_ClearsAccess()
    {
        var store = CreateUnlocked();
        store.AddOrUpdateSecret("k", "v");
        store.Lock();
        Assert.False(store.IsUnlocked);
        Assert.Throws<InvalidOperationException>(() => store.ListSecretNames());
    }

    [Fact]
    public void Secret_Description_SetGetClear()
    {
        using var store = CreateUnlocked();
        store.AddOrUpdateSecret("k", "v");
        Assert.Null(store.GetSecretDescription("k"));
        store.SetSecretDescription("k", "my description");
        Assert.Equal("my description", store.GetSecretDescription("k"));
        store.SetSecretDescription("k", null);
        Assert.Null(store.GetSecretDescription("k"));
    }

    [Fact]
    public void GroupDescription_SetGetPersist()
    {
        using (var store = CreateUnlocked("pw1"))
        {
            store.AddOrUpdateSecret("g:a", "1");
            store.SetGroupDescription("g", "group note");
            Assert.Equal("group note", store.GetGroupDescription("g"));
        }
        var reloaded = VaultStore.Load(_path, TimeSpan.Zero);
        Assert.True(reloaded.Unlock("pw1"));
        Assert.Equal("group note", reloaded.GetGroupDescription("g"));
        reloaded.Dispose();
    }

    [Fact]
    public void DeleteGroup_AlsoRemovesGroupDescription()
    {
        using var store = CreateUnlocked();
        store.AddOrUpdateSecret("g:a", "1");
        store.SetGroupDescription("g", "note");
        store.DeleteGroup("g");
        Assert.Null(store.GetGroupDescription("g"));
    }

    [Fact]
    public void ChangeMasterPassword_ReEncrypts_PreservesEverything()
    {
        byte[] seed;
        using (var store = CreateUnlocked("old-pw"))
        {
            store.AddOrUpdateSecret("k", "v");
            store.SetSecretDescription("k", "desc");
            seed = store.AddClient("app1", new[] { "k" });

            Assert.True(store.ChangeMasterPassword("old-pw", "new-pw"));

            // 여전히 해제 상태, 데이터·시드 그대로
            Assert.True(store.TryGetSecret("k", out var v)); Assert.Equal("v", v);
            Assert.Equal("desc", store.GetSecretDescription("k"));
            Assert.True(store.TryGetClient("app1", out var view));
            Assert.Equal(seed, view!.Seed); // 시드 값 보존(소비 앱 재설정 불필요)
        }

        // 재로드: 옛 암호 실패, 새 암호 성공
        var reloaded = VaultStore.Load(_path, TimeSpan.Zero);
        Assert.False(reloaded.Unlock("old-pw"));
        Assert.True(reloaded.Unlock("new-pw"));
        Assert.True(reloaded.TryGetSecret("k", out var v2)); Assert.Equal("v", v2);
        Assert.True(reloaded.TryGetClient("app1", out var view2));
        Assert.Equal(seed, view2!.Seed);
        reloaded.Dispose();
    }

    [Fact]
    public void ChangeMasterPassword_WrongCurrent_ReturnsFalse()
    {
        using var store = CreateUnlocked("old-pw");
        store.AddOrUpdateSecret("k", "v");
        Assert.False(store.ChangeMasterPassword("wrong", "new-pw"));
        // 옛 암호는 여전히 유효
        store.Lock();
        Assert.True(store.Unlock("old-pw"));
    }

    [Fact]
    public void Client_AddAndResolve()
    {
        using var store = CreateUnlocked();
        store.AddOrUpdateSecret("openai", "sk");
        byte[] seed = store.AddClient("app1", new[] { "openai" });
        Assert.Equal(32, seed.Length);

        Assert.Equal(new[] { "app1" }, store.ListClientNames());
        Assert.True(store.TryGetClient("app1", out var view));
        Assert.NotNull(view);
        Assert.Equal(seed, view!.Seed);
        Assert.Contains("openai", view.AllowedKeys);
    }

    [Fact]
    public void Client_UpdateAllowedKeys_KeepsSeed()
    {
        using var store = CreateUnlocked();
        store.AddOrUpdateSecret("openai", "1");
        store.AddOrUpdateSecret("github", "2");
        byte[] seed = store.AddClient("app1", new[] { "openai" });

        store.UpdateClientAllowedKeys("app1", new[] { "github" });

        Assert.True(store.TryGetClient("app1", out var view));
        Assert.NotNull(view);
        Assert.Equal(seed, view!.Seed);                  // 시드 그대로
        Assert.DoesNotContain("openai", view.AllowedKeys); // 권한 교체됨
        Assert.Contains("github", view.AllowedKeys);
    }

    [Fact]
    public void Client_UpdateAllowedKeys_UnknownClient_Throws()
    {
        using var store = CreateUnlocked();
        Assert.Throws<InvalidOperationException>(() => store.UpdateClientAllowedKeys("ghost", new[] { "x" }));
    }

    [Fact]
    public void Client_DuplicateName_Throws()
    {
        using var store = CreateUnlocked();
        store.AddClient("app1", Array.Empty<string>());
        Assert.Throws<InvalidOperationException>(() => store.AddClient("app1", Array.Empty<string>()));
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }
}
