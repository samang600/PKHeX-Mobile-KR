using CommunityToolkit.Maui;
namespace PKHeXKR;
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().UseMauiCommunityToolkit();
        return builder.Build();
    }
}
public class App : Application
{
    public App()
    {
        UserAppTheme = (AppTheme)Preferences.Get("theme_mode", 0);   // 0 자동, 1 라이트, 2 다크
        try { Environment.CurrentDirectory = FileSystem.AppDataDirectory; } catch { }   // SysBot.Base 로그(logs/) 쓰기 위치
        AppState.InitLanguage();
    }
    protected override Window CreateWindow(IActivationState activationState) => new(new MainPage()) { Title = "PKHeX 모바일" };
}
