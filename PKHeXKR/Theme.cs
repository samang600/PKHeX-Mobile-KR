using Microsoft.Maui.Controls.Shapes;
namespace PKHeXKR;

/// <summary>디자인 토큰과 공통 위젯 생성기. 모든 화면은 여기서 만든 부품으로 조립.</summary>
public static class T
{
    /// <summary>성별 버튼 색: 남 파랑, 여 분홍.</summary>
    public static void GenderTint(Button b, int gender)
    {
        var c = gender == 0 ? Color.FromArgb("#2563EB") : gender == 1 ? Color.FromArgb("#DB2777") : Color.FromArgb("#6B7280");
        b.TextColor = c; b.BorderColor = c;
    }
    public static readonly Color Accent = Color.FromArgb("#4F46E5");
    public static readonly Color AccentSoft = Color.FromArgb("#E0E7FF");
    public static readonly Color Good = Color.FromArgb("#16A34A");
    public static readonly Color Bad = Color.FromArgb("#DC2626");
    public static readonly Color Warn = Color.FromArgb("#D97706");

    public static void Bg(VisualElement v) => v.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#F3F4F6"), Color.FromArgb("#0F1115"));
    public static void CardBg(VisualElement v) => v.SetAppThemeColor(VisualElement.BackgroundColorProperty, Colors.White, Color.FromArgb("#1A1D24"));
    public static void Text(Label l) => l.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#111827"), Color.FromArgb("#F3F4F6"));
    public static void Sub(Label l) => l.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#6B7280"), Color.FromArgb("#9CA3AF"));
    public static void Line(VisualElement v) => v.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#E5E7EB"), Color.FromArgb("#2A2E37"));

    public static Label L(string text = "", double size = 14, bool bold = false, bool sub = false)
    {
        var l = new Label { Text = text, FontSize = size, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation };
        if (sub) Sub(l); else Text(l);
        return l;
    }

    public static Border Card(View content, double pad = 12)
    {
        var b = new Border { Content = content, Padding = pad, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 14 } };
        CardBg(b);
        return b;
    }

    /// <summary>둥근 알약형 버튼. primary면 강조색 배경.</summary>
    public static Button Pill(string text, bool primary = false, double size = 13)
    {
        var b = new Button { Text = text, FontSize = size, CornerRadius = 18, Padding = new Thickness(14, 4), HeightRequest = 36, BorderWidth = primary ? 0 : 1 };
        if (primary) { b.BackgroundColor = Accent; b.TextColor = Colors.White; }
        else
        {
            b.SetAppThemeColor(Button.BackgroundColorProperty, Colors.White, Color.FromArgb("#1A1D24"));
            b.SetAppThemeColor(Button.TextColorProperty, Accent, Color.FromArgb("#A5B4FC"));
            b.SetAppThemeColor(Button.BorderColorProperty, Color.FromArgb("#C7D2FE"), Color.FromArgb("#3730A3"));
        }
        return b;
    }

    /// <summary>아이콘+짧은 글자 세로 배치 버튼 (하단 작업 막대용).</summary>
    public static View Action(string glyph, string text, Action onTap)
    {
        var g = L(glyph, 18); g.HorizontalOptions = LayoutOptions.Center;
        var t = L(text, 11, sub: true); t.HorizontalOptions = LayoutOptions.Center;
        var s = new VerticalStackLayout { Spacing = 1, Padding = new Thickness(4, 6), Children = { g, t } };
        var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => onTap();
        s.GestureRecognizers.Add(tap);
        return s;
    }

    /// <summary>라벨 위, 입력 아래의 폼 필드.</summary>
    public static View Field(string label, View input)
    {
        var l = L(label, 12, sub: true);
        return new VerticalStackLayout { Spacing = 2, Children = { l, input } };
    }

    public static Entry Input(Keyboard kb = null, string placeholder = "")
    {
        var e = new Entry { FontSize = 15, Keyboard = kb ?? Keyboard.Default, Placeholder = placeholder, HeightRequest = 44 };
        e.SetAppThemeColor(Entry.TextColorProperty, Color.FromArgb("#111827"), Color.FromArgb("#F3F4F6"));
        return e;
    }

    /// <summary>탭하면 검색 선택 시트가 열리는 필드 (목록형 값 선택용).</summary>
    public static (Border view, Label text) Chooser(Action onTap)
    {
        var txt = L("", 15); txt.HorizontalOptions = LayoutOptions.Fill;
        var chev = L("⌄", 15, sub: true);
        var g = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(12, 0), HeightRequest = 44 };
        g.Add(txt, 0); g.Add(chev, 1);
        var b = new Border { Content = g, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 10 } };
        b.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D1D5DB"), Color.FromArgb("#374151"));
        var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => onTap();
        b.GestureRecognizers.Add(tap);
        return (b, txt);
    }

    public static Grid Cols(int n, double spacing = 10)
    {
        var g = new Grid { ColumnSpacing = spacing, RowSpacing = spacing };
        for (int i = 0; i < n; i++) g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        return g;
    }
}
