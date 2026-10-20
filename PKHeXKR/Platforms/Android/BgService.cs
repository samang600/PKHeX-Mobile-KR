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
    private const string Channel = "bg_tasks", DoneChannel = "bg_done";
    private const int OngoingId = 1999;
    private static bool started;

    public override IBinder OnBind(Intent intent) => null;

    public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
    {
        var n = Build(Platform.AppContext);
        if (OperatingSystem.IsAndroidVersionAtLeast(29)) StartForeground(OngoingId, n, ForegroundService.TypeDataSync);
        else StartForeground(OngoingId, n);
        if (BgTask.Count == 0) Stop();
        return StartCommandResult.NotSticky;
    }

    private void Stop()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(24)) StopForeground(StopForegroundFlags.Remove); else StopForeground(true);
        StopSelf(); started = false;
    }

    private static void Channels(Context c)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var nm = (NotificationManager)c.GetSystemService(Context.NotificationService);
        if (nm.GetNotificationChannel(Channel) == null)
            nm.CreateNotificationChannel(new NotificationChannel(Channel, "진행 중인 작업", NotificationImportance.Low) { Description = "리빙덱스 생성·PokeLDN 교환·시드 찾기 진행 상황" });
        if (nm.GetNotificationChannel(DoneChannel) == null)
            nm.CreateNotificationChannel(new NotificationChannel(DoneChannel, "작업 완료", NotificationImportance.Default) { Description = "오래 걸리는 작업이 끝났을 때" });
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
            .SetContentTitle(t == null ? "PKHeX 모바일" : t.Title + (count > 1 ? $" 외 {count - 1}개" : ""))
            .SetContentText(t?.Text is { Length: > 0 } s ? s : "진행 중…")
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
        if (!started)
        {
            started = true;
            var intent = new Intent(c, typeof(BgService));
            if (OperatingSystem.IsAndroidVersionAtLeast(26)) c.StartForegroundService(intent); else c.StartService(intent);
            return;
        }
        NotificationManagerCompat.From(c).Notify(OngoingId, Build(c));
    }

    /// <summary>작업이 끝나면: 완료 알림(원하면), 남은 작업이 없으면 서비스 멈춤.</summary>
    public static void Finish(BgTask t, bool notify)
    {
        var c = Platform.AppContext;
        if (notify)
        {
            Channels(c);
            var n = new NotificationCompat.Builder(c, DoneChannel)
                .SetSmallIcon(Resource.Drawable.ic_stat_pkhex)
                .SetContentTitle(t.Title + " · 끝")
                .SetContentText(t.Text)
                .SetStyle(new NotificationCompat.BigTextStyle().BigText(t.Text))
                .SetAutoCancel(true).SetContentIntent(OpenApp(c)).Build();
            try { NotificationManagerCompat.From(c).Notify(t.Id, n); } catch { }
        }
        if (!started) return;
        if (BgTask.Count == 0)
        {
            started = false;
            c.StopService(new Intent(c, typeof(BgService)));
            NotificationManagerCompat.From(c).Cancel(OngoingId);
        }
        else NotificationManagerCompat.From(c).Notify(OngoingId, Build(c));
    }
}
