namespace PKHeXKR;

/// <summary>
/// 오래 걸리는 작업(리빙덱스 생성·PokeLDN 교환·시드 찾기 등)을 앱을 내려도 계속하게 하고, 알림(상단바)에 진행 상황·완료를 보여 줌.
/// 안드로이드: 작업이 하나라도 있으면 포그라운드 서비스(데이터 동기화 종류)를 켜 둠 → 시스템이 앱을 끄지 않음.
/// </summary>
public sealed class BgTask : IDisposable
{
    private static readonly List<BgTask> running = [];
    private static int nextId = 2000;
    private static bool asked;
    public static bool Enabled { get => Preferences.Get("bg_notify", true); set => Preferences.Set("bg_notify", value); }
    /// <summary>완료 알림 진동 (끄면 진동 없는 알림 채널로).</summary>
    public static bool Vibrate { get => Preferences.Get("bg_vibrate", true); set => Preferences.Set("bg_vibrate", value); }

    public readonly string Title;
    public readonly int Id;
    public string Text { get; private set; } = "";
    public int Done_, Total;
    private DateTime last = DateTime.MinValue;
    private bool finished;
    internal readonly CancellationTokenSource Cancel;

    private BgTask(string title, CancellationTokenSource cts) { Title = title; Cancel = cts; Id = Interlocked.Increment(ref nextId); }

    public static BgTask Begin(string title, CancellationTokenSource cts = null)
    {
        var t = new BgTask(title, cts);
        lock (running) running.Add(t);
        if (!asked) { asked = true; MainThread.BeginInvokeOnMainThread(async () => await AskPermission()); }
        Platform_Update(t, true);
        return t;
    }

    /// <summary>진행 상황 (0.5초에 한 번만 알림을 고침).</summary>
    public void Report(string text, int done = -1, int total = -1)
    {
        Text = text ?? ""; Done_ = done; Total = total;
        if ((DateTime.Now - last).TotalMilliseconds < 500) return;
        last = DateTime.Now;
        Platform_Update(this, false);
    }

    /// <summary>끝: 완료 알림 (앱을 보고 있지 않을 때 알 수 있게).</summary>
    public void Done(string text)
    {
        if (finished) return;
        finished = true; Text = text ?? "";
        lock (running) running.Remove(this);
        Platform_Done(this, true);
    }

    public void Dispose()
    {
        if (finished) return;
        finished = true;
        lock (running) running.Remove(this);
        Platform_Done(this, false);
    }

    internal static BgTask Current { get { lock (running) return running.LastOrDefault(); } }
    internal static int Count { get { lock (running) return running.Count; } }

    // ───── 플랫폼 ─────
    private static void Platform_Update(BgTask t, bool first)
    {
#if ANDROID
        if (!Enabled) return;
        try { Platforms.Android.BgService.Update(first); } catch { }
#endif
    }

    private static void Platform_Done(BgTask t, bool notify)
    {
#if ANDROID
        try { Platforms.Android.BgService.Finish(t, notify && Enabled); } catch { }
#endif
    }

    /// <summary>안드로이드 13+: 알림 권한 (처음 한 번 물어봄).</summary>
    public static async Task AskPermission()
    {
#if ANDROID
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(33) && await Permissions.CheckStatusAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
                await Permissions.RequestAsync<Permissions.PostNotifications>();
        }
        catch { }
#else
        await Task.CompletedTask;
#endif
    }
}
