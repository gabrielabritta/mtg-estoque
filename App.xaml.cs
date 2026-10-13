using System.Windows;

namespace MtgEstoque;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--test")) Environment.Exit(Store.SelfTest() ? 0 : 1);
        if (e.Args is ["--parse", var inFile, var resFile])
        {
            var es = Store.Parse(System.IO.File.ReadAllText(inFile));
            System.IO.File.WriteAllText(resFile, $"linhas: {es.Count}\ncópias: {es.Sum(x => x.N)}\nfoil: {es.Where(x => x.Foil).Sum(x => x.N)}\nsem arte: {es.Count(x => x.Print == "")}\n"
                + string.Join("\n", es.Take(3).Select(x => $"{x.N} {x.Name} [{x.Print}] foil={x.Foil}")));
            Environment.Exit(0);
        }
        if (e.Args is ["--ligaresolve", var compraFile, var resultFile])
        {
            // diagnóstico: identifica as artes de uma compra da LigaMagic (não mexe nos dados)
            var items = Liga.Parse(System.IO.File.ReadAllText(compraFile));
            var lines = new List<string>();
            foreach (var it in items)
            {
                var m = Task.Run(() => Liga.Resolve(it)).GetAwaiter().GetResult();
                lines.Add($"{(m.Sure ? "OK " : "?? ")} {it.Name,-24} {it.Ed,-6} -> {m.Print,-12} {m.Why}");
            }
            System.IO.File.WriteAllText(resultFile, string.Join("\n", lines));
            Environment.Exit(0);
        }
        if (e.Args is ["--liga", var deckName, var ligaFile])
        {
            // diagnóstico: lista de um deck no formato da LigaMagic (só leitura dos dados)
            var data = Store.Load();
            var deck = Store.Loc(data, deckName);
            var cards = deck.Wants.Where(w => w.Board != Boards.Maybe).GroupBy(w => w.Card, Store.CI).ToDictionary(g => g.Key, g => g.Sum(w => w.Qty), Store.CI);
            Task.Run(() => Types.EnsureLayouts(cards.Keys)).GetAwaiter().GetResult();
            var merged = cards.GroupBy(x => Types.LigaName(x.Key), Store.CI).OrderBy(g => g.Key).Select(g => $"{g.Sum(x => x.Value)} {g.Key}");
            System.IO.File.WriteAllText(ligaFile, string.Join("\n", merged));
            Environment.Exit(0);
        }
        if (e.Args is ["--mox", var url, var outFile])
        {
            // diagnóstico: baixa um deck e escreve o resumo, sem tocar nos dados
            var (name, es) = Task.Run(() => Moxfield.Load(url)).GetAwaiter().GetResult();
            System.IO.File.WriteAllText(outFile, $"{name}\n" + string.Join("\n", es.GroupBy(x => x.Board).Select(g => $"{g.Key}: {g.Sum(x => x.N)}"))
                + "\n" + string.Join("\n", es.Where(x => x.Board == Boards.Cmd).Select(x => $"cmd: {x.Name} {x.Print} foil={x.Foil}")));
            Environment.Exit(0);
        }
        Theme.Load();
        // toda janela (principal e diálogos): ícone do app e barra de título na cor do tema
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
        {
            var w = (Window)s;
            w.Icon ??= AppIcon;
            PaintTitleBar(w);
        }));
        new MainWindow().Show();
    }

    static readonly System.Windows.Media.ImageSource AppIcon =
        System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/icon.ico"));

    // Windows 11: pinta a barra de título com o fundo/texto do tema (em vez da cor de destaque do
    // sistema), mantendo os botões nativos. Em versões antigas a chamada só falha e fica o padrão.
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    static int ColorRef(string hex)
    {
        var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        return c.R | (c.G << 8) | (c.B << 16); // COLORREF = 0x00BBGGRR
    }

    public static void PaintTitleBar(Window w)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        var p = Theme.Current;
        int bg = ColorRef(p.Bg), text = ColorRef(p.Text), border = ColorRef(p.Border), dark = p.Dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref dark, 4);   // DWMWA_USE_IMMERSIVE_DARK_MODE (botões nativos claros no escuro)
        DwmSetWindowAttribute(hwnd, 35, ref bg, 4);     // DWMWA_CAPTION_COLOR
        DwmSetWindowAttribute(hwnd, 36, ref text, 4);   // DWMWA_TEXT_COLOR
        DwmSetWindowAttribute(hwnd, 34, ref border, 4); // DWMWA_BORDER_COLOR
    }
}
