using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MtgEstoque;

// Importação das compras da LigaMagic (texto copiado pelo favorito "Copiar compras").
// A Liga dá nome, edição (com prefixo de variante: "bfcmm" = CMM borderless) e o scan da carta, mas não o
// número de coleção. A impressão certa sai da edição; se a edição tem várias impressões da carta, comparo o
// scan com a imagem de cada uma (só a ilustração, que não muda com o idioma) e fico com a mais parecida.
public static class Liga
{
    public record Item(int Qty, string Name, string Ed, bool Foil, string Img);
    public record Match(Item Item, string Print, bool Sure, string Why);

    public static bool IsLiga(string text) => text.TrimStart().StartsWith("#ligamagic", StringComparison.OrdinalIgnoreCase);

    public static List<Item> Parse(string text) =>
        text.Replace("\r", "").Split('\n').Skip(1).Select(Fields).Where(f => f.Length >= 3 && f[1].Trim() != "")
            .Select(f => new Item(int.TryParse(f[0], out var n) && n > 0 ? n : 1, f[1].Trim(), f[2].Trim().ToLowerInvariant(),
                                  f.Length > 3 && f[3].Trim() == "foil", f.Length > 4 ? f[4].Trim() : ""))
            .ToList();

    // Às vezes o TAB vira espaço no caminho (clipboard/caixa de texto): aí lê de trás pra frente
    // "qtd nome… ed [foil] [url]".
    static string[] Fields(string l)
    {
        if (l.Contains('\t')) return l.Split('\t');
        var w = l.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (w.Count < 3) return [];
        var img = w[^1].StartsWith("http") ? w[^1] : ""; if (img != "") w.RemoveAt(w.Count - 1);
        var foil = w[^1] == "foil" ? "foil" : ""; if (foil != "") w.RemoveAt(w.Count - 1);
        if (w.Count < 3) return [];
        return [w[0], string.Join(' ', w[1..^1]), w[^1], foil, img];
    }

    // "bfcmm" -> "bfcmm", "fcmm", "cmm": o código do Scryfall costuma ser um sufixo do código da Liga.
    // Depois, os códigos com uma letra a menos ("pwp22" -> "pw22").
    public static IEnumerable<string> SetGuesses(string ed)
    {
        var seen = new HashSet<string>();
        for (int i = 0; i < ed.Length - 1; i++) if (seen.Add(ed[i..])) yield return ed[i..];
        for (int i = 0; i < ed.Length; i++) { var s = ed.Remove(i, 1); if (s.Length >= 3 && seen.Add(s)) yield return s; }
    }

    static readonly HttpClient Http = new() { DefaultRequestHeaders = { { "User-Agent", "MtgEstoque/1.0" } } };

    public static async Task<Match> Resolve(Item it)
    {
        Img.ArtFeed feed;
        try { feed = await Img.Feed(it.Name); while (feed.Next != null) await feed.More(); }
        catch { return new Match(it, "", false, "não achei no Scryfall"); }

        List<Img.Art>? cands = null;
        foreach (var s in SetGuesses(it.Ed))
            if (feed.Arts.Where(a => a.Print.Split('/')[0] == s).ToList() is { Count: > 0 } c) { cands = c; break; }
        bool setOk = cands != null;
        cands ??= feed.Arts.Take(40).ToList();
        if (cands.Count == 0) return new Match(it, "", false, "sem impressões");
        if (cands.Count == 1) return new Match(it, cands[0].Print, setOk, setOk ? "única impressão da edição" : "edição não reconhecida");
        if (it.Img == "") return new Match(it, cands[0].Print, false, $"{cands.Count} impressões, sem imagem pra comparar");

        try
        {
            var liga = Fingerprint(await Http.GetByteArrayAsync(it.Img));
            var scores = new List<(Img.Art art, double d)>();
            foreach (var a in cands) scores.Add((a, Distance(liga, Fingerprint(await File.ReadAllBytesAsync(await Img.Thumb(a))))));
            scores.Sort((x, y) => x.d.CompareTo(y.d));
            double best = scores[0].d, second = scores[1].d;
            // bem parecida e bem melhor que a segunda = certeza; senão, marca pra conferir
            bool sure = setOk && best < 15 && second - best > 6;
            return new Match(it, scores[0].art.Print, sure, sure ? $"imagem igual ({cands.Count} impressões)" : $"parecida, confira ({cands.Count} impressões)");
        }
        catch { return new Match(it, cands[0].Print, false, "não consegui comparar a imagem"); }
    }

    // Miniatura 12x17 em tons de cinza da carta inteira: nesse tamanho o texto (que muda com o idioma) some,
    // mas a ilustração e a moldura continuam — e é a moldura que separa arte estendida/borderless da comum.
    const int FW = 12, FH = 17;

    static byte[] Fingerprint(byte[] bytes)
    {
        var bi = new BitmapImage();
        bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = new MemoryStream(bytes); bi.DecodePixelWidth = 244; bi.EndInit();
        var gray = new FormatConvertedBitmap(bi, PixelFormats.Gray8, null, 0);
        int gw = gray.PixelWidth, gh = gray.PixelHeight;
        var px = new byte[gw * gh];
        gray.CopyPixels(px, gw, 0);
        var o = new byte[FW * FH];
        for (int y = 0; y < FH; y++)
            for (int x = 0; x < FW; x++)
            {
                // média do bloco correspondente (reduz ruído de compressão)
                int x0 = x * gw / FW, x1 = Math.Max(x0 + 1, (x + 1) * gw / FW), y0 = y * gh / FH, y1 = Math.Max(y0 + 1, (y + 1) * gh / FH);
                int sum = 0, n = 0;
                for (int yy = y0; yy < y1; yy++) for (int xx = x0; xx < x1; xx++) { sum += px[yy * gw + xx]; n++; }
                o[y * FW + x] = (byte)(sum / n);
            }
        return o;
    }

    static double Distance(byte[] a, byte[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += Math.Abs(a[i] - b[i]);
        return s / a.Length;
    }

    public static bool SelfTest()
    {
        var items = Parse("#ligamagic\n1\tSol Ring\tbfcmm\t\thttps://x/a.jpg\n2\tShared Roots\ttla\tfoil\t\n");
        return items.Count == 2 && items[0].Ed == "bfcmm" && !items[0].Foil && items[1].Qty == 2 && items[1].Foil
            && SetGuesses("bfcmm").Contains("cmm") && SetGuesses("ofmh2").Contains("mh2") && SetGuesses("cbkhm").Contains("khm") && SetGuesses("pwp22").Contains("pw22")
            && IsLiga("#ligamagic\n") && !IsLiga("1 Sol Ring")
            && Parse("#ligamagic\n1 Hunter's Insight cmm  https://x/a.jpg\n1 Shared Roots tla foil https://x/b.jpg") is [{ Name: "Hunter's Insight", Ed: "cmm", Foil: false, Img: "https://x/a.jpg" }, { Name: "Shared Roots", Foil: true }];
    }
}

// Conferência da importação: bolinha azul = identificada com certeza; amarela = confira a arte.
public class LigaImportDialog : Dialog
{
    readonly List<Liga.Match> matches;
    public readonly List<string> Prints;

    public LigaImportDialog(string target, List<Liga.Match> matches) : base($"Importar compra em {target}", 760, 640)
    {
        this.matches = matches;
        Prints = matches.Select(m => m.Print).ToList();
        int unsure = matches.Count(m => !m.Sure);
        var root = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        head.Children.Add(new TextBlock { Text = $"{matches.Sum(m => m.Item.Qty)} cartas da LigaMagic", FontSize = 16, FontWeight = FontWeights.SemiBold });
        head.Children.Add(new TextBlock
        {
            Text = unsure == 0 ? "Todas as artes identificadas." : $"{unsure} pra conferir — passe o mouse pra comparar, clique na arte pra trocar",
            Foreground = Theme.B("Muted"), Margin = new Thickness(0, 4, 0, 0),
        });
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "Adicionar", Style = (Style)Application.Current.FindResource("Primary") };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(new Button { Content = "Cancelar", IsCancel = true });
        buttons.Children.Add(ok);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        // artes que o scan não decidiu ficam em cima
        var list = new StackPanel();
        void section(string title, bool sure)
        {
            var idx = Enumerable.Range(0, matches.Count).Where(i => matches[i].Sure == sure).ToList();
            if (idx.Count == 0) return;
            list.Children.Add(new TextBlock { Text = $"{title} · {idx.Count}", FontWeight = FontWeights.SemiBold, Foreground = Theme.B(sure ? "Muted" : "Text"), Margin = new Thickness(0, list.Children.Count == 0 ? 0 : 14, 0, 8) });
            foreach (var i in idx) list.Children.Add(Row(i));
        }
        section("Requerem confirmação manual", false);
        section("Identificadas", true);
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }

    UIElement Row(int i)
    {
        var m = matches[i];
        var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var w in new[] { new GridLength(20), new GridLength(30), new GridLength(1, GridUnitType.Star), new GridLength(130), new GridLength(1, GridUnitType.Star) })
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        void put(UIElement e, int col) { Grid.SetColumn(e, col); g.Children.Add(e); }
        put(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = Theme.B(m.Sure ? "DotBlue" : "DotAmber"), VerticalAlignment = VerticalAlignment.Center, ToolTip = m.Why }, 0);
        put(new TextBlock { Text = m.Item.Qty.ToString(), Foreground = Theme.B("Muted"), VerticalAlignment = VerticalAlignment.Center }, 1);
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.Inlines.Add(new System.Windows.Documents.Run(m.Item.Name));
        if (m.Item.Foil) name.Inlines.Add(new System.Windows.Documents.Run("  ✦") { Foreground = Theme.B("Foil"), FontWeight = FontWeights.Bold });
        put(name, 2);
        string label(string p) => p == "" ? "escolher" : p.Replace('/', ' ').ToUpper();
        var art = new Button { Content = label(Prints[i]), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 3, 8, 3), ToolTip = "Trocar a arte" };
        art.Click += (_, _) =>
        {
            var pick = new ArtPicker(m.Item.Name, Prints[i] == "" ? null : Prints[i], Array.Empty<string>()) { Owner = this };
            if (pick.ShowDialog() == true) { Prints[i] = pick.Chosen!; art.Content = label(Prints[i]); }
        };
        put(art, 3);
        put(new TextBlock { Text = m.Why, Foreground = Theme.B("Muted"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, 4);

        // passar o mouse: scan da Liga ao lado da arte escolhida (carrega só quando abre)
        g.Background = Brushes.Transparent;
        var liga = new Image { Width = 200 }; var ours = new Image { Width = 200 };
        var oursTitle = new TextBlock { Margin = new Thickness(0, 0, 0, 4) };
        var pair = new Grid();
        pair.ColumnDefinitions.Add(new ColumnDefinition()); pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); pair.ColumnDefinitions.Add(new ColumnDefinition());
        void cell(UIElement t, Image im, int c) { var p = new StackPanel(); p.Children.Add(t); p.Children.Add(im); Grid.SetColumn(p, c); pair.Children.Add(p); }
        cell(new TextBlock { Text = "Na LigaMagic", Margin = new Thickness(0, 0, 0, 4) }, liga, 0);
        cell(oursTitle, ours, 2);
        g.ToolTip = pair;
        ToolTipService.SetInitialShowDelay(g, 150);
        ToolTipService.SetShowDuration(g, 60000);
        string shown = "-";
        g.ToolTipOpening += async (_, _) =>
        {
            try { if (liga.Source == null && m.Item.Img != "") liga.Source = new BitmapImage(new Uri(m.Item.Img)); } catch { }
            if (shown == Prints[i]) return;
            shown = Prints[i];
            oursTitle.Text = shown == "" ? "Sem arte escolhida" : label(shown);
            ours.Source = null;
            try { if (shown != "") ours.Source = Img.Load(await Img.Get(m.Item.Name, shown), 200); } catch { }
        };
        return g;
    }
}
