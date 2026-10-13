using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MtgEstoque;

// Paleta de um tema. Primary/OnPrimary: botão principal e o texto dentro dele (branco ou preto, o que
// contrastar mais). As cores de status (bolinhas) são fixas por claro/escuro e nunca vêm da paleta,
// pra não se confundirem com o tom do tema.
public record Palette(string Name, bool Dark, string Bg, string Surface, string Hover, string Selected, string Border,
                      string Text, string Muted, string Primary, string OnPrimary, string Chip, string Input);

public static class Theme
{
    public static readonly Palette[] All =
    {
        new("Claro",      false, "#FFFFFF", "#F7F7F8", "#EFEFF1", "#E6E6EA", "#E2E2E6", "#1A1A1A", "#7A7A80", "#1A1A1A", "#FFFFFF", "#F2F2F4", "#FFFFFF"),
        new("Névoa",      false, "#F5F8FC", "#E9EFF7", "#DFE7F2", "#D3DEEC", "#D3DCE8", "#14233A", "#64748B", "#22395A", "#FFFFFF", "#E2E9F3", "#FFFFFF"),
        new("Sálvia",     false, "#F5F8F3", "#E8EFE4", "#DDE7D8", "#D1DECB", "#D2DCCB", "#1C2A1F", "#6A7A6C", "#2F4A36", "#FFFFFF", "#E1EADC", "#FFFFFF"),
        new("Sépia",      false, "#F7F1E6", "#EEE4D2", "#E6DAC4", "#DDCFB6", "#DDCFB8", "#3B2F22", "#86765F", "#5C4430", "#FBF7EF", "#EAE0CC", "#FBF7EF"),
        new("Noturno",    true,  "#1E1F22", "#26282C", "#303237", "#3A3D43", "#383B41", "#ECEDEE", "#9A9DA3", "#ECEDEE", "#1E1F22", "#2E3035", "#26282C"),
        new("Grafite",    true,  "#121212", "#1A1A1A", "#242424", "#2E2E2E", "#2C2C2C", "#F2F2F2", "#8F8F8F", "#F2F2F2", "#121212", "#222222", "#1A1A1A"),
        new("Ardósia",    true,  "#1B2838", "#223246", "#2B3D54", "#344862", "#33465E", "#E6EDF6", "#9AAAC0", "#C9D8EC", "#1B2838", "#2A3B51", "#223246"),
        new("Meia-noite", true,  "#0D1421", "#131C2E", "#1B2639", "#243147", "#222E43", "#E8EEF8", "#8A97AD", "#A9C0E4", "#0D1421", "#182335", "#131C2E"),
        new("Floresta",   true,  "#131B16", "#1A241D", "#223026", "#2B3A2F", "#2A382E", "#E6EFE8", "#93A597", "#B9D3BF", "#131B16", "#213026", "#1A241D"),
    };

    public static Palette Current { get; private set; } = All[0];
    public static bool Welcomed { get; set; }
    public static event Action? Changed;

    static readonly string FilePath = System.IO.Path.Combine(Store.Dir, "settings.json");
    record Settings(string Theme, bool Welcomed);

    public static void Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath))!;
            Welcomed = s.Welcomed;
            Apply(s.Theme, save: false);
        }
        catch { Apply(All[0].Name, save: false); }
    }

    public static void Save()
    {
        Directory.CreateDirectory(Store.Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new Settings(Current.Name, Welcomed)));
    }

    static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    // Troca os pincéis do app (XAML usa DynamicResource; o resto é redesenhado via Changed).
    public static void Apply(string name, bool save = true)
    {
        var p = All.FirstOrDefault(x => x.Name == name) ?? All[0];
        Current = p;
        var r = Application.Current.Resources;
        void set(string key, string hex) => r[key] = Brush(hex);
        set("Bg", p.Bg); set("Surface", p.Surface); set("Hover", p.Hover); set("Selected", p.Selected); set("Border", p.Border);
        set("Text", p.Text); set("Muted", p.Muted); set("Accent", p.Primary); set("OnAccent", p.OnPrimary); set("Chip", p.Chip); set("Input", p.Input);
        // aviso: inverso do fundo pra destacar
        set("Toast", p.Dark ? p.Text : "#1A1A1A"); set("OnToast", p.Dark ? p.Bg : "#FFFFFF");
        // status: saturados no claro, mais luminosos no escuro
        set("DotRed", p.Dark ? "#FF6B5E" : "#D93025");
        set("DotBlue", p.Dark ? "#5AA2FF" : "#1E6FD9");
        set("DotAmber", p.Dark ? "#F5C044" : "#E0A100");
        set("DotGray", p.Dark ? "#6E737A" : "#A3A8AE");
        set("DotGreen", p.Dark ? "#5FD07A" : "#2E9E4F");
        set("Good", p.Dark ? "#6CCB7A" : "#2E7D32");
        set("Warn", p.Dark ? "#F0B65A" : "#B06E00");
        set("DangerText", p.Dark ? "#FF7B72" : "#C62828");
        set("Foil", p.Dark ? "#E8C35A" : "#B8860B");
        set("GoodSoft", p.Dark ? "#1F3A26" : "#E3F1E4");
        set("WarnSoft", p.Dark ? "#3D3118" : "#FFF3D6");
        if (save) Save();
        foreach (Window w in Application.Current.Windows) App.PaintTitleBar(w);
        Changed?.Invoke();
    }

    public static Brush B(string key) => (Brush)Application.Current.Resources[key];

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    // Cards de tema com uma miniatura do app em cada paleta. Clique aplica na hora.
    public static StackPanel Picker()
    {
        var root = new StackPanel();
        var light = new WrapPanel(); var dark = new WrapPanel(); // claros numa linha, escuros na outra
        root.Children.Add(light); root.Children.Add(dark);
        foreach (var p in All)
        {
            bool on = p.Name == Current.Name;
            var mini = new Grid { Width = 122, Height = 64, Clip = new RectangleGeometry(new Rect(0, 0, 122, 64), 7, 7) }; // cantos arredondados de verdade
            mini.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            mini.ColumnDefinitions.Add(new ColumnDefinition());
            var side = new Border { Background = Brush(p.Surface) };
            var sideLines = new StackPanel { Margin = new Thickness(7, 10, 7, 0) };
            foreach (var w in new[] { 18.0, 14, 16 })
                sideLines.Children.Add(new Border { Height = 4, Width = w, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2), Background = Brush(p.Muted), Margin = new Thickness(0, 0, 0, 6), Opacity = 0.7 });
            side.Child = sideLines;
            mini.Children.Add(side);
            var body = new Border { Background = Brush(p.Bg) };
            Grid.SetColumn(body, 1);
            var lines = new StackPanel { Margin = new Thickness(10, 10, 10, 0) };
            lines.Children.Add(new Border { Height = 6, Width = 46, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3), Background = Brush(p.Text) });
            foreach (var w in new[] { 64.0, 52 })
                lines.Children.Add(new Border { Height = 4, Width = w, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2), Background = Brush(p.Muted), Margin = new Thickness(0, 7, 0, 0), Opacity = 0.6 });
            lines.Children.Add(new Border { Height = 12, Width = 36, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4), Background = Brush(p.Primary), Margin = new Thickness(0, 8, 0, 0) });
            body.Child = lines;
            mini.Children.Add(body);

            var frame = new Border { CornerRadius = new CornerRadius(8), Child = mini, BorderThickness = new Thickness(1) };
            frame.SetResourceReference(Border.BorderBrushProperty, "Border"); // borda na cor do tema atual
            var sp = new StackPanel();
            sp.Children.Add(frame);
            var label = new TextBlock { Text = p.Name, Margin = new Thickness(2, 8, 0, 0), FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            sp.Children.Add(label);
            var card = new Border
            {
                Child = sp, Padding = new Thickness(6), Margin = new Thickness(0, 0, 10, 10), CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(2), Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent,
            };
            card.SetResourceReference(Border.BorderBrushProperty, on ? "Accent" : "Bg");
            card.MouseLeftButtonUp += (_, _) => Apply(p.Name);
            (p.Dark ? dark : light).Children.Add(card);
        }
        return root;
    }
}

// Janela "Aparência" (a mesma escolha de tema da tela de boas-vindas).
public class ThemeDialog : Dialog
{
    public ThemeDialog() : base("Aparência", 820, 540)
    {
        Build();
        Theme.Changed += Build;
        Closed += (_, _) => Theme.Changed -= Build;
    }

    void Build()
    {
        var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        var title = new TextBlock { Text = "Tema", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) };
        sp.Children.Add(title);
        sp.Children.Add(Theme.Picker());
        var ok = new Button { Content = "Fechar", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), Style = (Style)Application.Current.FindResource("Primary"), IsCancel = true };
        ok.Click += (_, _) => Close();
        sp.Children.Add(ok);
        Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
