using KeyManager.Core;

namespace KeyManager.Core.Tests;

public class PipeServerLifecycleTests
{
    /// <summary>
    /// 종료 버그 회귀 테스트. 트레이 종료는 UI 스레드에서 StopAsync()를 동기 대기(.GetResult())한다.
    /// StopAsync 내부 await가 동기화 컨텍스트(UI 스레드)로 복귀하려 하면 데드락이 난다.
    /// ConfigureAwait(false) 적용으로 데드락이 없어야 한다.
    /// </summary>
    [Fact]
    public void StopAsync_DoesNotDeadlock_WhenBlockedOnSyncContext()
    {
        string path = Path.Combine(Path.GetTempPath(), $"km-life-{Guid.NewGuid():n}.json");
        string pipe = $"KeyManager.life.{Guid.NewGuid():n}";
        var store = VaultStore.CreateNew(path, "pw", TimeSpan.FromMinutes(5));
        store.Unlock("pw");
        var server = new PipeBrokerServer(new BrokerService(store), pipe);
        server.Start();

        var done = new ManualResetEventSlim(false);
        Exception? failure = null;
        var t = new Thread(() =>
        {
            // WinForms UI 스레드를 모사: 컨티뉴에이션을 Post로 받지만 스레드가 막혀 실행 못 함.
            SynchronizationContext.SetSynchronizationContext(new BlockedThreadContext());
            try { server.StopAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { failure = ex; }
            finally { done.Set(); }
        }) { IsBackground = true };
        t.Start();

        bool completed = done.Wait(TimeSpan.FromSeconds(5));
        store.Dispose();
        try { File.Delete(path); } catch { }

        Assert.Null(failure);
        Assert.True(completed, "StopAsync가 UI 동기화 컨텍스트에서 데드락했습니다.");
    }

    /// <summary>막힌 UI 스레드를 모사: Post된 컨티뉴에이션은 영영 실행되지 않는다.</summary>
    private sealed class BlockedThreadContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) { /* 막힌 스레드 → 실행 안 됨 */ }
    }
}
