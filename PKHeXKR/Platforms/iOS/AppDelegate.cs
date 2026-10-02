using Foundation;
using UIKit;
namespace PKHeXKR;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    /// <summary>파일 앱·다른 앱의 "공유/열기"로 받은 세이브·포켓몬 파일(.pk9/.pa9/.pk8/.pkm/main 등)을 바로 엶.</summary>
    public override bool OpenUrl(UIApplication application, NSUrl url, NSDictionary options)
    {
        if (url == null || !url.IsFileUrl) return base.OpenUrl(application, url, options);
        bool access = url.StartAccessingSecurityScopedResource();
        try
        {
            var data = NSData.FromUrl(url)?.ToArray();
            var name = url.LastPathComponent ?? "file.bin";
            if (data == null || data.Length == 0) return false;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                for (int k = 0; k < 60 && MainPage.Instance == null; k++) await Task.Delay(100);
                if (MainPage.Instance != null) await MainPage.Instance.OpenBytes(data, name);
            });
            return true;
        }
        catch { return false; }
        finally { if (access) url.StopAccessingSecurityScopedResource(); }
    }
}
