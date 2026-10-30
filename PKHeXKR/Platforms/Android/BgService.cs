using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;

namespace PKHeXKR.Platforms.Android;

/// <summary>
/// 오래 걸리는 작업 동안 켜 두는 포그라운드 서비스. 알림에 지금 작업의 진행 상황을 보여 주고, 작업이 모두 끝나면 멈춤.
/// 작업 자체는 앱 안의 스레드에서 돌고, 이 서비스는 앱을 내려도 시스템이 끄지 않게 붙잡아 두는 역할.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public class BgService : Service
{
    private const string Channel = "bg_tasks", DoneChannel = "bg_done", QuietChannel = "bg_done_quiet";
    private const int OngoingId = 1999;
    private static volatile bool started;   // StartForegroundService를 부름 (서비스가 아직 안 떴을 수도)
    private static volatile bool live;      // OnStartCommand까지 왔음 (실제로 포그라운드)
    private static volatile bool blocked;   // 시스템이 포그라운드 서비스를 막음 (안드로이드 15 dataSync 시간 한도 등) → 이후는 알림만

    public override IBinder OnBind(Intent intent) => null;

    public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
    {
        var n = Build(Platform.AppContext);
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(29)) StartForeground(OngoingId, n, ForegroundService.TypeDataSync);
            else StartForeground(OngoingId, n);
            live = true;
        }
        catch (Exception)   // ForegroundServiceStartNotAllowedException 등: 포그라운드로 못 올림 → 이번엔 알림만, 다시 시도하지 않음
        {
            blocked = true; started = false; live = false;
            try { NotificationManagerCompat.From(Platform.AppContext).Notify(OngoingId, n); } catch { }
            StopSelf();
            return StartCommandResult.NotSticky;
        }
        // StartForegroundService 직후 작업이 바로 끝나 StopService가 먼저 불린 경우: 여기서 스스로 멈춤 (그 전에 StopService를 부르면 앱이 죽음)
        if (BgTask.Count == 0) Stop();
        return StartCommandResult.NotSticky;
    }

    /// <summary>안드로이드 15+: dataSync 종류는 하루 6시간 한도 → 시간이 다 되면 시스템이 부름. 바로 멈춰야 함 (안 멈추면 ANR).</summary>
    public override void OnTimeout(int startId, ForegroundService fgsType)
    {
        blocked = true;
        Stop();
    }

    public override void OnDestroy()
    {
        started = false; live = false;
        base.OnDestroy();
    }

    private void Stop()
    {
        try { if (OperatingSystem.IsAndroidVersionAtLeast(24)) StopForeground(StopForegroundFlags.Remove); else StopForeground(true); } catch { }
        StopSelf(); started = false; live = false;
    }

    private static void Channels(Context c)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var nm = (NotificationManager)c.GetSystemService(Context.NotificationService);
        if (nm.GetNotificationChannel(Channel) == null)
            nm.CreateNotificationChannel(new NotificationChannel(Channel, "진행 중인 작업", NotificationImportance.Low) { Description = "리빙덱스 생성·PokeLDN 교환·시드 찾기 진행 상황" });
        if (nm.GetNotificationChannel(DoneChannel) == null)
            nm.CreateNotificationChannel(new NotificationChannel(DoneChannel, "작업 완료", NotificationImportance.Default) { Description = "오래 걸리는 작업이 끝났을 때" });
        if (nm.GetNotificationChannel(QuietChannel) == null)
        {
            var q = new NotificationChannel(QuietChannel, "작업 완료 (진동 없음)", NotificationImportance.Default) { Description = "설정에서 '알림 진동'을 끈 경우" };
            q.EnableVibration(false);
            nm.CreateNotificationChannel(q);
        }
    }

    private static PendingIntent OpenApp(Context c)
    {
        var i = c.PackageManager.GetLaunchIntentForPackage(c.PackageName);
        i?.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        return PendingIntent.GetActivity(c, 0, i, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
    }

    private static Notification Build(Context c)
    {
        Channels(c);
        var t = BgTask.Current; int count = BgTask.Count;
        var b = new NotificationCompat.Builder(c, Channel)
            .SetSmallIcon(Resource.Drawable.ic_stat_pkhex)
            .SetContentTitle(Loc.T(t == null ? "PKHeX 모바일" : t.Title + (count > 1 ? $" 외 {count - 1}개" : "")))
            .SetContentText(Loc.T(t?.Text is { Length: > 0 } s ? s : "진행 중…"))
            .SetOngoing(true).SetOnlyAlertOnce(true).SetSilent(true)
            .SetContentIntent(OpenApp(c));
        if (t != null && t.Total > 0 && t.Done_ >= 0) b.SetProgress(t.Total, Math.Min(t.Done_, t.Total), false);
        else b.SetProgress(0, 0, true);
        return b.Build();
    }

    /// <summary>작업이 생기거나 진행되면: 서비스를 켜고(처음) 알림을 고침.</summary>
    public static void Update(bool first)
    {
        var c = Platform.AppContext;
        if (!started && !blocked)
        {
            started = true; live = false;
            var intent = new Intent(c, typeof(BgService));
            try { if (OperatingSystem.IsAndroidVersionAtLeast(26)) c.StartForegroundService(intent); else c.StartService(intent); }
            catch { started = false; blocked = true; }   // 백그라운드에서 시작 금지 등 → 알림만
            if (started) return;
        }
        try { NotificationManagerCompat.From(c).Notify(OngoingId, Build(c)); } catch { }
    }

    /// <summary>작업이 끝나면: 완료 알림(원하면), 남은 작업이 없으면 서비스 멈춤.</summary>
    public static void Finish(BgTask t, bool notify)
    {
        var c = Platform.AppContext;
        if (notify)
        {
            Channels(c);
            var n = new NotificationCompat.Builder(c, BgTask.Vibrate ? DoneChannel : QuietChannel)
                .SetSmallIcon(Resource.Drawable.ic_stat_pkhex)
                .SetContentTitle(Loc.T(t.Title + " · 끝"))
                .SetContentText(Loc.T(t.Text))
                .SetStyle(new NotificationCompat.BigTextStyle().BigText(t.Text))
                .SetAutoCancel(true).SetContentIntent(OpenApp(c)).Build();
            try { NotificationManagerCompat.From(c).Notify(t.Id, n); } catch { }
        }
        if (!started) { if (BgTask.Count == 0) try { NotificationManagerCompat.From(c).Cancel(OngoingId); } catch { } return; }
        if (BgTask.Count == 0)
        {
            // 서비스가 아직 OnStartCommand에 이르지 못했으면 StopService를 부르면 안 됨 (StartForeground 전에 멈추면 시스템이 앱을 죽임)
            // → 그 경우 OnStartCommand가 Count==0을 보고 스스로 멈춤
            if (!live) return;
            started = false; live = false;
            try { c.StopService(new Intent(c, typeof(BgService))); } catch { }
            try { NotificationManagerCompat.From(c).Cancel(OngoingId); } catch { }
        }
        else try { NotificationManagerCompat.From(c).Notify(OngoingId, Build(c)); } catch { }
    }
}
