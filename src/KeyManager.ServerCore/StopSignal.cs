using System.Runtime.Versioning;

namespace KeyManager.Server;

/// <summary>
/// 헤드리스 서버와 트레이 동반앱 사이의 <b>우아한 종료 신호</b>(커스텀 IPC 없이 이름있는 커널 이벤트만).
/// 서버 호스트는 <see cref="CreateForHost"/>로 이벤트를 만들고 <see cref="WaitAsync"/>로 대기하다
/// 신호가 오면 정지한다. 트레이는 <see cref="Signal"/>로 서버에 종료를 요청한다.
///
/// 세션 0(서비스)과 사용자 세션(트레이)이 공유해야 하므로 <c>Global\</c> 네임스페이스를 쓴다.
/// Windows 전용(EventWaitHandle 이름 지정).
/// </summary>
[SupportedOSPlatform("windows")]
public static class StopSignal
{
    /// <summary>이름있는 수동 리셋 이벤트. 서버 단일 인스턴스와 짝을 이룬다.</summary>
    public const string EventName = @"Global\KeyManager.Server.Stop";

    /// <summary>서버 단일 인스턴스 뮤텍스 이름(트레이의 "실행중" 감지에도 사용).</summary>
    public const string ServerMutexName = @"Global\KeyManager.Server.SingleInstance";

    /// <summary>
    /// 서버 호스트용 종료 이벤트를 만든다(수동 리셋, 초기 비신호). 프로세스 시작 시 1회 생성해 보관.
    /// </summary>
    public static EventWaitHandle CreateForHost()
        => new(initialState: false, EventResetMode.ManualReset, EventName);

    /// <summary>
    /// 트레이 → 서버 종료 요청. 이벤트가 없으면(서버 미기동) 조용히 false.
    /// </summary>
    public static bool Signal()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(EventName, out EventWaitHandle? handle) || handle is null)
                return false;
            using (handle) return handle.Set();
        }
        catch { return false; }
    }

    /// <summary>서버가 이미 실행 중인지(단일 인스턴스 뮤텍스 존재로) 감지.</summary>
    public static bool IsServerRunning()
    {
        try
        {
            if (!Mutex.TryOpenExisting(ServerMutexName, out Mutex? m) || m is null)
                return false;
            m.Dispose();
            return true;
        }
        catch { return false; }
    }

    /// <summary>주어진 이벤트가 신호될 때까지(또는 취소될 때까지) 비동기 대기.</summary>
    public static async Task WaitAsync(WaitHandle handle, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration ctReg = ct.Register(() => tcs.TrySetResult());
        RegisteredWaitHandle? reg = null;
        reg = ThreadPool.RegisterWaitForSingleObject(
            handle,
            (_, _) => { tcs.TrySetResult(); reg?.Unregister(null); },
            state: null,
            millisecondsTimeOutInterval: -1,
            executeOnlyOnce: true);
        try { await tcs.Task.ConfigureAwait(false); }
        finally { reg.Unregister(null); }
    }
}
