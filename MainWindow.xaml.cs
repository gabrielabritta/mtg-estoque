using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace MtgEstoque;

public record DeckRow(string Grupo, string Board, string Carta, string Arte, string Foil, int Qtd, int Livres, int Falta, string Desired, Stack? S);
public record StackRow(string Setor, Brush? Dot, string Tip, string Carta, string Arte, string Foil, int Qtd, Stack S);
public record WantRow(Brush? Dot, string Tip, string Carta, int Falta, int Livres, int Comprar, string Decks);
public record HomeRow(string Carta, int Total, string Onde);
public record ArtRow(Brush? Dot, string Tip, string Carta, string Arte, string Foil, int Qtd, string Onde, string Print);
public record LogRow(string Dia, string Hora, string Carta, string Arte, int Qtd, string De, string Para, Moved M);

public partial class MainWindow : Window
{
    static readonly StringComparer CI = Store.CI;
    // cores do tema atual (redesenhado quando o tema muda)
    static Brush Tile => Theme.B("Surface");
    static Brush TileHover => Theme.B("Hover");
    static Brush Gray => Theme.B("Muted");
    static Brush SelBg => Theme.B("Selected");
    static Brush HoverBg => Theme.B("Hover");
    static Brush Hex(string h) => (Brush)new BrushConverter().ConvertFromString(h)!;

    Data d = Store.Load();
    const string AllTag = "*geral", LogTag = "*log";
    const int PageSize = 150;
    string? view; // null = Início; AllTag = Geral; senão, nome do local
    int page;
    Location? Cur => view is null or AllTag or LogTag ? null : Store.Loc(d, view);
    string Mode => view == AllTag ? "all" : view == LogTag ? "log" : Cur == null ? "home" : Cur.Kind == "deck" ? "deck" : "store";

    public MainWindow()
    {
        InitializeComponent();
        ContentRendered += async (_, _) => await Update.Check(this);
        // hover numa linha da tabela já mostra a carta no painel
        var rs = new Style(typeof(DataGridRow));
        rs.Setters.Add(new EventSetter(MouseEnterEvent, new MouseEventHandler((s, _) => ShowRow(((DataGridRow)s).Item))));
        rs.Setters.Add(new EventSetter(MouseLeaveEvent, new MouseEventHandler((_, _) => ShowSelected())));
        // botão direito numa linha não selecionada: seleciona ela antes de abrir o menu
        rs.Setters.Add(new EventSetter(PreviewMouseRightButtonDownEvent, new MouseButtonEventHandler((s, _) =>
        {
            var row = (DataGridRow)s;
            if (!row.IsSelected) { Table.SelectedItems.Clear(); row.IsSelected = true; }
        })));
        Table.RowStyle = rs;
        Table.ContextMenuOpening += (_, e) =>
        {
            if (Mode != "store" || SelectedStacks().Count == 0) { e.Handled = true; return; }
            Table.ContextMenu = StoreMenu();
        };
        // arrastar cartas (binder/box) para outro setor: solta num chip de setor ou numa carta do setor
        // movimentações: clique numa linha abre o histórico só daquela carta (só clique de verdade)
        Table.PreviewMouseLeftButtonUp += (_, e) => { if (Mode == "log" && logCard == null && RowUnder(e.OriginalSource) is LogRow lr) { logCard = lr.Carta; page = 0; Render(); } };
        Table.AllowDrop = true;
        Table.PreviewMouseLeftButtonDown += (_, e) => dragStart = e.GetPosition(Table);
        Table.PreviewMouseMove += Table_DragMove;
        Table.Drop += (_, e) =>
        {
            if (e.Data.GetData(DragFmt) is not List<Stack> stacks) return;
            if (RowUnder(e.OriginalSource) is StackRow target) MoveToSector(stacks, target.S.Sector);
        };
        RefreshNav();
        Theme.Changed += () =>
        {
            RefreshNav(view ?? "");
            Render();
            if (Welcome.Visibility == Visibility.Visible) BuildWelcome();
        };
        // boas-vindas só na primeira abertura: marca como visto já ao mostrar (fechar o app também conta)
        if (!Theme.Welcomed) { Theme.Welcomed = true; Theme.Save(); BuildWelcome(); Vis(Welcome, true); }
    }

    // ---------- aparência / boas-vindas ----------
    void Theme_Click(object s, RoutedEventArgs e) => new ThemeDialog { Owner = this }.ShowDialog();

    void CloseWelcome()
    {
        Theme.Welcomed = true;
        Theme.Save();
        Vis(Welcome, false);
    }

    // Primeira abertura: limpa, sem texto sobrando — tema + três jeitos de começar.
    void BuildWelcome()
    {
        var c = WelcomeContent;
        c.Children.Clear();
        TextBlock text(string t, double size, Brush color, FontWeight weight, Thickness margin) =>
            new() { Text = t, FontSize = size, Foreground = color, FontWeight = weight, Margin = margin, TextWrapping = TextWrapping.Wrap };

        c.Children.Add(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/icon_64x64.png")), Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Left });
        c.Children.Add(text("Bem-vindo ao Estoque", 32, Theme.B("Text"), FontWeights.SemiBold, new Thickness(0, 22, 0, 6)));
        c.Children.Add(text("Sua coleção de Magic, seus decks e binders — organizados no seu computador.", 15, Gray, FontWeights.Normal, new Thickness(0, 0, 0, 40)));

        c.Children.Add(text("TEMA", 11, Gray, FontWeights.SemiBold, new Thickness(2, 0, 0, 12)));
        c.Children.Add(Theme.Picker());

        c.Children.Add(text("PARA COMEÇAR", 11, Gray, FontWeights.SemiBold, new Thickness(2, 30, 0, 12)));
        var actions = new WrapPanel();
        actions.Children.Add(StartCard("\uE8B5", "Importar coleção", "CSV ou lista do Moxfield", () => AddList_Click(this, new RoutedEventArgs())));
        actions.Children.Add(StartCard("\uE8A5", "Criar um deck", "Por link do Moxfield ou colando a lista", NewDeck));
        actions.Children.Add(StartCard("\uE82D", "Criar binder ou box", "Organize por setores", NewLoc));
        c.Children.Add(actions);

        var skip = new Button { Content = "Começar vazio", Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, Padding = new Thickness(2, 6, 2, 6) };
        skip.Click += (_, _) => CloseWelcome();
        c.Children.Add(skip);
    }

    Border StartCard(string glyph, string title, string desc, Action act)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 20, Foreground = Theme.B("Text"), Margin = new Thickness(0, 0, 0, 16) });
        sp.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Theme.B("Text") });
        sp.Children.Add(new TextBlock { Text = desc, FontSize = 12, Foreground = Gray, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        var b = new Border { Child = sp, Width = 244, Padding = new Thickness(20, 20, 20, 22), Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(12), Background = Tile, Cursor = Cursors.Hand };
        b.MouseEnter += (_, _) => b.Background = TileHover;
        b.MouseLeave += (_, _) => b.Background = Tile;
        b.MouseLeftButtonUp += (_, _) => { CloseWelcome(); act(); };
        return b;
    }

    // ---------- navegação ----------
    void RefreshNav(string? select = null)
    {
        Nav.Items.Clear();
        ListBoxItem item(string text, string tag) { var i = new ListBoxItem { Content = text, Tag = tag }; Nav.Items.Add(i); return i; }
        // cabeçalho clicável que recolhe/expande a seção (não é selecionável)
        void header(string text, bool collapsed, Action toggle)
        {
            var h = new ListBoxItem { Content = (collapsed ? "▸  " : "▾  ") + text, Focusable = false, FontSize = 11, Foreground = Gray, Margin = new Thickness(0, 14, 0, 0) };
            h.PreviewMouseLeftButtonDown += (_, e) => { toggle(); RefreshNav(view ?? ""); e.Handled = true; };
            Nav.Items.Add(h);
        }
        // botão direito: menu do local, sem entrar nele
        void locItem(Location l)
        {
            var i = item(l.Kind != "deck" && Store.IsDefault(d, l) ? $"{l.Name}  ·  padrão" : l.Name, l.Name);
            i.PreviewMouseRightButtonDown += (_, e) => { var m = LocMenu(l); m.PlacementTarget = i; m.IsOpen = true; e.Handled = true; };
        }
        item("Início", "");
        item("Geral", AllTag);
        item("Movimentações", LogTag);
        header("DECKS", hideDecks, () => hideDecks = !hideDecks);
        if (!hideDecks) foreach (var l in d.Locations.Where(l => l.Kind == "deck")) locItem(l);
        header("BINDERS E BOXES", hideStores, () => hideStores = !hideStores);
        if (!hideStores) foreach (var l in d.Locations.Where(l => l.Kind != "deck")) locItem(l);
        Select(select ?? "");
    }

    bool hideDecks, hideStores;

    // Renomear / exportar / excluir um deck, binder ou box (barra lateral e cards do Início).
    ContextMenu LocMenu(Location l)
    {
        var m = new ContextMenu();
        void add(string text, Action act, Brush? color = null)
        {
            var mi = new MenuItem { Header = text };
            if (color != null) mi.Foreground = color;
            mi.Click += (_, _) => act();
            m.Items.Add(mi);
        }
        add("Abrir", () => Select(l.Name));
        m.Items.Add(new Separator());
        add("Renomear…", () => RenameLocation(l));
        if (l.Kind != "deck" && !Store.IsDefault(d, l))
            add("Definir como local padrão", () => { d.DefaultLoc = l.Name; Commit(); Flash($"{l.Name} agora é o local padrão das cartas sem lugar"); });
        add("Exportar — copiar lista", () => Copy(ExportText(l), "Lista"));
        add("Exportar — salvar .txt…", () => SaveExport(l));
        m.Items.Add(new Separator());
        add("Excluir…", () => DeleteLocation(l), Theme.B("DangerText"));
        return m;
    }

    string ExportText(Location l) => l.Kind == "deck" ? Store.FormatDeck(d, l) : Store.FormatStacks(d.Stacks.Where(x => Store.Same(x.Loc, l.Name)));

    void SaveExport(Location l)
    {
        var f = new Microsoft.Win32.SaveFileDialog { FileName = l.Name, DefaultExt = ".txt", Filter = "Lista (*.txt)|*.txt" };
        if (f.ShowDialog(this) != true) return;
        System.IO.File.WriteAllText(f.FileName, ExportText(l));
        Flash($"Salvo em {System.IO.Path.GetFileName(f.FileName)}");
    }

    void Select(string tag)
    {
        var it = Nav.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is string t && Store.Same(t, tag));
        // local existe mas está numa seção recolhida: continua nele (só não fica destacado na barra)
        if (it == null && d.Locations.Any(l => Store.Same(l.Name, tag))) { view = d.Locations.First(l => Store.Same(l.Name, tag)).Name; Nav.SelectedItem = null; Render(); return; }
        Nav.SelectedItem = it ?? Nav.Items[0];
    }

    void Nav_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not ListBoxItem { Tag: string t }) return;
        var next = t == "" ? null : t;
        bool changed = view == null ? next != null : next == null || !Store.Same(view, next);
        view = next;
        if (changed)
        {
            deckSel.Clear(); sectorFilter = null; page = 0;
            if (!keepLogFilter) { logLoc = null; logCard = null; }
            if (!keepSearch) Search.Text = "";
            DeckScroll.ScrollToTop();
        }
        keepSearch = false;
        keepLogFilter = false;
        Render();
    }

    void Commit(string? select = null)
    {
        HideToast();
        Store.Save(d);
        bool same = select == null || (view != null && Store.Same(select, view));
        double deckY = DeckScroll.VerticalOffset, tableY = TableScroll()?.VerticalOffset ?? 0;
        RefreshNav(select ?? view);
        Render();
        if (same)
            Dispatcher.BeginInvoke(() => { DeckScroll.ScrollToVerticalOffset(deckY); TableScroll()?.ScrollToVerticalOffset(tableY); },
                                   System.Windows.Threading.DispatcherPriority.Loaded);
    }

    ScrollViewer? TableScroll() => FindChild<ScrollViewer>(Table);

    static T? FindChild<T>(DependencyObject o) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
        {
            var c = VisualTreeHelper.GetChild(o, i);
            if (c is T t) return t;
            if (FindChild<T>(c) is T r) return r;
        }
        return null;
    }

    // ---------- botões da janela (sem barra de título do Windows) ----------
    void Min_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object s, RoutedEventArgs e) => Close();

    // maximizada, a janela sem moldura passa um pouco da tela; a margem compensa
    void Window_StateChanged(object? s, EventArgs e)
    {
        bool max = WindowState == WindowState.Maximized;
        // maximizada, a moldura invisível do Windows fica fora da tela: recua o conteúdo nesse tanto
        var dpi = VisualTreeHelper.GetDpi(this);
        int pad = GetSystemMetrics(92); // SM_CXPADDEDBORDER
        Root.Margin = max ? new Thickness((GetSystemMetrics(32) + pad) / dpi.DpiScaleX, (GetSystemMetrics(33) + pad) / dpi.DpiScaleY,
                                          (GetSystemMetrics(32) + pad) / dpi.DpiScaleX, (GetSystemMetrics(33) + pad) / dpi.DpiScaleY) : new Thickness(0);
        BorderThickness = new Thickness(max ? 0 : 1);
        MaxBtn.Content = max ? "\uE923" : "\uE922";
        MaxBtn.ToolTip = max ? "Restaurar" : "Maximizar";
        if (!max) Dispatcher.BeginInvoke(KeepOnScreen, System.Windows.Threading.DispatcherPriority.Background); // depois do Windows restaurar a posição
    }

    // ---------- janela sem barra do Windows: manter dentro da tela ----------
    // Sem a barra nativa, o Windows maximiza do tamanho do monitor inteiro (passa da tela e cobre a
    // barra de tarefas). WM_GETMINMAXINFO responde com a \u00E1rea \u00FAtil do monitor onde a janela est\u00E1.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        System.Windows.Interop.HwndSource.FromHwnd(hwnd).AddHook(WndProc);
        KeepOnScreen();
    }

    // Maior que a tela -> encolhe; topo/lados pra fora -> volta pra dentro (sempre d\u00E1 pra arrastar).
    void KeepOnScreen()
    {
        var wa = SystemParameters.WorkArea;
        if (Width > wa.Width) Width = Math.Max(MinWidth, wa.Width - 20);
        if (Height > wa.Height) Height = Math.Max(MinHeight, wa.Height - 20);
        if (double.IsNaN(Left) || double.IsNaN(Top)) return;
        if (Top < wa.Top) Top = wa.Top;
        if (Top > wa.Bottom - 60) Top = wa.Bottom - 60;
        if (Left + Width < wa.Left + 120) Left = wa.Left;
        if (Left > wa.Right - 120) Left = wa.Right - Width;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct POINT { public int X, Y; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct MINMAXINFO { public POINT Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct MONITORINFO { public int Size; public RECT Monitor, Work; public int Flags; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0024) // WM_GETMINMAXINFO
        {
            var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var mi = new MONITORINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */), ref mi))
            {
                mmi.MaxPosition.X = mi.Work.Left - mi.Monitor.Left;
                mmi.MaxPosition.Y = mi.Work.Top - mi.Monitor.Top;
                mmi.MaxSize.X = mi.Work.Right - mi.Work.Left;
                mmi.MaxSize.Y = mi.Work.Bottom - mi.Work.Top;
                System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
            }
        }
        return IntPtr.Zero;
    }

    void ToggleSide_Click(object s, RoutedEventArgs e) => SideCol.Width = new GridLength(SideCol.Width.Value > 0 ? 0 : 230);

    // ---------- painel da imagem (redimensionável) ----------
    double previewW = 300; // largura escolhida arrastando a borda

    void SetPreview(bool on)
    {
        PreviewCol.MinWidth = on ? 200 : 0;
        PreviewCol.Width = new GridLength(on ? previewW : 0);
        Vis(Splitter, on); Vis(Preview, on);
    }

    void Splitter_DragCompleted(object s, System.Windows.Controls.Primitives.DragCompletedEventArgs e) => previewW = PreviewCol.ActualWidth;

    // ---------- render ----------
    static void Vis(UIElement e, bool on) => e.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    static string? CardOf(object? o) => o switch { DeckRow r => r.Carta, StackRow r => r.Carta, HomeRow r => r.Carta, ArtRow r => r.Carta, LogRow r => r.Carta, WantRow r => r.Carta, _ => null };
    static Stack? StackOf(object? o) => o switch { StackRow r => r.S, DeckRow r => r.S, _ => null };

    // seleção: no deck é a nossa (layout em colunas); no resto, a da tabela
    readonly List<DeckRow> deckSel = new();
    List<object> SelItems() => Mode == "deck" ? deckSel.Cast<object>().ToList() : Table.SelectedItems.Cast<object>().ToList();
    List<string> SelectedCards() => SelItems().Select(CardOf).OfType<string>().Distinct(CI).ToList();
    Stack? SelectedStack() => StackOf(SelItems().FirstOrDefault());
    List<Stack> SelectedStacks() => SelItems().Select(StackOf).OfType<Stack>().Distinct().ToList();

    static ListCollectionView Grouped<T>(List<T> rows, string by)
    {
        var v = new ListCollectionView(rows);
        v.GroupDescriptions.Add(new PropertyGroupDescription(by));
        return v;
    }

    void Render()
    {
        var keep = SelectedCards();
        var l = Cur;
        var mode = Mode;
        var q = Search.Text.Trim();
        bool match(string c) => c.Contains(q, StringComparison.OrdinalIgnoreCase);
        bool homeTiles = mode == "home" && q == "";

        Vis(SearchHint, q == "");
        Vis(HomeBar, mode is "home" or "all"); Vis(DeckBar, mode == "deck");
        int pullable = mode == "deck" && Cur != null ? Store.Pullable(d, Cur) : 0;
        Vis(PullFreeBtn, pullable > 0); PullFreeBtn.Content = $"Puxar {pullable} livre(s)"; Vis(StoreBar, mode == "store");
        Vis(OnlyMissing, mode == "deck"); Vis(AddBox, mode is "deck" or "store"); Vis(SectorBar, mode is "store" or "all" or "log");

        Vis(Pager, false);
        Vis(DeckMoreBtn, mode is "deck" or "store");
        ArtBtn.Content = mode == "deck" ? "Alterar arte" : mode == "store" ? "Corrigir arte" : "Arte exibida";

        Vis(HomePanel, homeTiles); Vis(TableWrap, !homeTiles && mode != "deck"); Vis(DeckScroll, mode == "deck");
        SetPreview(!homeTiles);
        Table.ItemsSource = null;
        Table.GroupStyle.Clear();
        Table.GroupStyle.Add((GroupStyle)FindResource(mode == "all" ? "ExpandGroup" : "PlainGroup"));

        if (mode is "home" or "all")
        {
            int decks = d.Locations.Count(x => x.Kind == "deck");
            // Geral: só o que você tem; a busca do Início inclui também o que falta nos decks
            var owned = d.Stacks.Select(s => s.Card);
            var names = (mode == "all" ? owned : owned.Concat(d.Locations.SelectMany(x => x.Wants.Select(w => w.Card)))).Distinct(CI).ToList();
            Header.Text = mode == "all" ? "Geral" : "Início";
            Sub.Text = mode == "all" ? $"{names.Count} cartas diferentes · {d.Stacks.Sum(s => s.Qty)} cópias em decks, binders e boxes"
                : $"{d.Stacks.Sum(s => s.Qty)} cartas · {decks} decks · {d.Locations.Count - decks} binders e boxes";
            if (homeTiles) BuildHome();
            else if (mode == "home")
                Table.ItemsSource = PageOf(names.Where(match).OrderBy(c => c).ToList()).Select(c => new HomeRow(c, Store.Have(d, c), Where(c, missing: true))).ToList();
            else if (geralWants)
            {
                Table.GroupStyle.Clear();
                var wants = WantRows().Where(r => match(r.Carta)).ToList();
                Sub.Text = $"{wants.Count} cartas faltando nos decks · {wants.Sum(r => r.Comprar)} pra comprar · {wants.Sum(r => Math.Min(r.Livres, r.Falta))} dá pra puxar da coleção";
                BuildGeralTabs(wants.Count);
                Table.ItemsSource = PageOf(wants);
            }
            else
            {
                BuildGeralTabs(WantRows().Count);
                // uma linha por carta + arte + foil, agrupadas por carta (paginação por carta)
                var pageCards = PageOf(names.Where(match).OrderBy(c => c).ToList()).ToHashSet(CI);
                var rows = d.Stacks.Where(s => pageCards.Contains(s.Card))
                    .GroupBy(s => (s.Card.ToLowerInvariant(), s.Print, s.Foil))
                    .Select(g =>
                    {
                        int free = g.Where(s => !Store.IsDeck(d, s.Loc)).Sum(s => s.Qty);
                        var (dot, tip) = free > 0 ? (DotBlue, $"{free} livre(s)") : (Theme.B("DotGray"), "Todas em uso em decks");
                        return new ArtRow(dot, tip, g.First().Card, ArtName(g.Key.Print), g.Key.Foil ? "✦" : "", g.Sum(s => s.Qty), Places(g), g.Key.Print);
                    })
                    .OrderBy(r => r.Carta).ThenBy(r => r.Arte).ThenBy(r => r.Foil).ToList();
                Table.ItemsSource = Grouped(rows, nameof(ArtRow.Carta));
            }
        }
        else if (mode == "log")
        {
            Header.Text = logCard ?? "Movimentações";
            var logs = LogsShown(q);
            Sub.Text = logCard != null ? $"Histórico da carta · {logs.Count} movimentações" : $"{logs.Count} de {d.Log.Count} registros · de onde cada cópia saiu e pra onde foi";
            BuildLogBar(logs);
            // agrupado por dia; clique numa linha = histórico só daquela carta
            Table.ItemsSource = Grouped(PageOf(logs).Select(x => new LogRow(DayLabel(x.When), x.When.ToString("HH:mm"), x.Card, ArtName(x.Print) + (x.Foil ? " ✦" : ""), x.Qty, x.From, x.To, x)).ToList(), nameof(LogRow.Dia));
        }
        else if (mode == "deck")
        {
            bool onlyMissing = OnlyMissing.IsChecked == true;
            deckRows = Store.DeckLines(d, l!)
                .Select(x => new DeckRow(GroupOf(x.W), x.W.Board, x.W.Card, x.S == null ? "—" : ArtName(x.S.Print), x.S?.Foil == true ? "✦" : "",
                    x.Qty, Store.Free(d, x.W.Card), x.Missing, x.W.Print != "" ? x.W.Print : Store.DesiredOf(l!, x.W.Card), x.S))
                .Where(r => match(r.Carta) && (!onlyMissing || r.Falta > 0))
                .OrderBy(r => Array.IndexOf(Types.Order, r.Grupo)).ThenBy(r => r.Carta).ThenBy(r => r.S == null).ToList();
            // mantém a seleção (mesma carta, mesma cópia, mesmo board)
            var keepRows = deckSel.Select(r => (r.Carta, r.S, r.Board)).ToList();
            deckSel.Clear();
            deckSel.AddRange(deckRows.Where(r => keepRows.Contains((r.Carta, r.S, r.Board))));
            var cmds = l!.Wants.Where(w => w.Board == Boards.Cmd).Select(w => w.Card).ToList();
            int missing = Store.DeckMissing(d, l);
            Header.Text = l.Name;
            Sub.Text = (cmds.Count > 0 ? $"{string.Join(" + ", cmds)} · " : "")
                + $"{l.Wants.Where(w => w.Board is Boards.Main or Boards.Cmd).Sum(w => w.Qty)} cartas · "
                + (missing > 0 ? $"faltam {missing}" : "completo");
            BuildDeck();
            _ = LoadTypes(l.Wants.Select(w => w.Card));
            _ = Img.Prefetch(Store.DeckLines(d, l).Select(x => (x.W.Card, (string?)(x.S?.Print is { Length: > 0 } p ? p : d.Prints.GetValueOrDefault(x.W.Card)))).ToList());
        }
        else
        {
            var stacks = d.Stacks.Where(s => Store.Same(s.Loc, l!.Name)).ToList();
            BuildSectorBar(l!, stacks);
            var rows = stacks.Where(s => match(s.Card) && (sectorFilter == null || Store.Same(s.Sector, sectorFilter)))
                .Select(s => { var (dot, tip) = NeededDot(s.Card); return new StackRow(s.Sector == "" ? "Sem setor" : s.Sector, dot, tip, s.Card, ArtName(s.Print), s.Foil ? "✦" : "", s.Qty, s); })
                .OrderBy(r => r.Setor == "Sem setor").ThenBy(r => r.Setor).ThenBy(r => r.Carta).ToList();
            int sectors = Store.SectorsOf(d, l!).Count;
            Header.Text = l!.Name;
            Sub.Text = $"{(l.Kind == "box" ? "Box" : "Binder")}{(Store.IsDefault(d, l) ? " padrão" : "")} · {stacks.Sum(s => s.Qty)} cartas"
                + (sectors > 0 ? $" · {sectors} setores" : "");
            Table.ItemsSource = Grouped(PageOf(rows), nameof(StackRow.Setor));
        }

        bool empty = !homeTiles && mode != "deck" && Table.Items.Count == 0;
        Vis(EmptyHint, empty); Vis(Table, !empty);
        EmptyHint.Text = mode == "log" && q == "" ? "Nenhuma movimentação ainda. Quando uma cópia mudar de lugar, ela aparece aqui." : q != "" ? "Nada encontrado." : mode == "store" && sectorFilter is { } sf ? $"Nenhuma carta em {(sf == "" ? "Sem setor" : sf)}. Arraste cartas para o chip do setor ou use o botão direito → Mover para setor." : "Nenhuma carta aqui ainda.";
        if (mode != "deck") foreach (var r in Table.Items) if (keep.Contains(CardOf(r)!, CI)) Table.SelectedItems.Add(r);
        UpdateSelection();
    }

    // Fatia a página atual e atualiza o paginador (só aparece com mais de uma página).
    List<T> PageOf<T>(List<T> all)
    {
        int pages = Math.Max(1, (all.Count + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 0, pages - 1);
        Vis(Pager, pages > 1);
        PageLabel.Text = $"{page * PageSize + 1}–{Math.Min(all.Count, (page + 1) * PageSize)} de {all.Count}";
        PrevPage.IsEnabled = page > 0;
        NextPage.IsEnabled = page < pages - 1;
        return all.Skip(page * PageSize).Take(PageSize).ToList();
    }

    void PrevPage_Click(object s, RoutedEventArgs e) { page--; Render(); Table.ScrollIntoView(Table.Items.Cast<object>().FirstOrDefault()); }
    void NextPage_Click(object s, RoutedEventArgs e) { page++; Render(); Table.ScrollIntoView(Table.Items.Cast<object>().FirstOrDefault()); }

    static string ArtName(string print) => print == "" ? "Padrão" : print.Replace('/', ' ').ToUpper();

    static string GroupOf(Want w) => w.Board is Boards.Cmd or Boards.Side or Boards.Maybe ? w.Board : Types.Group(w.Card);

    async Task LoadTypes(IEnumerable<string> cards)
    {
        try { if (await Types.Fetch(cards.ToList()) && Mode == "deck") Render(); }
        catch { }
    }

    // "Neyith (1) · Staples / Azul (2)"
    static string Places(IEnumerable<Stack> ss) => string.Join("  ·  ", ss.GroupBy(s => (s.Loc, s.Sector))
        .Select(g => (g.Key.Sector == "" ? g.Key.Loc : $"{g.Key.Loc} / {g.Key.Sector}") + $" ({g.Sum(s => s.Qty)})"));

    string Where(string card, bool missing)
    {
        var parts = new[] { Places(d.Stacks.Where(s => Store.Same(s.Card, card))) }.Where(x => x != "");
        var miss = !missing ? Enumerable.Empty<string>() : d.Locations.Where(x => x.Kind == "deck").Select(x => (x.Name, n: Store.Missing(d, x, card))).Where(x => x.n > 0)
            .Select(x => $"falta em {x.Name} ({x.n})");
        return string.Join("  ·  ", parts.Concat(miss));
    }

    void Table_AutoGeneratingColumn(object? s, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.PropertyName is "Grupo" or "Board" or "Setor" or "S" or "Print" or "Tip" or "Dia" or "M") { e.Cancel = true; return; }
        if (e.PropertyName == "Dot") { e.Column = DotColumn(); return; }
        if (e.PropertyName == "Foil" && e.Column is DataGridTextColumn fc)
        {
            // foil em destaque (dourado) nas tabelas também
            var st = new Style(typeof(TextBlock));
            st.Setters.Add(new Setter(TextBlock.ForegroundProperty, Theme.B("Foil")));
            st.Setters.Add(new Setter(TextBlock.FontSizeProperty, 14.0));
            st.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
            fc.ElementStyle = st;
        }
        e.Column.Width = e.PropertyName is "Carta" or "Onde" or "De" or "Para" ? new DataGridLength(1, DataGridLengthUnitType.Star)
            : new DataGridLength(e.PropertyName is "Foil" ? 50 : 84);
    }

    bool keepSearch;

    void Filter_Changed(object s, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        // digitou no Início: vai pro Geral já filtrado (uma busca só, num lugar só)
        if (Mode == "home" && Search.Text != "") { keepSearch = true; Select(AllTag); Search.CaretIndex = Search.Text.Length; return; }
        page = 0;
        Render();
    }

    // ---------- deck em colunas ----------
    List<DeckRow> deckRows = new();
    readonly Dictionary<DeckRow, Border> rowEls = new();
    int deckCols;
    const double ColWidth = 250;

    void DeckScroll_SizeChanged(object s, SizeChangedEventArgs e)
    {
        if (Mode == "deck" && Math.Max(1, (int)(DeckScroll.ActualWidth / ColWidth)) != deckCols) BuildDeck();
    }

    // Grupos (Comandante, Criatura, …) distribuídos na coluna mais curta, como no Moxfield.
    // Sideboard e Considerando ficam numa seção separada embaixo; o espaço de cima é só o deck.
    void BuildDeck()
    {
        DeckGrid.Children.Clear();
        rowEls.Clear();
        deckCols = Math.Max(1, (int)(Math.Max(DeckScroll.ActualWidth, ColWidth) / ColWidth));
        bool extra(string g) => g is Boards.Side or Boards.Maybe;
        var groups = deckRows.GroupBy(r => r.Grupo).ToList();
        DeckGrid.Children.Add(Masonry(groups.Where(g => !extra(g.Key))));
        var rest = groups.Where(g => extra(g.Key)).ToList();
        if (rest.Count > 0)
        {
            DeckGrid.Children.Add(new Border { Height = 1, Background = Theme.B("Border"), Margin = new Thickness(0, 6, 20, 18) });
            DeckGrid.Children.Add(Masonry(rest));
        }
        PaintSel();
    }

    // Grupos em ordem, coluna por coluna (como jornal): a coluna fecha quando passa da altura média.
    Grid Masonry(IEnumerable<IGrouping<string, DeckRow>> groups)
    {
        int n = deckCols;
        var grid = new Grid();
        var cols = Enumerable.Range(0, n).Select(_ => new StackPanel { Margin = new Thickness(0, 0, 20, 0) }).ToArray();
        var list = groups.ToList();
        static int size(IGrouping<string, DeckRow> g) => g.Count() + 3;
        double target = (double)list.Sum(size) / n;
        int c = 0, h = 0;
        foreach (var g in list)
        {
            // fecha a coluna se este grupo passaria bem da média e ainda há colunas livres
            if (h > 0 && c < n - 1 && h + size(g) / 2.0 > target) { c++; h = 0; }
            cols[c].Children.Add(GroupEl(g.Key, g.ToList()));
            h += size(g);
        }
        for (int i = 0; i < n; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(cols[i], i);
            grid.Children.Add(cols[i]);
        }
        return grid;
    }

    UIElement GroupEl(string name, List<DeckRow> rows)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
        var head = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 0, 6) };
        head.Inlines.Add(new Run(name));
        head.Inlines.Add(new Run($"  {rows.Sum(r => r.Qtd)}") { Foreground = Gray, FontWeight = FontWeights.Normal });
        head.Cursor = Cursors.Hand;
        head.ToolTip = "Clique: selecionar a seção · Botão direito: copiar";
        head.MouseLeftButtonUp += (_, _) => { deckSel.Clear(); deckSel.AddRange(rows); PaintSel(); OnSelectionChanged(); };
        head.MouseRightButtonUp += (_, e) =>
        {
            var m = new ContextMenu { PlacementTarget = head };
            // faltantes: o que falta; no Considerando, o que você não tem livre
            var all = rows.Select(r => (r, n: r.Qtd)).ToList();
            var miss = rows.Select(r => (r, n: r.Falta > 0 ? r.Falta : r.Board == Boards.Maybe && r.S == null && r.Livres == 0 ? r.Qtd : 0)).Where(x => x.n > 0).ToList();
            void add(string text, Action act, bool enabled = true)
            {
                var mi = new MenuItem { Header = text, IsEnabled = enabled };
                mi.Click += (_, _) => act();
                m.Items.Add(mi);
            }
            Dictionary<string, int> names(List<(DeckRow r, int n)> xs) => xs.GroupBy(x => x.r.Carta, CI).ToDictionary(g => g.Key, g => g.Sum(x => x.n), CI);
            add($"Copiar {name} (LigaMagic)", () => CopyLiga(names(all), name));
            add($"Copiar {name} (Moxfield)", () => Copy(SectionWithArt(all), name));
            m.Items.Add(new Separator());
            add($"Copiar faltantes (LigaMagic)  {miss.Sum(x => x.n)}", () => CopyLiga(names(miss), $"Faltantes de {name}"), miss.Count > 0);
            add($"Copiar faltantes (Moxfield)  {miss.Sum(x => x.n)}", () => Copy(SectionWithArt(miss), $"Faltantes de {name}"), miss.Count > 0);
            m.IsOpen = true;
            e.Handled = true;
        };
        sp.Children.Add(head);
        sp.Children.Add(new Border { Height = 1, Background = Theme.B("Border"), Margin = new Thickness(0, 0, 0, 4) });
        foreach (var r in rows) sp.Children.Add(RowEl(r));
        return sp;
    }

    // "1 Sol Ring (C15) 268 *F*": arte da cópia; linha sem cópia usa a arte desejada (ou a do deck).
    string SectionWithArt(List<(DeckRow r, int n)> rows)
    {
        var l = Cur!;
        return string.Join("\r\n", rows
            .Select(x => (x.r.Carta, Print: x.r.Desired != "" ? x.r.Desired : x.r.S?.Print is { Length: > 0 } sp ? sp : Store.DeckPrint(d, l, x.r.Carta), Foil: x.r.S?.Foil == true, Qtd: x.n))
            .GroupBy(x => (x.Carta.ToLowerInvariant(), x.Print, x.Foil))
            .OrderBy(g => g.First().Carta)
            .Select(g =>
            {
                var line = $"{g.Sum(x => x.Qtd)} {g.First().Carta}";
                if (g.Key.Print != "") { var p = g.Key.Print.Split('/', 2); line += $" ({p[0].ToUpper()}) {p[1]}"; }
                return g.Key.Foil ? line + " *F*" : line;
            }));
    }

    static Brush DotRed => Theme.B("DotRed");
    static Brush DotBlue => Theme.B("DotBlue");
    static Brush DotAmber => Theme.B("DotAmber");

    // Bolinha de status da linha (o tooltip explica): azul = tem a cópia; azul vazado = tem, em outra arte;
    // vermelho = falta; amarelo = falta, mas você tem (todas em uso) — continua na buylist.
    (Brush? color, bool hollow, string tip) Status(DeckRow r)
    {
        if (r.S == null && r.Board == Boards.Maybe)
        {
            // Considerando: dá pra testar sem comprar?
            if (r.Livres > 0) return (Theme.B("DotGreen"), false, $"Tem {r.Livres} livre — dá pra testar sem comprar");
            if (Store.Have(d, r.Carta) > 0) return (DotAmber, false, "Você tem, mas todas estão em uso em outros decks");
            return (DotRed, false, "Você não tem");
        }
        if (r.S == null)
        {
            if (r.Falta == 0) return (null, false, "");
            if (r.Livres > 0) return (Theme.B("DotGreen"), false, $"Falta {r.Falta} · tem {r.Livres} livre na coleção — \"Puxar livres\" traz pro deck");
            if (Store.Have(d, r.Carta) > 0) return (DotAmber, false, $"Falta {r.Falta} · você tem, mas todas estão em uso em outros decks (entra na buylist)");
            return (DotRed, false, $"Falta {r.Falta} · você não tem (entra na buylist)");
        }
        if (r.Desired != "" && r.S.Print != r.Desired) return (DotBlue, true, $"Tem a carta em {ArtName(r.S.Print)} · arte desejada: {ArtName(r.Desired)}");
        return (DotBlue, false, $"Tem a cópia no deck · {ArtName(r.S.Print)}{(r.S.Foil ? " foil" : "")}");
    }

    DeckRow? anchorRow; // início do Shift+clique

    Border RowEl(DeckRow r)
    {
        var dp = new DockPanel();
        var (color, hollow, tip) = Status(r);
        var dot = new Border
        {
            Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = hollow || color == null ? Brushes.Transparent : color, BorderBrush = color ?? Brushes.Transparent, BorderThickness = new Thickness(hollow ? 1.5 : 0),
            ToolTip = tip == "" ? null : tip,
        };
        ToolTipService.SetInitialShowDelay(dot, 150);
        DockPanel.SetDock(dot, Dock.Left);
        dp.Children.Add(dot);
        var qty = new TextBlock { Text = r.Qtd.ToString(), Width = 22, Foreground = Gray };
        DockPanel.SetDock(qty, Dock.Left);
        dp.Children.Add(qty);
        var right = new TextBlock { Foreground = Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        bool wrongArt = r.S != null && r.Desired != "" && r.S.Print != r.Desired;
        if (r.S?.Foil == true)
            right.Inlines.Add(new Run("✦") { Foreground = Theme.B("Foil"), FontSize = 14, FontWeight = FontWeights.Bold, ToolTip = "Foil" });
        if (wrongArt && Store.FreeStacks(d, r.Carta).Any(x => x.Print == r.Desired))
        {
            // tem a arte desejada livre: um clique troca (códigos das artes só no tooltip/painel)
            right.Inlines.Add(new Run("  ⇄") { Foreground = Theme.B("Good"), FontSize = 14, FontWeight = FontWeights.Bold });
            right.Cursor = Cursors.Hand;
            right.ToolTip = $"Trocar pela {ArtName(r.Desired)} que está livre ({ArtName(r.S!.Print)} vai pro binder/box padrão)";
            right.MouseLeftButtonDown += (_, e) => { SwapRow(r); e.Handled = true; };
        }

        DockPanel.SetDock(right, Dock.Right);
        dp.Children.Add(right);
        dp.Children.Add(new TextBlock { Text = r.Carta, TextTrimming = TextTrimming.CharacterEllipsis });

        var b = new Border { Padding = new Thickness(6, 4, 6, 4), CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Child = dp, Cursor = Cursors.Hand };
        rowEls[r] = b;
        b.MouseEnter += (_, _) => { ShowRow(r); if (!deckSel.Contains(r)) b.Background = HoverBg; };
        b.MouseLeave += (_, _) => { b.Background = deckSel.Contains(r) ? SelBg : Brushes.Transparent; ShowSelected(); };
        b.MouseLeftButtonDown += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && anchorRow != null && deckRows.Contains(anchorRow))
            {
                int a = deckRows.IndexOf(anchorRow), z = deckRows.IndexOf(r);
                deckSel.Clear();
                deckSel.AddRange(deckRows.Skip(Math.Min(a, z)).Take(Math.Abs(a - z) + 1));
            }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { if (!deckSel.Remove(r)) deckSel.Add(r); anchorRow = r; }
            else { deckSel.Clear(); deckSel.Add(r); anchorRow = r; }
            PaintSel(); OnSelectionChanged();
            if (e.ClickCount == 2) AlterArt();
        };
        b.MouseRightButtonUp += (_, e) =>
        {
            if (!deckSel.Contains(r)) { deckSel.Clear(); deckSel.Add(r); PaintSel(); OnSelectionChanged(); }
            DeckMenu().IsOpen = true;
            e.Handled = true;
        };
        return b;
    }

    void PaintSel()
    {
        foreach (var (r, b) in rowEls) b.Background = deckSel.Contains(r) ? SelBg : Brushes.Transparent;
    }

    // Menu do botão direito no deck (estilo Moxfield).
    ContextMenu DeckMenu()
    {
        var m = new ContextMenu();
        void add(string text, Action act, string keys = "")
        {
            var mi = new MenuItem { Header = text, InputGestureText = keys };
            mi.Click += (_, _) => act();
            m.Items.Add(mi);
        }
        var first = deckSel[0];
        add("Alterar arte…", AlterArt);
        if (first.S != null) add("Corrigir esta arte…", CorrectArt);
        if (first.S != null && first.Desired != "" && first.S.Print != first.Desired && Store.FreeStacks(d, first.Carta).Any(x => x.Print == first.Desired))
            add($"Trocar pela {ArtName(first.Desired)} (tem livre)", () => SwapRow(first));
        add("Mover para outro local…", () => Move_Click(this, new RoutedEventArgs()));
        m.Items.Add(new Separator());
        var rem = new MenuItem { Header = "Tirar do deck", InputGestureText = "Del" };
        rem.Click += (_, _) => Remove_Click(this, new RoutedEventArgs());
        m.Items.Add(rem);
        foreach (var (board, keys) in new[] { (Boards.Main, "Alt+1"), (Boards.Side, "Alt+3"), (Boards.Maybe, "Alt+4"), (Boards.Cmd, "") })
            if (first.Board != board) add($"Mover para {(board == Boards.Main ? "o Main" : board == Boards.Cmd ? "Comandante" : board)}", () => SetBoardSel(board), keys);
        m.Items.Add(new Separator());
        if (deckSel.Any(r => r.Falta > 0)) add("Já tenho…", () => HaveThese_Click(this, new RoutedEventArgs()));
        add("Copiar nome", () => { Clipboard.SetText(string.Join("\r\n", SelectedCards())); Flash("Nome copiado"); });
        add("Ver movimentações", () => ShowCardLog(first.Carta));
        add("Definir como imagem do deck", () =>
        {
            var l = Cur!;
            l.Cover = $"{first.Carta}|{first.S?.Print ?? Store.DeckPrint(d, l, first.Carta)}";
            Commit();
            Flash("Imagem do deck definida");
        });
        return m;
    }

    void SetBoardSel(string board)
    {
        var l = Cur!;
        foreach (var r in deckSel.ToList()) if (r.Board != board) Store.SetBoard(l, r.Carta, r.Board, board, r.Qtd);
        deckSel.Clear();
        Commit();
    }

    void PullFree_Click(object s, RoutedEventArgs e)
    {
        var snap = Store.Snapshot(d);
        int n = Store.PullFree(d, Cur!);
        Commit();
        ShowToast($"{n} carta(s) puxada(s) da coleção pro deck", snap);
    }

    void Remove_Click(object s, RoutedEventArgs e)
    {
        var l = Cur!;
        if (deckSel.Count == 0) return;
        int n = 0;
        var snap = Store.Snapshot(d);
        foreach (var r in deckSel.ToList()) { Store.RemoveFromDeck(d, l, r.Carta, r.Board, r.Qtd, r.S); n += r.Qtd; }
        deckSel.Clear();
        Commit();
        ShowToast($"{n} carta(s) tirada(s) do deck · as cópias foram pra {Store.Default(d)}", snap);
    }

    // ---------- setores ----------
    string? sectorFilter; // null = todos; "" = sem setor
    const string DragFmt = "mtg-stacks";
    Point dragStart;

    // Chips: Todos · Sem setor · setores… · + Setor. Clique filtra; soltar carta move; botão direito renomeia/exclui.
    void BuildSectorBar(Location l, List<Stack> stacks)
    {
        SectorBar.Children.Clear();
        int count(string? sec) => stacks.Where(s => sec == null || Store.Same(s.Sector, sec)).Sum(s => s.Qty);
        void chip(string label, string? sector, bool drop)
        {
            bool on = !label.StartsWith("+") && (sectorFilter == null ? sector == null : sector != null && Store.Same(sector, sectorFilter));
            var tb = new TextBlock { Text = sector == null && label.StartsWith("+") ? label : $"{label}  {count(sector)}", Foreground = on ? Theme.B("OnAccent") : Theme.B("Text") };
            var b = new Border { Child = tb, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(14), Cursor = Cursors.Hand,
                                 Background = on ? Theme.B("Accent") : Theme.B("Chip"), BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, AllowDrop = drop };
            if (label.StartsWith("+")) { b.MouseLeftButtonUp += (_, _) => { if (NewSector() is string n) { sectorFilter = n; Commit(); } }; SectorBar.Children.Add(b); return; }
            b.MouseLeftButtonUp += (_, _) => { sectorFilter = sector; page = 0; Render(); };
            if (drop)
            {
                b.DragEnter += (_, _) => b.BorderBrush = Theme.B("Accent");
                b.DragLeave += (_, _) => b.BorderBrush = Brushes.Transparent;
                b.Drop += (_, e) => { b.BorderBrush = Brushes.Transparent; if (e.Data.GetData(DragFmt) is List<Stack> ss) MoveToSector(ss, sector!); };
            }
            if (!string.IsNullOrEmpty(sector))
                b.MouseRightButtonUp += (_, e) =>
                {
                    var m = new ContextMenu { PlacementTarget = b };
                    var ren = new MenuItem { Header = "Renomear setor…" };
                    ren.Click += (_, _) => RenameSector(l, sector);
                    var del = new MenuItem { Header = "Excluir setor (cartas ficam sem setor)", Foreground = Theme.B("DangerText") };
                    del.Click += (_, _) =>
                    {
                        var snap = Store.Snapshot(d);
                        Store.DeleteSector(d, l, sector);
                        if (Store.Same(sectorFilter ?? "\0", sector)) sectorFilter = null;
                        Commit();
                        ShowToast($"Setor {sector} excluído", snap);
                    };
                    m.Items.Add(ren); m.Items.Add(del);
                    m.IsOpen = true; e.Handled = true;
                };
            SectorBar.Children.Add(b);
        }
        chip("Todos", null, false);
        chip("Sem setor", "", true);
        foreach (var s in Store.SectorsOf(d, l)) chip(s, s, true);
        chip("+ Setor", null, false);
    }

    string? NewSector()
    {
        var l = Cur!;
        var dlg = new PromptDialog("Novo setor", $"Nome do setor em {l.Name}");
        if (dlg.ShowDialog() != true || dlg.Value == "") return null;
        if (!l.Sectors.Contains(dlg.Value, CI)) l.Sectors.Add(dlg.Value);
        Store.Save(d);
        return dlg.Value;
    }

    void RenameSector(Location l, string old)
    {
        var dlg = new PromptDialog("Renomear setor", "Nome do setor", old);
        if (dlg.ShowDialog() != true || dlg.Value == "" || dlg.Value == old) return;
        var snap = Store.Snapshot(d);
        Store.RenameSector(d, l, old, dlg.Value);
        if (Store.Same(sectorFilter ?? "\0", old)) sectorFilter = dlg.Value;
        Commit();
        ShowToast($"Setor {old} → {dlg.Value}", snap);
    }

    void MoveToSector(List<Stack> stacks, string sector)
    {
        var l = Cur!;
        stacks = stacks.Where(s => d.Stacks.Contains(s) && !Store.Same(s.Sector, sector)).ToList();
        if (stacks.Count == 0) return;
        var snap = Store.Snapshot(d);
        int n = stacks.Sum(s => s.Qty);
        foreach (var s in stacks) Store.Move(d, s, s.Qty, l.Name, sector);
        if (sector != "" && !l.Sectors.Contains(sector, CI)) l.Sectors.Add(sector);
        Commit();
        ShowToast($"{n} carta(s) → {(sector == "" ? "Sem setor" : sector)}", snap);
    }

    static object? RowUnder(object source)
    {
        for (var o = source as DependencyObject; o != null; o = VisualTreeHelper.GetParent(o))
            if (o is DataGridRow r) return r.Item;
        return null;
    }

    void Table_DragMove(object s, MouseEventArgs e)
    {
        if (Mode != "store" || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(Table);
        if (Math.Abs(p.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (RowUnder(e.OriginalSource) is not StackRow row) return;
        // arrasta a seleção se a linha faz parte dela; senão só a linha
        var stacks = Table.SelectedItems.Contains(row) ? SelectedStacks() : new List<Stack> { row.S };
        DragDrop.DoDragDrop(Table, new DataObject(DragFmt, stacks), DragDropEffects.Move);
    }

    // ---------- desfazer ----------
    string? undoSnap;
    System.Windows.Threading.DispatcherTimer? toastTimer;

    void ShowToast(string msg, string? snap)
    {
        undoSnap = snap;
        ToastText.Text = msg;
        Vis(Toast, true);
        Vis(UndoBtn, snap != null);
        toastTimer?.Stop();
        toastTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(snap != null ? 8 : 4) };
        toastTimer.Tick += (_, _) => { Vis(Toast, false); undoSnap = null; toastTimer!.Stop(); };
        toastTimer.Start();
    }

    void HideToast() { toastTimer?.Stop(); undoSnap = null; Vis(Toast, false); }

    void Undo_Click(object s, RoutedEventArgs e)
    {
        if (undoSnap == null) return;
        d = Store.FromJson(undoSnap);
        undoSnap = null;
        deckSel.Clear();
        if (view != null && view is not (AllTag or LogTag) && !d.Locations.Any(l => Store.Same(l.Name, view))) view = null;
        Commit();
        Flash("Desfeito");
    }

    // Menu do botão direito em binder/box (mesmo formato do deck).
    ContextMenu StoreMenu()
    {
        var m = new ContextMenu();
        void add(string text, Action act, string keys = "", Brush? color = null)
        {
            var mi = new MenuItem { Header = text, InputGestureText = keys };
            if (color != null) mi.Foreground = color;
            mi.Click += (_, _) => act();
            m.Items.Add(mi);
        }
        add("Corrigir esta arte…", CorrectArt);
        var sec = new MenuItem { Header = "Mover para setor" };
        var cur = SelectedStacks().Select(x => x.Sector).Distinct(CI).ToList();
        foreach (var name in new[] { "" }.Concat(Store.SectorsOf(d, Cur!)))
        {
            if (cur.Count == 1 && Store.Same(cur[0], name)) continue;
            var mi = new MenuItem { Header = name == "" ? "Sem setor" : name };
            mi.Click += (_, _) => MoveToSector(SelectedStacks(), name);
            sec.Items.Add(mi);
        }
        if (sec.Items.Count > 0) sec.Items.Add(new Separator());
        var nw = new MenuItem { Header = "Novo setor…" };
        nw.Click += (_, _) => { if (NewSector() is string n) MoveToSector(SelectedStacks(), n); };
        sec.Items.Add(nw);
        m.Items.Add(sec);
        add("Mover para outro local…", () => Move_Click(this, new RoutedEventArgs()));
        m.Items.Add(new Separator());
        add("+1 cópia", () => Plus_Click(this, new RoutedEventArgs()));
        add("−1 cópia", () => Minus_Click(this, new RoutedEventArgs()));
        add("Remover da coleção", RemoveStacks, "Del", Theme.B("DangerText"));
        m.Items.Add(new Separator());
        add("Copiar nome", () => { Clipboard.SetText(string.Join("\r\n", SelectedCards())); Flash("Nome copiado"); });
        if (SelectedCards().FirstOrDefault() is string c) add("Ver movimentações", () => ShowCardLog(c));
        return m;
    }

    // Binder/box: apagar as cópias selecionadas (não vão pra lugar nenhum — saíram da coleção).
    void RemoveStacks()
    {
        var stacks = SelectedStacks();
        int n = stacks.Sum(x => x.Qty);
        if (n == 0) return;
        var what = stacks.Count == 1 ? $"{n}x {stacks[0].Card}" : $"{n} cópias ({stacks.Count} cartas)";
        if (!ConfirmDialog.Ask(this, $"Remover {what} da coleção?", "Pra carta vendida, trocada ou perdida. Pra só mudar de lugar, use Mover.", $"Remover {n} {(n == 1 ? "cópia" : "cópias")}", danger: true)) return;
        var snap = Store.Snapshot(d);
        foreach (var st in stacks) Store.Take(d, st, st.Qty);
        Commit();
        ShowToast($"{what} removida(s) da coleção", snap);
    }

    void Window_PreviewKeyDown(object s, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.F) { Search.Focus(); Search.SelectAll(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Z && undoSnap != null && Keyboard.FocusedElement is not TextBox) { Undo_Click(this, e); e.Handled = true; return; }
        if (e.Key == Key.Escape)
        {
            if (AddPopup.IsOpen) AddPopup.IsOpen = false;
            else if (Keyboard.FocusedElement is TextBox tb && tb.Text != "") tb.Text = "";
            else { deckSel.Clear(); PaintSel(); Table.UnselectAll(); UpdateSelection(); }
            e.Handled = true; return;
        }
        if (ctrl && e.Key == Key.A && Mode == "deck" && Keyboard.FocusedElement is not TextBox)
        { deckSel.Clear(); deckSel.AddRange(deckRows); PaintSel(); OnSelectionChanged(); e.Handled = true; return; }
        if (Mode == "store" && e.Key == Key.Delete && Keyboard.FocusedElement is not TextBox) { RemoveStacks(); e.Handled = true; return; }
        if (Mode != "deck" || deckSel.Count == 0 || Keyboard.FocusedElement is TextBox) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        if (key == Key.Delete || (alt && key == Key.D2)) Remove_Click(this, e);
        else if (alt && key == Key.D1) SetBoardSel(Boards.Main);
        else if (alt && key == Key.D3) SetBoardSel(Boards.Side);
        else if (alt && key == Key.D4) SetBoardSel(Boards.Maybe);
        else return;
        e.Handled = true;
    }

    // ---------- adicionar carta pelo nome (busca enquanto digita) ----------
    CancellationTokenSource? acCts;

    async void AddText_Changed(object s, TextChangedEventArgs e)
    {
        Vis(AddHint, AddText.Text == "");
        var q = AddText.Text.Trim();
        acCts?.Cancel();
        if (q.Length < 2) { AddPopup.IsOpen = false; return; }
        var cts = acCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(200, cts.Token);
            var names = await Img.Autocomplete(q);
            if (cts.IsCancellationRequested) return;
            AddList.ItemsSource = names;
            AddList.SelectedIndex = names.Count > 0 ? 0 : -1;
            AddPopup.IsOpen = names.Count > 0;
        }
        catch { }
    }

    void AddText_PreviewKeyDown(object s, KeyEventArgs e)
    {
        if (!AddPopup.IsOpen) return;
        int n = AddList.Items.Count;
        if (e.Key == Key.Down) AddList.SelectedIndex = Math.Min(AddList.SelectedIndex + 1, n - 1);
        else if (e.Key == Key.Up) AddList.SelectedIndex = Math.Max(AddList.SelectedIndex - 1, 0);
        else if (e.Key == Key.Enter && AddList.SelectedItem is string name) AddCard(name);
        else if (e.Key == Key.Escape) AddPopup.IsOpen = false;
        else return;
        AddList.ScrollIntoView(AddList.SelectedItem);
        e.Handled = true;
    }

    void AddList_Click(object s, MouseButtonEventArgs e) { if (AddList.SelectedItem is string name) AddCard(name); }

    // Deck: entra na lista e, se tiver cópia livre, já puxa da coleção. Binder/box: +1 cópia.
    void AddCard(string name)
    {
        AddPopup.IsOpen = false;
        AddText.Text = "";
        var l = Cur!;
        var print = d.Prints.GetValueOrDefault(name, "");
        if (l.Kind == "deck")
        {
            var w = l.Wants.FirstOrDefault(x => Store.Same(x.Card, name) && x.Board == Boards.Main);
            if (w == null) l.Wants.Add(new Want { Card = name, Qty = 1 }); else w.Qty++;
            // tem livre: pergunta de qual lugar pegar (e registra), em vez de puxar escondido
            var free = Store.FreeStacks(d, name).ToList();
            string msg = $"{name} adicionada · falta (não tem livre)";
            if (free.Count > 0 && Store.Missing(d, l, name) > 0)
            {
                var src = new SourceDialog(name, free);
                if (src.ShowDialog() == true && src.Chosen is Stack st)
                {
                    msg = $"{name} adicionada · pegue em {Store.Place(st.Loc, st.Sector)}";
                    Store.Pull(d, l, new[] { st }, name, st.Print, st.Foil, 1);
                }
                else msg = $"{name} adicionada · deixada como falta";
            }
            Commit();
            Flash(msg);
        }
        else
        {
            // cópia nova: pergunta qual arte (as que você já tem aparecem primeiro)
            var owned = d.Stacks.Where(x => Store.Same(x.Card, name)).Select(x => x.Print).Distinct();
            var pick = new ArtPicker(name, print == "" ? null : print, owned);
            if (pick.ShowDialog() != true) { AddText.Focus(); return; }
            Store.AddStack(d, name, pick.Chosen!, false, 1, l.Name, "");
            d.Prints.TryAdd(name, pick.Chosen!);
            Commit();
            Flash($"{name} ({ArtName(pick.Chosen!)}) adicionada em {l.Name}");
        }
        AddText.Focus();
    }

    // ---------- início ----------
    void BuildHome()
    {
        HomeContent.Children.Clear();
        void section(string title, IEnumerable<UIElement> tiles)
        {
            HomeContent.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 12) });
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
            foreach (var t in tiles) wrap.Children.Add(t);
            HomeContent.Children.Add(wrap);
        }
        section("Decks", d.Locations.Where(l => l.Kind == "deck").Select(DeckTile));
        section("Binders e boxes", d.Locations.Where(l => l.Kind != "deck").Select(StoreTile));
    }

    Border MakeTile(UIElement content, string name, Thickness padding)
    {
        var b = new Border { Width = 232, Background = Tile, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 14, 14), Padding = padding, Cursor = Cursors.Hand, Child = content };
        b.MouseEnter += (_, _) => b.Background = TileHover;
        b.MouseLeave += (_, _) => b.Background = Tile;
        b.MouseLeftButtonUp += (_, _) => Select(name);
        b.MouseRightButtonUp += (_, e) => { var m = LocMenu(Store.Loc(d, name)); m.PlacementTarget = b; m.IsOpen = true; e.Handled = true; };
        return b;
    }

    UIElement DeckTile(Location l)
    {
        var img = new Image { Width = 216, Height = 120, Stretch = Stretch.UniformToFill, Clip = new RectangleGeometry(new Rect(0, 0, 216, 120), 6, 6) };
        var art = new Grid { Height = 120 };
        art.Children.Add(new Border { Background = Theme.B("Selected"), CornerRadius = new CornerRadius(6) });
        art.Children.Add(img);
        var cmd = l.Wants.FirstOrDefault(w => w.Board == Boards.Cmd)?.Card;
        // imagem do deck: a escolhida no menu, senão o comandante
        if (l.Cover is { } cover && cover.Split('|') is [var cc, var cp]) _ = LoadCrop(img, cc, cp);
        else if (cmd != null) _ = LoadCrop(img, cmd, Store.DeckPrint(d, l, cmd));

        int missing = Store.DeckMissing(d, l);
        var sub = new TextBlock { Foreground = Gray, Margin = new Thickness(2, 2, 0, 2) };
        sub.Inlines.Add(new Run($"{l.Wants.Where(w => w.Board is Boards.Main or Boards.Cmd).Sum(w => w.Qty)} cartas"));
        if (missing > 0) { sub.Inlines.Add(new Run("  ·  ")); sub.Inlines.Add(new Run($"faltam {missing}") { Foreground = Theme.B("DangerText") }); }

        var sp = new StackPanel();
        sp.Children.Add(art);
        sp.Children.Add(new TextBlock { Text = l.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 10, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        sp.Children.Add(new TextBlock { Text = cmd ?? "Sem comandante", Foreground = Gray, Margin = new Thickness(2, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 });
        sp.Children.Add(sub);
        return MakeTile(sp, l.Name, new Thickness(8));
    }

    UIElement StoreTile(Location l)
    {
        var stacks = d.Stacks.Where(s => Store.Same(s.Loc, l.Name)).ToList();
        int sectors = stacks.Where(s => s.Sector != "").Select(s => s.Sector).Distinct(CI).Count();
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = l.Kind == "binder" ? "" : l.Kind == "box" ? "" : "",
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 22, Foreground = Gray, Margin = new Thickness(0, 0, 0, 10),
        });
        sp.Children.Add(new TextBlock { Text = l.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        sp.Children.Add(new TextBlock { Text = (Store.IsDefault(d, l) ? "Padrão  ·  " : "") + $"{stacks.Sum(s => s.Qty)} cartas" + (sectors > 0 ? $"  ·  {sectors} setores" : ""), Foreground = Gray, Margin = new Thickness(0, 2, 0, 0) });
        return MakeTile(sp, l.Name, new Thickness(16));
    }

    async Task LoadCrop(Image img, string card, string print)
    {
        try { img.Source = Img.Load(await Img.Get(card, print == "" ? null : print, false, "art_crop"), 432); }
        catch { }
    }

    // ---------- seleção / preview ----------
    string? pvCard, pvPrint;
    int pvToken;
    bool pvDfc, pvBack;

    void UpdateSelection()
    {

    }

    void Table_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        OnSelectionChanged();
    }

    // ---------- movimentações: filtros por local e por carta ----------
    string? logLoc, logCard;
    bool keepLogFilter;

    static string LocOf(string place) => place.Split(" / ")[0];

    static string DayLabel(DateTime t) =>
        t.Date == DateTime.Today ? "Hoje" : t.Date == DateTime.Today.AddDays(-1) ? "Ontem" : t.ToString("dd/MM/yyyy");

    List<Moved> LogsShown(string q) => Enumerable.Reverse(d.Log)
        .Where(x => (logLoc == null || Store.Same(LocOf(x.From), logLoc) || Store.Same(LocOf(x.To), logLoc))
                 && (logCard == null || Store.Same(x.Card, logCard))
                 && (q == "" || x.Card.Contains(q, StringComparison.OrdinalIgnoreCase) || x.From.Contains(q, StringComparison.OrdinalIgnoreCase) || x.To.Contains(q, StringComparison.OrdinalIgnoreCase)))
        .ToList();

    // Chips: [Carta: X ✕] · Todos · cada local que aparece no histórico · Apagar…
    void BuildLogBar(List<Moved> shown)
    {
        SectorBar.Children.Clear();
        Border chip(string text, bool on, Action click, Brush? fg = null)
        {
            var b = new Border
            {
                Child = new TextBlock { Text = text, Foreground = fg ?? (on ? Theme.B("OnAccent") : Theme.B("Text")) },
                Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(14), Cursor = Cursors.Hand,
                Background = on ? Theme.B("Accent") : Theme.B("Chip"),
            };
            b.MouseLeftButtonUp += (_, _) => click();
            SectorBar.Children.Add(b);
            return b;
        }
        if (logCard != null) chip($"{logCard}   ✕", true, () => { logCard = null; page = 0; Render(); });
        var locs = d.Log.SelectMany(x => new[] { LocOf(x.From), LocOf(x.To) }).GroupBy(x => x, CI).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
        chip("Todos os locais", logLoc == null, () => { logLoc = null; page = 0; Render(); });
        foreach (var name in locs) chip(name, logLoc != null && Store.Same(logLoc, name), () => { logLoc = name; page = 0; Render(); });
        if (d.Log.Count > 0)
        {
            bool filtered = shown.Count < d.Log.Count;
            chip(filtered ? $"Apagar estes {shown.Count}…" : "Apagar histórico…", false, () => DeleteLogs(shown), Theme.B("DangerText"));
        }
    }

    void DeleteLogs(List<Moved> items)
    {
        if (items.Count == 0) return;
        bool all = items.Count == d.Log.Count;
        if (!ConfirmDialog.Ask(this, all ? "Apagar todo o histórico?" : $"Apagar {items.Count} registros?",
            "Só o histórico é apagado — nenhuma carta muda de lugar.", all ? "Apagar tudo" : $"Apagar {items.Count}", danger: true)) return;
        var snap = Store.Snapshot(d);
        var set = items.ToHashSet();
        d.Log.RemoveAll(set.Contains);
        logCard = null;
        Commit();
        ShowToast($"{items.Count} registro(s) de movimentação apagado(s)", snap);
    }

    // Botão direito numa carta (deck/binder): abre o histórico dela.
    void ShowCardLog(string card)
    {
        logCard = card; logLoc = null; keepLogFilter = true;
        Select(LogTag);
    }

    void OnSelectionChanged()
    {
        UpdateSelection();
        if (SelItems().FirstOrDefault() is { } row) ShowRow(row);
    }

    int hoverToken;
    string pvNote = "";

    // Mouse saiu das cartas: o painel volta pra carta selecionada (não fica na última do hover).
    void ShowSelected() { if (SelItems().FirstOrDefault() is { } sel) ShowRow(sel); }

    // Mostra a carta da linha no painel (hover ou seleção). Espera o mouse parar ~90ms: passar rápido
    // por várias cartas não dispara download nenhum.
    async void ShowRow(object? row)
    {
        var card = CardOf(row);
        if (card == null) return;
        var print = (row is ArtRow { Print: not "" } ar ? ar.Print : null)
            ?? (row is DeckRow { Desired: not "" } dr ? dr.Desired : null) // arte desejada vem primeiro
            ?? (StackOf(row)?.Print is { Length: > 0 } sp ? sp : d.Prints.GetValueOrDefault(card));
        string note = row is DeckRow { Desired: not "" } dz ? (dz.S != null && dz.S.Print != dz.Desired ? $"  ·  desejada (você tem {ArtName(dz.S.Print)})" : dz.S == null ? "  ·  desejada" : "") : "";
        int t = ++hoverToken; // cancela qualquer hover pendente
        if (card == pvCard && print == pvPrint) return;
        await Task.Delay(90);
        if (t != hoverToken) return;
        pvCard = card; pvPrint = print; pvBack = false; pvNote = note;
        pvDfc = Img.IsDfc(card, print) == true;
        await ShowArt();
        if (pvCard == card) { pvDfc = Img.IsDfc(card, print) == true; Vis(Flip, pvDfc); }
    }

    async Task ShowArt()
    {
        int t = ++pvToken;
        var card = pvCard!;
        Vis(Flip, pvDfc);
        ArtLabel.Text = "Carregando…";
        try
        {
            var path = await Img.Get(card, pvPrint, pvBack);
            if (t != pvToken) return;
            CardImg.Source = Img.Load(path);
            Vis(ArtBtn, true);
            ArtLabel.Text = (pvPrint == null ? "Arte padrão" : pvPrint.Replace('/', ' ').ToUpper()) + pvNote;
        }
        catch { if (t == pvToken) ArtLabel.Text = "Sem imagem"; }
    }

    async void Flip_Click(object s, RoutedEventArgs e) { pvBack = !pvBack; await ShowArt(); }

    // Botão do painel: no deck = alterar arte; em binder/box = corrigir; no Geral = arte exibida.
    void ChangeArt_Click(object s, RoutedEventArgs e)
    {
        if (Mode == "deck") AlterArt();
        else if (Mode == "store") CorrectArt();
        else PreferredArt();
    }

    // usáveis aqui: livres e as que já estão neste local; em outros decks aparecem como "Usando"
    ArtPicker Picker(string card, string? current)
    {
        bool usableHere(Stack x) => !Store.IsDeck(d, x.Loc) || (Cur != null && Store.Same(x.Loc, Cur.Name));
        var mine = d.Stacks.Where(x => Store.Same(x.Card, card)).ToList();
        return new ArtPicker(card, current, mine.Where(usableHere).Select(x => x.Print).Distinct(), InUse(mine.Where(x => !usableHere(x))));
    }

    // Corrigir: a cópia já era dessa arte, só estava cadastrada errado. Nada muda de lugar.
    async void CorrectArt()
    {
        var row = SelItems().FirstOrDefault();
        if (StackOf(row) is not Stack st) { Flash("Selecione uma cópia que você tem"); return; }
        int n = row switch { DeckRow r => r.Qtd, StackRow r => r.Qtd, _ => st.Qty };
        var all = d.Stacks.Where(x => Store.Same(x.Card, st.Card)).Select(x => x.Print).Distinct();
        var pick = new ArtPicker(st.Card, st.Print == "" ? null : st.Print, all);
        if (pick.ShowDialog() != true || pick.Chosen == st.Print) return;
        var snap = Store.Snapshot(d);
        string card = st.Card, old = st.Print;
        Store.AddStack(d, card, pick.Chosen!, st.Foil, n, st.Loc, st.Sector);
        Store.Take(d, st, n);
        deckSel.Clear();
        Commit();
        ShowToast($"{card}: {ArtName(old)} corrigida para {ArtName(pick.Chosen!)}", snap);
        await ShowArtOf(card, pick.Chosen!);
    }

    // Alterar (deck): grava a arte desejada. Tem livre -> troca na hora; não tem -> fica marcada "quer …".
    async void AlterArt()
    {
        var l = Cur!;
        if (SelItems().FirstOrDefault() is not DeckRow row) return;
        var card = row.Carta;
        var current = row.Desired is { Length: > 0 } dz ? dz : row.S?.Print;
        var pick = Picker(card, string.IsNullOrEmpty(current) ? null : current);
        if (pick.ShowDialog() != true) return;
        var chosen = pick.Chosen!;
        var snap = Store.Snapshot(d);
        foreach (var w in l.Wants.Where(w => Store.Same(w.Card, card))) w.Print = chosen;

        string msg;
        if (d.Stacks.Any(x => Store.Same(x.Loc, l.Name) && Store.Same(x.Card, card) && x.Print == chosen))
            msg = $"{card}: {ArtName(chosen)} já está no deck";
        else if ((row.S != null || Store.Missing(d, l, card) > 0) && Store.SwapToDesired(d, l, card, row.S, chosen) is string from)
            msg = row.S != null ? $"{card}: {ArtName(chosen)} veio de {from} · {ArtName(row.S.Print)} foi pra {Store.Default(d)}" : $"{card}: {ArtName(chosen)} veio de {from}";
        else
            msg = $"{card}: quer {ArtName(chosen)} · marcado pra trocar quando tiver";
        deckSel.Clear();
        Commit();
        ShowToast(msg, snap);
        await ShowArtOf(card, chosen);
    }

    // Clique no "⇄ trocar" da linha: pega a arte desejada que está livre.
    void SwapRow(DeckRow r)
    {
        var l = Cur!;
        var snap = Store.Snapshot(d);
        if (Store.SwapToDesired(d, l, r.Carta, r.S, r.Desired) is not string from) return;
        deckSel.Clear();
        Commit();
        ShowToast($"{r.Carta}: {ArtName(r.Desired)} veio de {from} · {ArtName(r.S?.Print ?? "")} foi pra {Store.Default(d)}", snap);
    }

    // Geral / Início: só a arte exibida da carta.
    async void PreferredArt()
    {
        var row = SelItems().FirstOrDefault();
        var card = CardOf(row) ?? pvCard;
        if (card == null) return;
        var pick = Picker(card, d.Prints.GetValueOrDefault(card));
        if (pick.ShowDialog() != true) return;
        d.Prints[card] = pick.Chosen!;
        Commit();
        await ShowArtOf(card, pick.Chosen!);
    }

    async Task ShowArtOf(string card, string print)
    {
        pvCard = card; pvPrint = print; pvBack = false;
        await ShowArt();
    }

    // ---------- ações da seleção ----------
    void Plus_Click(object s, RoutedEventArgs e)
    {
        foreach (var st in SelectedStacks()) st.Qty++;
        Commit();
    }

    void Minus_Click(object s, RoutedEventArgs e)
    {
        foreach (var st in SelectedStacks()) Store.Take(d, st, 1);
        Commit();
    }

    void Move_Click(object s, RoutedEventArgs e)
    {
        var stacks = SelectedStacks();
        if (stacks.Count == 0) { Flash("Nenhuma cópia aqui para mover"); return; }
        var dlg = new MoveDialog(d, Cur?.Name, stacks.Count == 1 ? stacks[0].Qty : 0);
        if (dlg.ShowDialog() != true || dlg.Dest == "") return;
        var dest = dlg.Dest;

        // deck de destino já tem a mesma carta em outra arte: trocar as artes ou só mover?
        Stack? Counterpart(Stack st) => d.Stacks.FirstOrDefault(x => Store.Same(x.Loc, dest) && Store.Same(x.Card, st.Card) && (x.Print != st.Print || x.Foil != st.Foil));
        bool swap = false;
        if (Store.IsDeck(d, dest))
        {
            var clash = stacks.Where(st => Counterpart(st) != null).ToList();
            if (clash.Count > 0)
            {
                var pairs = clash.Select(st => (st.Card, st.Print, Counterpart(st)!.Print)).Distinct().ToList();
                var sw = new SwapDialog(clash[0].Loc, dest, pairs);
                if (sw.ShowDialog() != true) return;
                swap = sw.Swap;
            }
        }

        int n = 0, swapped = 0;
        var snap = Store.Snapshot(d);
        foreach (var st in stacks)
        {
            int q = Math.Min(dlg.Qty ?? st.Qty, st.Qty);
            string fromLoc = st.Loc, fromSector = st.Sector;
            if (swap && Counterpart(st) is Stack other)
            {
                // troca de arte: a carta continua nos dois lugares, só as cópias físicas trocam
                int t = Math.Min(q, other.Qty);
                Store.Move(d, st, t, dest, "", keepWants: true);
                Store.Move(d, other, t, fromLoc, fromSector, keepWants: true);
                swapped += t; q -= t;
            }
            if (q > 0) { Store.Move(d, st, q, dest, dlg.Sector); n += q; }
        }
        deckSel.Clear();
        Commit();
        ShowToast(swapped > 0 ? $"{swapped} trocada(s) com {dest}" + (n > 0 ? $", {n} movida(s)" : "") : $"{n} cópia(s) movida(s) para {dest}", snap);
    }

    void HaveThese_Click(object s, RoutedEventArgs e)
    {
        if (!Review(Cur!, null, SelectedCards())) Flash("Já tem todas");
        Commit();
    }

    // arte -> "Jodah, Neyith" (decks onde as cópias estão)
    static Dictionary<string, string> InUse(IEnumerable<Stack> ss) =>
        ss.GroupBy(x => x.Print).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Loc).Distinct(CI)));

    // Tela de revisão do que falta no deck; aplica a escolha de cada carta.
    // es: arte/foil da lista importada; only: limita às cartas selecionadas. false = nada faltando.
    bool Review(Location l, List<Entry>? es, List<string>? only = null)
    {
        static string Where(IEnumerable<Stack> ss) => string.Join(" · ", ss.GroupBy(s => (s.Loc, s.Sector))
            .Select(g => (g.Key.Sector == "" ? g.Key.Loc : $"{g.Key.Loc} / {g.Key.Sector}") + $" ({g.Sum(s => s.Qty)})"));

        var rows = new List<ReviewRow>();
        foreach (var c in l.Wants.Where(w => w.Board != Boards.Maybe).Select(w => w.Card).Distinct(CI))
        {
            if (only != null && !only.Contains(c, CI)) continue;
            int need = Store.Missing(d, l, c);
            if (need == 0) continue;
            var hint = es?.FirstOrDefault(x => Store.Same(x.Name, c) && x.Print != "")
                ?? (Store.DesiredOf(l, c) is { Length: > 0 } dp ? new Entry(1, c, dp, false, Boards.Main) : es?.FirstOrDefault(x => Store.Same(x.Name, c)));
            var free = Store.FreeStacks(d, c).ToList();
            var other = Store.OtherDeckStacks(d, c, l).ToList();
            var freeArts = free.GroupBy(x => (x.Print, x.Foil)).OrderByDescending(g => g.Sum(x => x.Qty))
                .Select(g => (g.Key.Print, g.Key.Foil, g.Sum(x => x.Qty), Where(g))).ToList();
            bool specified = hint?.Print is { Length: > 0 };
            // sem arte na lista: sugere a cópia livre que você mais tem
            var (print, foil) = specified ? (hint!.Print, hint.Foil)
                : freeArts.Count > 0 ? (freeArts[0].Print, freeArts[0].Foil)
                : (d.Prints.GetValueOrDefault(c, ""), hint?.Foil ?? false);
            rows.Add(new ReviewRow
            {
                Card = c, Print = print, Foil = foil, Specified = specified, Need = need, FreeArts = freeArts,
                InUse = InUse(other),
                Free = free.Sum(x => x.Qty), FreeWhere = Where(free),
                Other = other.Sum(x => x.Qty), OtherWhere = Where(other),
                OtherByDeck = other.GroupBy(x => x.Loc, CI).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty)),
            });
        }
        if (rows.Count == 0) return false;

        var dlg = new ReviewDialog(l.Name, rows, haveDefault: only != null);
        if (dlg.ShowDialog() != true) return true;

        foreach (var r in rows)
        {
            int left = r.Need;
            if (r.Action != ReviewDialog.Miss) left -= Store.Pull(d, l, Store.FreeStacks(d, r.Card), r.Card, r.Print, r.Foil, left);
            if (r.StealFrom is string from) left -= Store.Pull(d, l, Store.OtherDeckStacks(d, r.Card, l).Where(x => Store.Same(x.Loc, from)), r.Card, r.Print, r.Foil, left);
            if (r.Action == ReviewDialog.Create && left > 0) Store.AddStack(d, r.Card, r.Print, r.Foil, left, l.Name, "");
            // usou da coleção: a cópia que entrou é a arte que vale (a da lista não vira "desejada")
            if (r.Action != ReviewDialog.Miss) Store.AcceptOwnedArt(d, l, r.Card);
        }
        return true;
    }

    void ToggleCmd_Click(object s, RoutedEventArgs e)
    {
        var sel = SelectedCards();
        foreach (var w in Cur!.Wants.Where(w => sel.Contains(w.Card, CI) && w.Board is Boards.Main or Boards.Cmd))
            w.Board = w.Board == Boards.Cmd ? Boards.Main : Boards.Cmd;
        Commit();
    }

    // ---------- locais ----------
    bool ValidName(string name, string? current)
    {
        if (name == "" || Store.Same(name, "Início")) { Flash("Escolha um nome"); return false; }
        if ((current == null || !Store.Same(name, current)) && d.Locations.Any(l => Store.Same(l.Name, name)))
        { Flash($"Já existe \"{name}\" — escolha outro nome"); return false; }
        return true;
    }

    void NewDeck_Click(object s, RoutedEventArgs e) => NewDeck();

    // Depois de criar, a tela de revisão decide de onde vem cada carta.
    async void NewDeck()
    {
        var dlg = new ListDialog("Novo deck", "", name: "", link: true);
        if (dlg.ShowDialog() != true) return;
        var name = dlg.NameBox.Text.Trim();
        var link = dlg.LinkBox.Text.Trim();
        List<Entry> es;
        if (link != "")
        {
            if (await LoadMox(link) is not var (mname, mes)) return;
            es = mes;
            if (name == "") name = mname;
        }
        else es = Store.Parse(dlg.Body.Text, lastLineIsCmd: true);
        if (!ValidName(name, null)) return;

        var l = new Location { Name = name, Kind = "deck", Wants = Store.ToWants(es), Source = link == "" ? null : link };
        Store.Insert(d, l);
        TakePrints(es, false);
        Review(l, es);
        Commit(name);
        Flash($"{es.Sum(x => x.N)} cartas · faltam {Store.DeckMissing(d, l)}");
    }

    void EditDeck_Click(object s, RoutedEventArgs e)
    {
        var l = Cur!;
        var dlg = new ListDialog("Editar deck", Store.FormatDeck(d, l), name: l.Name);
        if (dlg.ShowDialog() != true) return;
        var name = dlg.NameBox.Text.Trim();
        if (!ValidName(name, l.Name)) return;
        var es = Store.Parse(dlg.Body.Text);
        foreach (var en in es) if (en.Print != "") d.Prints.TryAdd(en.Name, en.Print);
        Store.SetWants(d, l, Store.ToWants(es));
        Store.RenameLoc(d, l, name);
        Review(l, es);
        Commit(name);
    }

    async Task<(string name, List<Entry> es)?> LoadMox(string url)
    {
        Flash("Baixando do Moxfield…");
        try { return await Moxfield.Load(url); }
        catch (Exception ex) { Flash($"Moxfield: {ex.Message}"); return null; }
    }

    // Moxfield: as artes de lá são a verdade.
    void TakePrints(List<Entry> es, bool force)
    {
        foreach (var en in es.Where(x => x.Print != ""))
            if (force) d.Prints[en.Name] = en.Print; else d.Prints.TryAdd(en.Name, en.Print);
    }

    async void SyncMox_Click(object s, RoutedEventArgs e)
    {
        var l = Cur!;
        if (await LoadMox(l.Source!) is not var (_, es)) return;
        Store.SetWants(d, l, Store.ToWants(es));
        TakePrints(es, false);
        Review(l, es);
        Commit(l.Name);
        Flash($"Sincronizado · faltam {Store.DeckMissing(d, l)}");
    }

    void NewLoc_Click(object s, RoutedEventArgs e) => NewLoc();

    async void NewLoc()
    {
        var dlg = new LocDialog("Novo binder / box", link: true);
        if (dlg.ShowDialog() != true) return;
        var name = dlg.NameBox.Text.Trim();
        var link = dlg.LinkBox.Text.Trim();
        List<Entry> es = new();
        if (link != "")
        {
            if (await LoadMox(link) is not var (mname, mes)) return;
            es = mes;
            if (name == "") name = mname;
        }
        if (!ValidName(name, null)) return;
        Store.Insert(d, new Location { Name = name, Kind = dlg.Kind });
        foreach (var en in es) Store.AddStack(d, en.Name, en.Print, en.Foil, en.N, name, en.Board is Boards.Main or Boards.Cmd ? "" : en.Board);
        TakePrints(es, false);
        Commit(name);
        if (es.Count > 0) Flash($"{es.Sum(x => x.N)} cartas adicionadas em {name}");
    }

    void RenameLoc_Click(object s, RoutedEventArgs e) => RenameLocation(Cur!);
    void DeleteLoc_Click(object s, RoutedEventArgs e) => DeleteLocation(Cur!);

    void RenameLocation(Location l)
    {
        var dlg = new LocDialog("Renomear", l.Name, l.Kind);
        if (dlg.ShowDialog() != true) return;
        var name = dlg.NameBox.Text.Trim();
        if (!ValidName(name, l.Name)) return;
        bool here = view != null && Store.Same(view, l.Name);
        Store.RenameLoc(d, l, name);
        l.Kind = dlg.Kind;
        if (here) view = name;
        Commit(here ? name : view);
    }

    // Confirma e pergunta: desmontou (cartas pra um binder/box à escolha) ou vendeu (saem da coleção)?
    void DeleteLocation(Location l)
    {
        int copies = d.Stacks.Where(x => Store.Same(x.Loc, l.Name)).Sum(x => x.Qty);
        var dlg = new DeleteDialog(d, l, copies);
        if (dlg.ShowDialog() != true) return;
        bool here = view != null && Store.Same(view, l.Name);
        var snap = Store.Snapshot(d);
        Store.DeleteLoc(d, l, dlg.RemoveCards, dlg.Target);
        if (here) { view = null; deckSel.Clear(); }
        Commit(here ? "" : view);
        ShowToast(copies == 0 ? $"{l.Name} excluído" : dlg.RemoveCards ? $"{l.Name} excluído · {copies} cópia(s) removida(s) da coleção" : $"{l.Name} excluído · {copies} cópia(s) foram pra {dlg.Target}", snap);
    }

    void AddList_Click(object s, RoutedEventArgs e)
    {
        var target = Cur?.Name ?? Store.Default(d);
        var sectors = d.Stacks.Where(x => Store.Same(x.Loc, target) && x.Sector != "").Select(x => x.Sector).Distinct(CI).OrderBy(x => x);
        var dlg = new ListDialog($"Adicionar em {target}", "", sectors: sectors);
        if (dlg.ShowDialog() != true) return;
        if (Liga.IsLiga(dlg.Body.Text)) { ImportLiga(target, dlg.SectorBox.Text.Trim(), dlg.Body.Text); return; }
        foreach (var en in Store.Parse(dlg.Body.Text))
        {
            Store.AddStack(d, en.Name, en.Print, en.Foil, en.N, target, dlg.SectorBox.Text.Trim());
            if (en.Print != "") d.Prints.TryAdd(en.Name, en.Print);
        }
        Commit();
    }

    // Compra da LigaMagic (favorito "Copiar compras"): identifica a arte de cada carta e mostra pra conferir.
    async void ImportLiga(string target, string sector, string text)
    {
        var items = Liga.Parse(text);
        if (items.Count == 0) { Flash("Nenhuma carta no texto da LigaMagic"); return; }
        var matches = new List<Liga.Match>();
        for (int i = 0; i < items.Count; i++)
        {
            ShowToast($"Identificando artes no Scryfall… {i + 1}/{items.Count}", null);
            matches.Add(await Liga.Resolve(items[i]));
        }
        HideToast();
        var dlg = new LigaImportDialog(target, matches) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var snap = Store.Snapshot(d);
        for (int i = 0; i < matches.Count; i++)
        {
            var it = matches[i].Item;
            Store.AddStack(d, it.Name, dlg.Prints[i], it.Foil, it.Qty, target, sector);
            if (dlg.Prints[i] != "") d.Prints.TryAdd(it.Name, dlg.Prints[i]);
        }
        Commit();
        ShowToast($"{items.Sum(x => x.Qty)} cartas da LigaMagic adicionadas em {target}", snap);
    }

    // ---------- copiar ----------
    void Flash(string msg) => ShowToast(msg, null);

    void Copy(string text, string what)
    {
        if (text == "") { Flash("Nada para copiar"); return; }
        Clipboard.SetText(text);
        Flash($"{what} copiada");
    }

    void CopyAll_Click(object s, RoutedEventArgs e) => Copy(Store.FormatStacks(d.Stacks), "Coleção");
    void CopyStore_Click(object s, RoutedEventArgs e) => Copy(Store.FormatStacks(d.Stacks.Where(x => Store.Same(x.Loc, view!))), "Lista");
    void CopyDeck_Click(object s, RoutedEventArgs e) => Copy(Store.FormatDeck(d, Cur!), "Lista");

    // ---------- Geral: coleção / faltando nos decks ----------
    bool geralWants;

    void BuildGeralTabs(int wants)
    {
        SectorBar.Children.Clear();
        void tab(string text, bool on, bool value)
        {
            var b = new Border
            {
                Child = new TextBlock { Text = text, Foreground = on ? Theme.B("OnAccent") : Theme.B("Text") },
                Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(14), Cursor = Cursors.Hand,
                Background = on ? Theme.B("Accent") : Theme.B("Chip"),
            };
            b.MouseLeftButtonUp += (_, _) => { geralWants = value; page = 0; Render(); };
            SectorBar.Children.Add(b);
        }
        tab("Coleção", !geralWants, false);
        tab($"Faltando nos decks  {wants}", geralWants, true);
    }

    // Want list: tudo que falta nos decks; quantas dá pra puxar de binders/boxes e quantas comprar.
    List<WantRow> WantRows()
    {
        var need = new Dictionary<string, List<(string deck, int n)>>(CI);
        foreach (var l in d.Locations.Where(x => x.Kind == "deck"))
            foreach (var c in l.Wants.Where(w => w.Board != Boards.Maybe).Select(w => w.Card).Distinct(CI))
                if (Store.Missing(d, l, c) is > 0 and var n)
                {
                    if (!need.TryGetValue(c, out var list)) need[c] = list = new();
                    list.Add((l.Name, n));
                }
        return need.Select(kv =>
        {
            int falta = kv.Value.Sum(x => x.n), free = Store.Free(d, kv.Key), buy = Math.Max(0, falta - free);
            var (dot, tip) = buy == 0 ? (DotBlue, $"Dá pra puxar tudo da coleção ({free} livre)")
                : Store.Have(d, kv.Key) > free ? (DotAmber, "Você tem, mas em uso em outros decks — precisa comprar")
                : (DotRed, "Você não tem — precisa comprar");
            return new WantRow(dot, tip, kv.Key, falta, free, buy, string.Join("  ·  ", kv.Value.Select(x => $"{x.deck} ({x.n})")));
        }).OrderByDescending(r => r.Comprar > 0).ThenBy(r => r.Carta).ToList();
    }

    // Binder/box: amarelo quando algum deck precisa dessa carta (dá pra levar pra lá).
    (Brush? dot, string tip) NeededDot(string card)
    {
        var where = d.Locations.Where(x => x.Kind == "deck").Select(x => (x.Name, n: Store.Missing(d, x, card))).Where(x => x.n > 0).ToList();
        return where.Count == 0 ? (null, "") : (DotAmber, "Falta em " + string.Join(", ", where.Select(x => $"{x.Name} ({x.n})")));
    }

    // Coluna da bolinha nas tabelas (Brush "Dot" + tooltip "Tip").
    static DataGridTemplateColumn DotColumn()
    {
        var f = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
        f.SetValue(WidthProperty, 8.0); f.SetValue(HeightProperty, 8.0);
        f.SetBinding(System.Windows.Shapes.Shape.FillProperty, new Binding("Dot"));
        f.SetBinding(ToolTipProperty, new Binding("Tip"));
        f.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        return new DataGridTemplateColumn { Header = "", Width = new DataGridLength(26), CellTemplate = new DataTemplate { VisualTree = f } };
    }

    // Só as cartas das seções pedidas, formato Moxfield ("1 Sol Ring (CMM) 410"), sem cabeçalhos.
    string BoardText(Location l, params string[] boards)
    {
        var cards = Store.NewCards();
        foreach (var w in l.Wants.Where(w => boards.Contains(w.Board))) cards[w.Card] = cards.GetValueOrDefault(w.Card) + w.Qty;
        return Store.FormatCards(cards, cards.Keys.ToDictionary(c => c, c => Store.DesiredOf(l, c) is { Length: > 0 } p ? p : Store.DeckPrint(d, l, c), CI));
    }

    // Ações do deck que não precisam ficar sempre à vista.
    void DeckMore_Click(object s, RoutedEventArgs e)
    {
        var l = Cur!;
        var m = new ContextMenu { PlacementTarget = DeckMoreBtn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        void add(string text, Action act, Brush? color = null)
        {
            var mi = new MenuItem { Header = text };
            if (color != null) mi.Foreground = color;
            mi.Click += (_, _) => act();
            m.Items.Add(mi);
        }
        if (l.Kind != "deck")
        {
            add("Exportar lista (.txt)…", () => SaveExport(l));
            add("Copiar lista", () => CopyStore_Click(this, e));
            add("Copiar para LigaMagic", () =>
            {
                var cards = Store.NewCards();
                foreach (var st in d.Stacks.Where(x => Store.Same(x.Loc, l.Name))) cards[st.Card] = cards.GetValueOrDefault(st.Card) + st.Qty;
                CopyLiga(cards, "Lista");
            });
            add("Adicionar lista…", () => AddList_Click(this, e));
            m.Items.Add(new Separator());
            add("Renomear…", () => RenameLocation(l));
            if (!Store.IsDefault(d, l)) add("Definir como local padrão", () => { d.DefaultLoc = l.Name; Commit(); Flash($"{l.Name} agora é o local padrão"); });
            m.Items.Add(new Separator());
            add("Excluir…", () => DeleteLocation(l), Theme.B("DangerText"));
            m.IsOpen = true;
            return;
        }
        // Moxfield cola cada seção numa aba: a principal é o Main; o resto fica em "Copiar ▸"
        add("Copiar main deck", () => Copy(BoardText(l, Boards.Main), "Main deck"));
        var more = new MenuItem { Header = "Copiar" };
        void sub(string text, Action act) { var mi = new MenuItem { Header = text }; mi.Click += (_, _) => act(); more.Items.Add(mi); }
        sub("Comandante", () => Copy(BoardText(l, Boards.Cmd), "Comandante"));
        sub("Sideboard", () => Copy(BoardText(l, Boards.Side), "Sideboard"));
        sub("Considerando", () => Copy(BoardText(l, Boards.Maybe), "Considerando"));
        sub("Main + Sideboard", () => Copy(BoardText(l, Boards.Main, Boards.Side), "Main + Sideboard"));
        more.Items.Add(new Separator());
        sub("Lista completa com seções (deck novo)", () => CopyDeck_Click(this, e));
        m.Items.Add(more);
        add("Exportar lista (.txt)…", () => SaveExport(l));
        int up = Store.ArtUpgrades(d, l).Values.Sum();
        add(up > 0 ? $"Copiar artes faltantes ({up})" : "Copiar artes faltantes", () => CopyUpgrades_Click(this, e));
        m.Items.Add(new Separator());
        add("LigaMagic — copiar deck", () => CopyLiga(DeckCards(l, missingOnly: false), "Deck"));
        add("LigaMagic — copiar faltantes", () => CopyLiga(DeckCards(l, missingOnly: true), "Faltantes"));
        if (l.Source != null) add("Sincronizar com o Moxfield", () => SyncMox_Click(this, e));
        m.Items.Add(new Separator());
        add("Manter as artes que já estão no deck", () =>
        {
            var snap = Store.Snapshot(d);
            int n = Store.AcceptOwnedArt(d, l);
            if (n == 0) { Flash("As artes do deck já batem com as cópias"); return; }
            Commit();
            ShowToast($"{n} carta(s) agora usam a arte da cópia que está no deck", snap);
        });
        m.Items.Add(new Separator());
        add("Editar lista…", () => EditDeck_Click(this, e));
        add("Renomear…", () => RenameLocation(l));
        m.Items.Add(new Separator());
        add("Excluir deck…", () => DeleteLocation(l), Theme.B("DangerText"));
        m.IsOpen = true;
    }

    // ---------- LigaMagic: "1 Nome" sem arte, dupla-face só com a frente ----------
    // Comandante + main + sideboard (só o "considerando" fica de fora).
    Dictionary<string, int> DeckCards(Location l, bool missingOnly)
    {
        var o = Store.NewCards();
        foreach (var c in l.Wants.Where(w => w.Board != Boards.Maybe).Select(w => w.Card).Distinct(CI))
            o[c] = missingOnly ? Store.Missing(d, l, c) : l.Wants.Where(w => Store.Same(w.Card, c) && w.Board != Boards.Maybe).Sum(w => w.Qty);
        return o;
    }

    async void CopyLiga(Dictionary<string, int> cards, string what)
    {
        if (cards.Values.All(n => n <= 0)) { Flash("Nada para copiar"); return; }
        Flash("Preparando lista para a LigaMagic…");
        try { await Types.EnsureLayouts(cards.Keys); } catch { } // sem internet: mantém o nome inteiro das cartas de duas partes
        var merged = Store.NewCards();
        foreach (var (c, n) in cards.Where(x => x.Value > 0)) { var k = Types.LigaName(c); merged[k] = merged.GetValueOrDefault(k) + n; }
        Copy(string.Join("\r\n", merged.OrderBy(x => x.Key).Select(x => $"{x.Value} {x.Key}")), $"{what} (LigaMagic)");
    }

    void BuylistLiga_Click(object s, RoutedEventArgs e) => CopyLiga(Store.Buylist(d, out _), "Buylist");

    void CopyUpgrades_Click(object s, RoutedEventArgs e)
    {
        var l = Cur!;
        var up = Store.ArtUpgrades(d, l);
        Copy(Store.FormatCards(up, up.Keys.ToDictionary(c => c, c => Store.DesiredOf(l, c), CI)), "Lista de artes faltantes");
    }

    void CopyMissing_Click(object s, RoutedEventArgs e)
    {
        var l = Cur!;
        var cards = Store.NewCards();
        foreach (var c in l.Wants.Where(w => w.Board != Boards.Maybe).Select(w => w.Card).Distinct(CI)) cards[c] = Store.Missing(d, l, c);
        Copy(Store.FormatCards(cards, d.Prints), "Lista de faltantes");
    }

    void Buylist_Click(object s, RoutedEventArgs e)
    {
        var buy = Store.Buylist(d, out int covered);
        var text = Store.FormatCards(buy, d.Prints);
        if (text != "") Clipboard.SetText(text);
        Flash($"Buylist: {buy.Values.Sum()} carta(s)" + (covered > 0 ? $" · {covered} dá pra puxar de binders/boxes" : "") + (text != "" ? " · copiada" : ""));
    }
}

// Cabeçalho do grupo no Geral: "4 cópias · 3 artes · 1 foil"
public class GroupSummaryConv : IValueConverter
{
    public object Convert(object v, Type t, object p, System.Globalization.CultureInfo c)
    {
        var rows = ((System.Collections.IEnumerable)v).OfType<ArtRow>().ToList();
        int copies = rows.Sum(r => r.Qtd), arts = rows.Select(r => r.Print).Distinct().Count(), foil = rows.Where(r => r.Foil != "").Sum(r => r.Qtd);
        return $"{copies} {(copies == 1 ? "cópia" : "cópias")}" + (arts > 1 ? $"  ·  {arts} artes" : "") + (foil > 0 ? $"  ·  {foil} foil" : "");
    }
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
}
