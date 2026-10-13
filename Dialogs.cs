using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MtgEstoque;

public abstract class Dialog : Window
{
    protected Dialog(string title, double w, double h)
    {
        Title = title; Width = w; Height = h;
        SetResourceReference(BackgroundProperty, "Bg");
        SetResourceReference(ForegroundProperty, "Text");
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"); FontSize = 13;
        Owner = Application.Current.MainWindow; WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    protected static StackPanel Field(string label, UIElement input)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        sp.Children.Add(new TextBlock { Text = label, Foreground = Theme.B("Muted"), Margin = new Thickness(0, 0, 0, 4) });
        sp.Children.Add(input);
        return sp;
    }

    protected StackPanel Buttons(string okText = "Salvar")
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = okText, Style = (Style)Application.Current.FindResource("Primary") };
        ok.Click += (_, _) => DialogResult = true;
        sp.Children.Add(new Button { Content = "Cancelar", IsCancel = true });
        sp.Children.Add(ok);
        return sp;
    }

    protected static ComboBox Sectors(IEnumerable<string> items) =>
        new() { IsEditable = true, ItemsSource = items.ToList(), Padding = new Thickness(6, 5, 6, 5) };
}

public class ListDialog : Dialog
{
    public readonly TextBox Body = new(), NameBox = new(), LinkBox = new();
    public readonly CheckBox Check = new();
    public readonly ComboBox SectorBox;

    // name != null: campo de nome; link: campo de link do Moxfield; sectors != null: campo de setor; check != null: checkbox.
    public ListDialog(string title, string body, string? name = null, string? check = null, bool checkDefault = false,
        IEnumerable<string>? sectors = null, bool link = false)
        : base(title, 520, 660)
    {
        SectorBox = Sectors(sectors ?? Array.Empty<string>());
        var root = new DockPanel { Margin = new Thickness(20) };
        void top(UIElement e) { DockPanel.SetDock(e, Dock.Top); root.Children.Add(e); }
        if (name != null) { NameBox.Text = name; top(Field(link ? "Nome (vazio = nome do Moxfield)" : "Nome", NameBox)); }
        if (link) top(Field("Link do Moxfield (opcional — substitui a lista abaixo)", LinkBox));
        if (sectors != null) top(Field("Setor (opcional)", SectorBox));

        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var buttons = Buttons();
        var open = new Button { Content = "Abrir arquivo…", ToolTip = "Lista .txt ou CSV exportado da coleção do Moxfield" };
        open.Click += (_, _) =>
        {
            var f = new Microsoft.Win32.OpenFileDialog { Filter = "Listas (*.txt;*.csv)|*.txt;*.csv|Todos (*.*)|*.*" };
            if (f.ShowDialog(this) == true) Body.Text = System.IO.File.ReadAllText(f.FileName);
        };
        buttons.Children.Insert(0, open);
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        if (check != null) { Check.Content = check; Check.IsChecked = checkDefault; Check.VerticalAlignment = VerticalAlignment.Center; bottom.Children.Add(Check); }
        else bottom.Children.Add(new TextBlock { Text = "Formato: 1 Sol Ring (FIC) 357 *F*", Foreground = Theme.B("Muted"), VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        Body.Text = body; Body.AcceptsReturn = true; Body.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        Body.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; Body.TextWrapping = TextWrapping.NoWrap;
        root.Children.Add(Body);
        Content = root;
        Loaded += (_, _) => (name == "" ? NameBox : Body).Focus();
    }
}

// Criar/renomear binder ou box. link: permite importar as cartas de um link do Moxfield.
public class LocDialog : Dialog
{
    public readonly TextBox NameBox = new(), LinkBox = new();
    public readonly ComboBox KindBox = new() { ItemsSource = new[] { "Binder", "Box" }, SelectedIndex = 0, Padding = new Thickness(6, 5, 6, 5) };

    readonly bool deck;

    // kind "deck": só o nome (deck não vira binder/box).
    public LocDialog(string title, string name = "", string kind = "binder", bool link = false) : base(title, 420, link ? 320 : kind == "deck" ? 190 : 250)
    {
        deck = kind == "deck";
        NameBox.Text = name;
        KindBox.SelectedIndex = kind == "box" ? 1 : 0;
        var sp = new StackPanel { Margin = new Thickness(20) };
        sp.Children.Add(Field(link ? "Nome (vazio = nome do Moxfield)" : "Nome", NameBox));
        if (!deck) sp.Children.Add(Field("Tipo", KindBox));
        if (link) sp.Children.Add(Field("Link do Moxfield (opcional — cria as cartas aqui)", LinkBox));
        sp.Children.Add(Buttons());
        Content = sp;
        Loaded += (_, _) => NameBox.Focus();
    }

    public string Kind => deck ? "deck" : KindBox.SelectedIndex == 1 ? "box" : "binder";
}

public class ReviewRow
{
    public string Card = "", Print = "", FreeWhere = "", OtherWhere = "";
    public bool Foil, Specified; // Specified: a lista dizia a arte
    public List<(string Print, bool Foil, int Qty, string Where)> FreeArts = new();
    public Dictionary<string, string> InUse = new(); // arte -> decks onde suas cópias estão em uso
    public bool HasArt => FreeArts.Any(a => a.Print == Print);
    public int Need, Free, Other;
    public Dictionary<string, int> OtherByDeck = new(); // deck -> cópias suas lá
    public string Action = ReviewDialog.Miss;

    public string? StealFrom => Action.StartsWith(ReviewDialog.StealPrefix) ? Action[ReviewDialog.StealPrefix.Length..] : null;
    public int FromFree => Action == ReviewDialog.Miss ? 0 : Math.Min(Free, Need);
    public int FromOther => StealFrom is string dk ? Math.Min(OtherByDeck.GetValueOrDefault(dk), Need - FromFree) : 0;
    public int Created => Action == ReviewDialog.Create ? Need - FromFree : 0;
    public int Missing => Need - FromFree - FromOther - Created;
}

// Revisão por carta do que falta num deck: de onde cada uma vem.
public class ReviewDialog : Dialog
{
    public const string Use = "Usar da coleção", StealPrefix = "Tirar de ", Create = "Tenho (criar cópia)", Miss = "Faltando";
    static Brush Green => Theme.B("Good");
    static Brush Amber => Theme.B("Warn");
    static Brush Red => Theme.B("DangerText");

    readonly List<ReviewRow> rows;
    readonly List<(ReviewRow row, ComboBox box)> boxes = new();
    readonly TextBlock summary = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };

    readonly bool haveDefault;

    // haveDefault: padrão "Tenho" (veio do botão "Já tenho").
    public ReviewDialog(string deck, List<ReviewRow> rows, bool haveDefault = false) : base($"{deck} — de onde vêm as cartas", 900, 680)
    {
        this.rows = rows;
        this.haveDefault = haveDefault;
        var root = new DockPanel { Margin = new Thickness(20) };
        void dock(UIElement e, Dock side) { DockPanel.SetDock(e, side); root.Children.Add(e); }

        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(summary);
        var bulk = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        void bulkBtn(string text, Func<ReviewRow, string> pick)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0) };
            b.Click += (_, _) => { foreach (var (r, box) in boxes) box.SelectedItem = pick(r); };
            bulk.Children.Add(b);
        }
        bulkBtn("Padrão", Default);
        bulkBtn("Tenho as que faltam", r => Create);
        bulkBtn("Tirar tudo de outros decks", r => r.OtherByDeck.Count > 0 ? StealPrefix + r.OtherByDeck.OrderByDescending(x => x.Value).First().Key : Default(r));
        bulkBtn("Tudo faltando", r => Miss);
        top.Children.Add(bulk);
        dock(top, Dock.Top);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "Confirmar", Style = (Style)Application.Current.FindResource("Primary") };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(new Button { Content = "Pular (deixar faltando)", IsCancel = true });
        buttons.Children.Add(ok);
        dock(buttons, Dock.Bottom);

        var list = new StackPanel();
        list.Children.Add(Line(new TextBlock { Text = "Carta", Foreground = Theme.B("Muted") }, new TextBlock { Text = "Arte", Foreground = Theme.B("Muted") }, new TextBlock { Text = "Qtd", Foreground = Theme.B("Muted") },
            new TextBlock { Text = "Onde está", Foreground = Theme.B("Muted") }, new TextBlock { Text = "Ação", Foreground = Theme.B("Muted") }));
        // ordem: o que precisa de decisão primeiro (outros decks), depois faltando, depois o que já resolve sozinho
        foreach (var r in rows.OrderBy(r => r.Free >= r.Need ? 2 : r.Other > 0 ? 0 : 1).ThenBy(r => r.Card))
        {
            var opts = new List<string>();
            if (r.Free > 0) opts.Add(Use);
            foreach (var dk in r.OtherByDeck.Keys) opts.Add(StealPrefix + dk);
            opts.Add(Create); opts.Add(Miss);
            var box = new ComboBox { ItemsSource = opts, SelectedItem = Default(r), Padding = new Thickness(6, 4, 6, 4) };
            box.SelectionChanged += (_, _) => { r.Action = (string)box.SelectedItem; Refresh(); };
            r.Action = Default(r);
            boxes.Add((r, box));

            var art = new Button { Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 4, 8, 4), ToolTip = "Escolher arte: uma cópia que você tem, ou outra" };
            var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var whereText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

            void update()
            {
                bool wrongArt = r.Specified && r.Free > 0 && !r.HasArt;
                art.Content = ArtLabel(r.Print) + (r.Foil ? " ✦" : "") + (r.FreeArts.Count > 0 ? "  ▾" : "");
                name.Inlines.Clear();
                name.Inlines.Add(new System.Windows.Documents.Run("● ") { Foreground = r.Free >= r.Need && !wrongArt ? Green : r.Free > 0 || r.Other > 0 ? Amber : Red });
                name.Inlines.Add(new System.Windows.Documents.Run(r.Card));
                var where = new List<string>();
                if (wrongArt) where.Add($"Não tem esta arte · livre em outra: {r.FreeWhere}");
                else if (r.Free > 0) where.Add($"Livre: {r.FreeWhere}");
                if (r.Other > 0) where.Add($"Tem, mas usando em: {r.OtherWhere}");
                if (where.Count == 0) where.Add("Não tenho");
                whereText.Text = string.Join("   ", where);
                whereText.ToolTip = string.Join("\n", where);
                whereText.Foreground = wrongArt || (r.Free < r.Need && r.Other > 0) ? Amber : Theme.B("Muted");
            }

            async Task pickOther()
            {
                try
                {
                    var pick = new ArtPicker(r.Card, r.Print == "" ? null : r.Print, r.FreeArts.Select(a => a.Print), r.InUse) { Owner = this };
                    if (pick.ShowDialog() == true) { r.Print = pick.Chosen!; r.Specified = true; }
                }
                catch { }
                update();
            }

            // menu com as cópias livres que você tem; "Outra arte…" abre todas as impressões
            art.Click += async (_, _) =>
            {
                if (r.FreeArts.Count == 0) { await pickOther(); return; }
                var menu = new ContextMenu { PlacementTarget = art };
                menu.Items.Add(new MenuItem { Header = "Que você tem:", IsEnabled = false });
                foreach (var a in r.FreeArts)
                {
                    var mi = new MenuItem { Header = $"{ArtLabel(a.Print)}{(a.Foil ? " ✦" : "")}  ·  {a.Qty}x  ·  {a.Where}", IsChecked = a.Print == r.Print && a.Foil == r.Foil };
                    HoverPreview(mi, r.Card, a.Print);
                    mi.Click += (_, _) => { r.Print = a.Print; r.Foil = a.Foil; update(); };
                    menu.Items.Add(mi);
                }
                menu.Items.Add(new Separator());
                var other = new MenuItem { Header = "Outra arte…" };
                other.Click += async (_, _) => await pickOther();
                menu.Items.Add(other);
                menu.IsOpen = true;
            };

            update();
            list.Children.Add(Line(name, art, new TextBlock { Text = r.Need.ToString(), VerticalAlignment = VerticalAlignment.Center }, whereText, box));
        }
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        Refresh();
    }

    string Default(ReviewRow r) => haveDefault ? Create : r.Free > 0 ? Use : Miss;

    // Mostra a carta ao passar o mouse; a imagem só é baixada quando o tooltip abre.
    static void HoverPreview(FrameworkElement el, string card, string print)
    {
        var img = new Image { Width = 244, Height = 340 };
        el.ToolTip = new ToolTip { Content = img, Placement = System.Windows.Controls.Primitives.PlacementMode.Right, Padding = new Thickness(4) };
        ToolTipService.SetInitialShowDelay(el, 150);
        el.ToolTipOpening += async (_, _) =>
        {
            if (img.Source != null) return;
            try { img.Source = Img.Load(await Img.Get(card, print == "" ? null : print), 244); } catch { }
        };
    }

    static string ArtLabel(string print) => print == "" ? "Padrão" : print.Replace('/', ' ').ToUpper();

    static Grid Line(params UIElement[] items)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 8, 6) };
        foreach (var w in new[] { new GridLength(1.2, GridUnitType.Star), new GridLength(130), new GridLength(44), new GridLength(1.4, GridUnitType.Star), new GridLength(190) })
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        for (int i = 0; i < items.Length; i++) { Grid.SetColumn(items[i], i); g.Children.Add(items[i]); }
        return g;
    }

    void Refresh()
    {
        int free = rows.Sum(r => r.FromFree), other = rows.Sum(r => r.FromOther), created = rows.Sum(r => r.Created), missing = rows.Sum(r => r.Missing);
        var parts = new List<string> { $"{free} vêm da coleção" };
        if (other > 0) parts.Add($"{other} de outros decks");
        if (created > 0) parts.Add($"{created} novas");
        parts.Add($"{missing} faltam");
        summary.Text = string.Join("  ·  ", parts);
    }
}

// Mover: tipo de destino (Deck / Binder ou box) -> destino -> setor/quantidade.
public class MoveDialog : Dialog
{
    readonly Data d;
    readonly string? current;
    readonly Button deckTab = new() { Content = "Deck", Margin = new Thickness(0, 0, 6, 0) };
    readonly Button storeTab = new() { Content = "Binder / Box", Margin = new Thickness(0) };
    readonly ListBox list = new() { Height = 230, BorderThickness = new Thickness(1), BorderBrush = Theme.B("Border"), Padding = new Thickness(4) };
    readonly ComboBox sector;
    readonly TextBox qty = new();
    readonly StackPanel sectorField;
    readonly Button ok;
    bool decks;

    // maxQty > 0: deixa escolher quantas cópias mover (uma pilha só selecionada).
    public MoveDialog(Data d, string? current, int maxQty) : base("Mover", 420, maxQty > 0 ? 560 : 500)
    {
        this.d = d; this.current = current;
        sector = Sectors(Array.Empty<string>());
        var sp = new StackPanel { Margin = new Thickness(20) };

        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        tabs.Children.Add(deckTab); tabs.Children.Add(storeTab);
        sp.Children.Add(Field("Para", tabs));
        deckTab.Click += (_, _) => Show(true);
        storeTab.Click += (_, _) => Show(false);

        sp.Children.Add(list);
        list.SelectionChanged += (_, _) => Refresh();
        list.MouseDoubleClick += (_, _) => { if (Dest != "") DialogResult = true; };

        sectorField = Field("Setor (opcional)", sector);
        sectorField.Margin = new Thickness(0, 12, 0, 0);
        sp.Children.Add(sectorField);
        if (maxQty > 0) { qty.Text = maxQty.ToString(); var f = Field($"Quantidade (máx. {maxQty})", qty); f.Margin = new Thickness(0, 12, 0, 0); sp.Children.Add(f); }

        var buttons = Buttons("Mover");
        buttons.Margin = new Thickness(0, 14, 0, 0);
        ok = (Button)buttons.Children[1];
        sp.Children.Add(buttons);
        Content = sp;

        // abre na aba "do outro lado": de um deck sugere binders; de binder sugere decks
        Show(current == null || !Store.IsDeck(d, current));
    }

    void Show(bool decks)
    {
        this.decks = decks;
        var primary = (Style)Application.Current.FindResource("Primary");
        var normal = (Style)Application.Current.FindResource(typeof(Button));
        deckTab.Style = decks ? primary : normal;
        storeTab.Style = decks ? normal : primary;
        list.ItemsSource = d.Locations.Where(l => (l.Kind == "deck") == decks && (current == null || !Store.Same(l.Name, current)))
            .Select(l => new ListBoxItem { Content = l.Name, Tag = l.Name, Padding = new Thickness(8, 6, 8, 6) }).ToList();
        Refresh();
    }

    void Refresh()
    {
        Vis(sectorField, !decks && Dest != "");
        ok.IsEnabled = Dest != "";
        if (Dest != "")
            sector.ItemsSource = Store.SectorsOf(d, Store.Loc(d, Dest));
    }

    static void Vis(UIElement e, bool on) => e.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

    public string Dest => (list.SelectedItem as ListBoxItem)?.Tag as string ?? "";
    public string Sector => sector.Text.Trim();
    public int? Qty => int.TryParse(qty.Text, out var n) && n > 0 ? n : null;
}

// Confirmação no estilo do app: botão nomeado pela ação ("Remover 3 cópias"), não Sim/Não.
public class ConfirmDialog : Dialog
{
    public ConfirmDialog(string title, string message, string yes, bool danger, bool cancel) : base(title, 440, 200)
    {
        SizeToContent = SizeToContent.Height;
        var sp = new StackPanel { Margin = new Thickness(22, 20, 22, 18) };
        sp.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        sp.Children.Add(new TextBlock { Text = message, Foreground = Theme.B("Muted"), Margin = new Thickness(0, 6, 0, 18), TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (cancel) buttons.Children.Add(new Button { Content = "Cancelar", IsCancel = true, Margin = new Thickness(0) });
        var ok = new Button { Content = yes, Style = (Style)Application.Current.FindResource(danger ? "Danger" : "Primary"), IsDefault = true, IsCancel = !cancel };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(ok);
        sp.Children.Add(buttons);
        Content = sp;
    }

    public static bool Ask(Window owner, string title, string message, string yes, bool danger = false) =>
        new ConfirmDialog(title, message, yes, danger, true) { Owner = owner }.ShowDialog() == true;

    public static void Info(Window owner, string title, string message) =>
        new ConfirmDialog(title, message, "Entendi", false, false) { Owner = owner }.ShowDialog();
}

// Pergunta um texto (nome de setor etc.).
public class PromptDialog : Dialog
{
    public readonly TextBox Box = new();

    public PromptDialog(string title, string label, string value = "") : base(title, 380, 190)
    {
        Box.Text = value;
        var sp = new StackPanel { Margin = new Thickness(20) };
        sp.Children.Add(Field(label, Box));
        var b = Buttons();
        ((Button)b.Children[1]).IsDefault = true;
        sp.Children.Add(b);
        Content = sp;
        Loaded += (_, _) => { Box.Focus(); Box.SelectAll(); };
    }

    public string Value => Box.Text.Trim();
}

// Excluir deck/binder/box: o que fazer com as cartas físicas.
public class DeleteDialog : Dialog
{
    public bool RemoveCards;
    public string? Target; // pra onde vão as cartas ao desmontar
    readonly ComboBox dest = new() { Padding = new Thickness(6, 5, 6, 5), MinWidth = 200 };

    public DeleteDialog(Data d, Location l, int copies) : base("Excluir", 520, copies > 0 ? 330 : 190)
    {
        var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        sp.Children.Add(new TextBlock { Text = $"Excluir \"{l.Name}\"?", FontSize = 16, FontWeight = FontWeights.SemiBold });
        sp.Children.Add(new TextBlock
        {
            Text = copies == 0 ? "Não tem nenhuma cópia física aqui." : $"Tem {copies} {(copies == 1 ? "cópia física" : "cópias físicas")} aqui. O que aconteceu com elas?",
            Foreground = Theme.B("Muted"), Margin = new Thickness(0, 6, 0, 16), TextWrapping = TextWrapping.Wrap,
        });

        if (copies > 0)
        {
            // Desmontei: escolhe o binder/box (o padrão já vem selecionado)
            var others = d.Locations.Where(x => x.Kind != "deck" && x != l).Select(x => x.Name).ToList();
            if (others.Count == 0) others.Add(Store.DefaultName);
            dest.ItemsSource = others;
            dest.SelectedItem = others.FirstOrDefault(x => d.DefaultLoc != null && Store.Same(x, d.DefaultLoc)) ?? others[0];
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var keep = new Button { Content = "Desmontei — mover", Style = (Style)Application.Current.FindResource("Primary"), Margin = new Thickness(10, 0, 0, 0), MinWidth = 150 };
            keep.Click += (_, _) => { Target = dest.SelectedItem as string; DialogResult = true; };
            DockPanel.SetDock(keep, Dock.Right);
            row.Children.Add(keep);
            var lbl = new TextBlock { Text = "para", Foreground = Theme.B("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(lbl, Dock.Left);
            row.Children.Add(lbl);
            row.Children.Add(dest);
            sp.Children.Add(row);

            var sold = new Button { Content = "Vendi — remover da coleção", Style = (Style)Application.Current.FindResource("Danger"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0), MinWidth = 150 };
            sold.Click += (_, _) =>
            {
                if (!ConfirmDialog.Ask(this, "Remover da coleção?", $"As {copies} cópias saem da coleção de vez (vendidas/trocadas).", $"Remover {copies} cópias", danger: true)) return;
                RemoveCards = true; DialogResult = true;
            };
            sp.Children.Add(sold);
            sp.Children.Add(new Border { Height = 1, Background = Theme.B("Border"), Margin = new Thickness(0, 16, 0, 12) });
        }

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        bottom.Children.Add(new Button { Content = "Cancelar", IsCancel = true, Margin = new Thickness(0) });
        if (copies == 0)
        {
            var del = new Button { Content = "Excluir", Style = (Style)Application.Current.FindResource("Danger") };
            del.Click += (_, _) => DialogResult = true;
            bottom.Children.Add(del);
        }
        sp.Children.Add(bottom);
        Content = sp;
    }
}

// Carta adicionada ao deck que você já tem livre: mostra de onde ela vem antes de puxar.
public class SourceDialog : Dialog
{
    public Stack? Chosen;
    readonly ListBox list = new() { BorderThickness = new Thickness(1), BorderBrush = Theme.B("Border"), Padding = new Thickness(4), MaxHeight = 220 };

    public SourceDialog(string card, List<Stack> free) : base(card, 480, 360)
    {
        var sp = new StackPanel { Margin = new Thickness(20) };
        sp.Children.Add(new TextBlock { Text = $"Você tem {card} livre", FontSize = 15, FontWeight = FontWeights.SemiBold });
        sp.Children.Add(new TextBlock { Text = "Escolha de onde pegar a cópia (fica registrado em Movimentações):", Foreground = Theme.B("Muted"), Margin = new Thickness(0, 4, 0, 12), TextWrapping = TextWrapping.Wrap });
        list.ItemsSource = free.Select(s => new ListBoxItem
        {
            Tag = s, Padding = new Thickness(8, 6, 8, 6),
            Content = $"{Store.Place(s.Loc, s.Sector)}   ·   {(s.Print == "" ? "Padrão" : s.Print.Replace('/', ' ').ToUpper())}{(s.Foil ? " ✦" : "")}   ·   {s.Qty}x",
        }).ToList();
        list.SelectedIndex = 0;
        list.MouseDoubleClick += (_, _) => Pick();
        sp.Children.Add(list);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = "Pegar desta", Style = (Style)Application.Current.FindResource("Primary") };
        ok.Click += (_, _) => Pick();
        buttons.Children.Add(new Button { Content = "Deixar faltando", IsCancel = true });
        buttons.Children.Add(ok);
        sp.Children.Add(buttons);
        Content = sp;
    }

    void Pick()
    {
        Chosen = (list.SelectedItem as ListBoxItem)?.Tag as Stack;
        DialogResult = true;
    }
}

// Mover pra um deck que já tem a mesma carta em outra arte: mostra as duas artes lado a lado.
public class SwapDialog : Dialog
{
    public bool Swap;

    public SwapDialog(string from, string dest, List<(string Card, string Mine, string Theirs)> pairs)
        : base("Mover", 560, Math.Min(250 + pairs.Count * 290, 760))
    {
        var gray = Theme.B("Muted");
        var root = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };

        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        head.Children.Add(new TextBlock { Text = $"{dest} já tem {(pairs.Count == 1 ? "essa carta" : "essas cartas")} em outra arte", FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        head.Children.Add(new TextBlock { Text = "Trocar as artes entre os dois, ou só mover?", Foreground = gray, Margin = new Thickness(0, 4, 0, 0) });
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var move = new Button { Content = "Só mover" };
        var swap = new Button { Content = "Trocar artes", Style = (Style)Application.Current.FindResource("Primary") };
        move.Click += (_, _) => { Swap = false; DialogResult = true; };
        swap.Click += (_, _) => { Swap = true; DialogResult = true; };
        buttons.Children.Add(new Button { Content = "Cancelar", IsCancel = true });
        buttons.Children.Add(move);
        buttons.Children.Add(swap);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var list = new StackPanel();
        foreach (var (card, mine, theirs) in pairs)
        {
            if (pairs.Count > 1) list.Children.Add(new TextBlock { Text = card, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 8), HorizontalAlignment = HorizontalAlignment.Center });
            var g = new Grid { Margin = new Thickness(0, 0, 0, 14), HorizontalAlignment = HorizontalAlignment.Center };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(Side(card, mine, from, 0, gray));
            var icon = new TextBlock
            {
                Text = "\uE8AB", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 20, Foreground = gray,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 34),
            };
            Grid.SetColumn(icon, 1);
            g.Children.Add(icon);
            g.Children.Add(Side(card, theirs, dest, 2, gray));
            list.Children.Add(g);
        }
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }

    static StackPanel Side(string card, string print, string where, int col, Brush gray)
    {
        var img = new Image { Width = 170, Height = 237, Stretch = Stretch.Uniform };
        var border = new Border { CornerRadius = new CornerRadius(8), Background = Theme.B("Selected"), Width = 170, Height = 237, Child = img };
        _ = Load(img, card, print);
        var sp = new StackPanel();
        sp.Children.Add(border);
        sp.Children.Add(new TextBlock { Text = where, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170 });
        sp.Children.Add(new TextBlock { Text = print == "" ? "Padrão" : print.Replace('/', ' ').ToUpper(), Foreground = gray, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 });
        Grid.SetColumn(sp, col);
        return sp;
    }

    static async Task Load(Image img, string card, string print)
    {
        try { img.Source = Img.Load(await Img.Get(card, print == "" ? null : print), 340); } catch { }
    }
}

// Escolher impressão: as que você tem livres primeiro ("Já tem"), as que estão em uso em outro deck
// ("Usando", não selecionáveis), depois as do Scryfall, 60 por vez.
public class ArtPicker : Dialog
{
    const int Step = 60;
    public string? Chosen;
    bool closed;
    readonly string card;
    readonly string? current;
    readonly HashSet<string> owned;
    readonly Dictionary<string, string> inUse;
    readonly WrapPanel wrap = new() { Margin = new Thickness(14) };
    readonly Button more = new() { Content = "Carregar mais", Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
    Img.ArtFeed? feed;
    int shown;

    // have: artes que você pode usar aqui; inUse: arte -> decks onde está em uso (excluídas as de 'have').
    public ArtPicker(string card, string? current, IEnumerable<string> have, Dictionary<string, string>? inUse = null) : base(card, 940, 720)
    {
        FontSize = 12;
        this.card = card; this.current = current;
        owned = have.Where(p => p != "").ToHashSet();
        this.inUse = (inUse ?? new()).Where(x => x.Key != "" && !owned.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value);
        Closed += (_, _) => closed = true;
        var root = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(more, Dock.Bottom);
        root.Children.Add(more);
        root.Children.Add(new ScrollViewer { Content = wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        more.Click += async (_, _) => await ShowMore();

        foreach (var p in owned) AddTile(Img.SmallOf(p), true);
        foreach (var (p, where) in this.inUse) AddTile(Img.SmallOf(p), false, where);
        Loaded += async (_, _) =>
        {
            try { feed = await Img.Feed(card); await ShowMore(); }
            catch { Title = $"{card} — sem conexão"; }
        };
    }

    async Task ShowMore()
    {
        more.IsEnabled = false;
        int target = shown + Step;
        while (feed!.Arts.Count < target && feed.Next != null) await feed.More();
        foreach (var a in feed.Arts.Skip(shown).Take(Step)) if (!owned.Contains(a.Print) && !inUse.ContainsKey(a.Print)) AddTile(a, false);
        shown = Math.Min(target, feed.Arts.Count);
        Title = $"{card} — {feed.Total} impressões" + (owned.Count > 0 ? $" · você tem {owned.Count}" : "");
        more.Visibility = shown < feed.Arts.Count || feed.Next != null ? Visibility.Visible : Visibility.Collapsed;
        more.IsEnabled = true;
    }

    void AddTile(Img.Art a, bool have, string? usedIn = null)
    {
        var img = new Image { Width = 146, Height = 204 };
        var sp = new StackPanel();
        sp.Children.Add(img);
        var caption = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        caption.Children.Add(new TextBlock { Text = a.Print.Replace('/', ' ').ToUpper(), Foreground = Theme.B("Muted"), VerticalAlignment = VerticalAlignment.Center });
        if (have)
            caption.Children.Add(new Border
            {
                Background = Theme.B("GoodSoft"), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(6, 0, 0, 0),
                Child = new TextBlock { Text = "Já tem", FontSize = 11, Foreground = Theme.B("Good") },
            });
        if (usedIn != null)
            caption.Children.Add(new Border
            {
                Background = Theme.B("WarnSoft"), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(6, 0, 0, 0),
                Child = new TextBlock { Text = "Usando", FontSize = 11, Foreground = Theme.B("Warn") },
            });
        sp.Children.Add(caption);
        var b = new Button { Content = sp, Padding = new Thickness(6), Margin = new Thickness(4) };
        b.Background = a.Print == current ? Theme.B("Selected") : Brushes.Transparent;
        if (usedIn != null)
        {
            // cópia em uso em outro deck: não dá pra escolher aqui (duplicaria a carta)
            b.Opacity = 0.6;
            b.ToolTip = $"Tem, mas está em uso em: {usedIn}";
            b.Click += (_, _) => ConfirmDialog.Info(this, $"Em uso em {usedIn}", "Pra trazer essa cópia, use \"Tirar de outro deck\" na revisão ou \"Mover…\" no deck de origem.");
        }
        else b.Click += (_, _) => { Chosen = a.Print; DialogResult = true; };
        wrap.Children.Add(b);
        _ = LoadThumb(img, a);
    }

    async Task LoadThumb(Image img, Img.Art a)
    {
        if (closed) return;
        try { var path = await Img.Thumb(a); if (!closed) img.Source = Img.Load(path, 146); }
        catch { }
    }
}
