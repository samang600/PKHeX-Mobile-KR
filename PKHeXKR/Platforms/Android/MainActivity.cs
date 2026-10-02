using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
namespace PKHeXKR;

// 다른 앱(파일 관리자·메신저 등)에서 세이브·포켓몬 파일(.pk8/.pk9/.pa9/.pkm/.bin 등)을 "열기"나 "공유"로 넘기면 이 앱에서 바로 열기
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTask, WindowSoftInputMode = Android.Views.SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataMimeType = "application/octet-stream")]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault }, DataMimeType = "*/*", DataScheme = "content")]
[IntentFilter(new[] { Intent.ActionSend }, Categories = new[] { Intent.CategoryDefault }, DataMimeType = "*/*")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // 뒤로가기(제스처 포함)를 앱이 직접 처리: 시트 닫기 → 없으면 종료 확인
        OnBackPressedDispatcher.AddCallback(this, new BackCallback());
        Handle(Intent);
    }

    private sealed class BackCallback() : AndroidX.Activity.OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed() => MainThread.BeginInvokeOnMainThread(() => MainPage.Instance?.HandleBack());
    }
    protected override void OnNewIntent(Intent intent) { base.OnNewIntent(intent); Handle(intent); }

    private void Handle(Intent i)
    {
        if (i == null || (i.Action != Intent.ActionView && i.Action != Intent.ActionSend)) return;
        Android.Net.Uri uri = i.Action == Intent.ActionSend ? (Android.Net.Uri)i.GetParcelableExtra(Intent.ExtraStream) : i.Data;
        if (uri == null) return;
        try
        {
            using var st = ContentResolver.OpenInputStream(uri);
            using var ms = new MemoryStream(); st.CopyTo(ms);
            var data = ms.ToArray(); var name = DisplayName(uri) ?? "file.bin";
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                for (int k = 0; k < 60 && MainPage.Instance == null; k++) await Task.Delay(100);
                if (MainPage.Instance != null) await MainPage.Instance.OpenBytes(data, name);
            });
        }
        catch { }
    }

    private string DisplayName(Android.Net.Uri uri)
    {
        try
        {
            using var c = ContentResolver.Query(uri, null, null, null, null);
            if (c != null && c.MoveToFirst()) { int idx = c.GetColumnIndex(Android.Provider.OpenableColumns.DisplayName); if (idx >= 0) return c.GetString(idx); }
        }
        catch { }
        return uri.LastPathSegment;
    }
}
