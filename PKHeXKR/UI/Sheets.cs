using Microsoft.Maui.Controls.Shapes;
namespace PKHeXKR;

/// <summary>화면 아래에서 올라오는 시트. 화면 전환 없이 기능을 여는 공통 틀.</summary>
public class Sheet : ContentView
{
    protected readonly Grid Body = new() { RowSpacing = 8 };
    private readonly Border panel;
    public event Action Closed;

    public bool IsSide { get; }
    public Sheet(string title, double heightRatio = 0.82, bool side = false)
    {
        IsSide = side;
        BackgroundColor = Color.FromRgba(0, 0, 0, 0.45);
        var handle = new BoxView { HeightRequest = 4, WidthRequest = 40, CornerRadius = 2, HorizontalOptions = LayoutOptions.Center, Color = Colors.Gray, Opacity = 0.5 };
        var close = new Button { Text = "✕", FontSize = 16, WidthRequest = 40, HeightRequest = 40, CornerRadius = 20, Padding = 0, BackgroundColor = Colors.Transparent };
        close.SetAppThemeColor(Button.TextColorProperty, Colors.Black, Colors.White);
        close.Clicked += (_, _) => Close();
        var head = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        head.Add(T.L(title, 18, bold: true), 0); head.Add(close, 1);

        var root = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) }, RowSpacing = 8 };
        root.Add(handle, 0, 0); root.Add(head, 0, 1); root.Add(Body, 0, 2);
        panel = new Border
        {
            Content = root, Padding = new Thickness(16, 8, 16, 16), StrokeThickness = 0, VerticalOptions = LayoutOptions.End,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20, 20, 0, 0) },
        };
        T.CardBg(panel);
        if (side)
        {
            handle.IsVisible = false;
            panel.VerticalOptions = LayoutOptions.Fill; panel.HorizontalOptions = LayoutOptions.Start;
            panel.Padding = new Thickness(16, 40, 12, 16);
            panel.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0, 20, 0, 20) };
        }
        Content = panel;
        var outside = new TapGestureRecognizer(); outside.Tapped += (_, _) => Close();
        GestureRecognizers.Add(outside);
        panel.GestureRecognizers.Add(new TapGestureRecognizer());
        SizeChanged += (_, _) =>
        {
            if (side) { if (Width > 0) panel.WidthRequest = Math.Min(Width * 0.84, 420); return; }
            if (Height > 0) panel.MaximumHeightRequest = Height * heightRatio; if (heightRatio >= 0.8 && Height > 0) panel.HeightRequest = Height * heightRatio;
        };
    }

    public void Close() { SheetHost.Remove(this); Closed?.Invoke(); }
}

public static class SheetHost
{
    private static Grid layer;
    public static void Attach(Grid overlay) => layer = overlay;
    public static void Show(Sheet s) { layer.Children.Add(s); layer.InputTransparent = false; if (s.IsSide) s.TranslationX = -420; else s.TranslationY = 400; s.Opacity = 0; _ = s.FadeToAsync(1, 120); _ = s.TranslateToAsync(0, 0, 200, Easing.CubicOut); }
    public static bool CloseTop() { if (layer.Children.LastOrDefault() is Sheet s) { s.Close(); return true; } return false; }
    public static void Remove(Sheet s) { layer.Children.Remove(s); layer.InputTransparent = layer.Children.Count == 0; }
}

/// <summary>목록 검색 선택 시트. 한글 초성 검색(예: ㅍㅋㅊ → 피카츄) 지원.</summary>
public class PickerSheet : Sheet
{
    private readonly IReadOnlyList<PKHeX.Core.ComboItem> all;
    private readonly CollectionView list = new() { SelectionMode = SelectionMode.None };
    private readonly Action<PKHeX.Core.ComboItem> picked;

    public PickerSheet(string title, IReadOnlyList<PKHeX.Core.ComboItem> items, int current, Action<PKHeX.Core.ComboItem> onPick, Func<PKHeX.Core.ComboItem, string> icon = null, Func<PKHeX.Core.ComboItem, int> tier = null) : base(title)
    {
        all = items; picked = onPick;
        if (title.StartsWith("볼")) list.ItemsLayout = new GridItemsLayout(2, ItemsLayoutOrientation.Vertical);   // 볼은 2열로
        var search = T.Input(placeholder: "검색 (초성 가능: ㅍㅋㅊ)");
        search.TextChanged += (_, e) => Filter(e.NewTextValue);
        list.ItemTemplate = new DataTemplate(() =>
        {
            var g = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, Padding = new Thickness(6, 4), ColumnSpacing = 10, HeightRequest = 46 };
            g.SetBinding(BackgroundColorProperty, "Tint");
            var img = new Image { WidthRequest = 36, HeightRequest = 30 };
            img.SetBinding(Image.SourceProperty, "Icon");
            var t = T.L("", 16); t.SetBinding(Label.TextProperty, "Text");
            t.SetBinding(Label.FontAttributesProperty, new Binding("Current", converter: new FuncConverter<bool, FontAttributes>(b => b ? FontAttributes.Bold : FontAttributes.None)));
            g.Add(img, 0); g.Add(t, 1);
            var tap = new TapGestureRecognizer(); tap.SetBinding(TapGestureRecognizer.CommandParameterProperty, ".");
            tap.Tapped += (_, e) => { if (e.Parameter is Row r) { Close(); picked(r.Item); } };
            g.GestureRecognizers.Add(tap);
            return g;
        });
        rows = items.Select(i => new Row(i, i.Value == current, icon?.Invoke(i), tier?.Invoke(i) ?? 2)).ToList();
        Body.RowDefinitions.Add(new(GridLength.Auto)); Body.RowDefinitions.Add(new(GridLength.Star));
        Body.Add(search, 0, 0); Body.Add(list, 0, 1);
        Filter("");
    }
    private readonly List<Row> rows;
    public record Row(PKHeX.Core.ComboItem Item, bool Current, string Icon, int Tier = 2)
    {
        public string Text => Item.Text;
        public Color Tint => Tier switch { 0 => Color.FromArgb("#3316A34A"), 1 => Color.FromArgb("#332563EB"), _ => Colors.Transparent };
    }

    private void Filter(string q)
    {
        q = (q ?? "").Trim();
        IEnumerable<Row> r = rows;
        if (q.Length > 0) r = rows.Where(x => Korean.Match(x.Text, q)).OrderBy(x => x.Tier).ThenBy(x => x.Text.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1);
        else r = rows.OrderBy(x => x.Current ? 0 : 1).ThenBy(x => x.Tier);   // 합법(초록) 기술이 위로
        list.ItemsSource = r.Take(300).ToList();
    }
}

public static class Korean
{
    private const string Cho = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
    public static string Initials(string s) => new(s.Select(c => c is >= '\uAC00' and <= '\uD7A3' ? Cho[(c - 0xAC00) / 588] : c).ToArray());
    public static bool IsInitials(string q) => q.All(c => Cho.Contains(c) || c == ' ');
    public static bool Match(string text, string q)
        => text.Contains(q, StringComparison.OrdinalIgnoreCase) || (IsInitials(q) && Initials(text).Contains(q));
}

public class FuncConverter<TIn, TOut>(Func<TIn, TOut> f) : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is TIn v ? f(v) : default(TOut);
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}
