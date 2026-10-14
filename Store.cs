using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MtgEstoque;

public static class Boards
{
    public const string Cmd = "Comandante", Main = "Main", Side = "Sideboard", Maybe = "Considerando";
}

// Print = arte no formato do Scryfall ("eoe/251"), ou "" quando não se sabe.
public record Entry(int N, string Name, string Print, bool Foil, string Board);

// Uma linha da lista do deck: cópias físicas (S != null) ou o que falta (S == null).
public record DeckLine(Want W, Stack? S, int Qty, int Missing);

// O que um deck PEDE (pode incluir carta que você não tem).
public class Want
{
    public string Card { get; set; } = "";
    public int Qty { get; set; }
    public string Board { get; set; } = Boards.Main;
    public string Print { get; set; } = ""; // arte desejada neste deck ("" = qualquer)
}

// Kind: deck | binder | box | global
public class Location
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "box";
    public List<Want> Wants { get; set; } = new();
    public string? Source { get; set; } // link do Moxfield, para sincronizar
    public string? Cover { get; set; }  // imagem do deck: "carta|arte"; vazio = comandante
    public List<string> Sectors { get; set; } = new(); // setores criados (podem estar vazios)
}

// Cópias físicas: cada uma está em exatamente um local.
public class Stack
{
    public string Card { get; set; } = "";
    public string Print { get; set; } = "";
    public bool Foil { get; set; }
    public int Qty { get; set; }
    public string Loc { get; set; } = "";
    public string Sector { get; set; } = "";
}

// Uma cópia que mudou de lugar (pra achar fisicamente depois).
public class Moved
{
    public DateTime When { get; set; }
    public string Card { get; set; } = "";
    public string Print { get; set; } = "";
    public bool Foil { get; set; }
    public int Qty { get; set; }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}

public class Data
{
    public int Version { get; set; } = 2;
    public List<Location> Locations { get; set; } = new();
    public List<Stack> Stacks { get; set; } = new();
    // Arte exibida/exportada por carta.
    public Dictionary<string, string> Prints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Moved> Log { get; set; } = new();
    public string? DefaultLoc { get; set; } // binder/box padrão (null = o primeiro)

    // Formatos antigos; só lidos para migrar.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, Dictionary<string, int>>? Owned { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, Dictionary<string, int>>? Decks { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, List<string>>? Commanders { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, int>? Collection { get; set; }
}

public static class Store
{
    // Local padrão das cartas sem lugar definido: o que o usuário escolheu, senão o primeiro binder/box.
    // Só é criado (com este nome) quando precisa guardar carta e não existe nenhum binder/box.
    public const string DefaultName = "Avulsas";

    public static string Default(Data d)
    {
        var l = d.Locations.FirstOrDefault(x => x.Kind != "deck" && d.DefaultLoc != null && Same(x.Name, d.DefaultLoc))
             ?? d.Locations.FirstOrDefault(x => x.Kind != "deck");
        if (l == null) d.Locations.Add(l = new Location { Name = DefaultName, Kind = "box" });
        d.DefaultLoc = l.Name;
        return l.Name;
    }

    public static bool IsDefault(Data d, Location l) => l.Kind != "deck" && Same(Default(d), l.Name);
    public static readonly StringComparer CI = StringComparer.OrdinalIgnoreCase;
    public static bool Same(string a, string b) => CI.Equals(a, b);

    // Pasta da coleção + cache de imagens. O instalador grava a escolhida em "datadir.txt" ao lado do
    // .exe; sem ele, usa %APPDATA%\MtgEstoque.
    public static readonly string Dir = ResolveDir();

    static string ResolveDir()
    {
        try
        {
            var f = System.IO.Path.Combine(AppContext.BaseDirectory, "datadir.txt");
            if (File.Exists(f) && File.ReadAllText(f).Trim() is { Length: > 0 } p) return Environment.ExpandEnvironmentVariables(p);
        }
        catch { }
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MtgEstoque");
    }
    static readonly string FilePath = System.IO.Path.Combine(Dir, "data.json");

    // "1 Sol Ring (FIC) 357 *F*" -> qtd, nome, set, número, marcadores (*F* = foil).
    static readonly Regex LineRx = new(@"^(\d+)x?\s+(.+?)(?:\s+\(([^)]+)\)(?:\s+([^\s*]+))?)?(?:\s+(\*.*))?$");
    static readonly Regex HeaderRx = new(@"^(commanders?|main(board)?|deck|sideboard|considering|maybeboard)\s*:?$", RegexOptions.IgnoreCase);

    public static Dictionary<string, int> NewCards() => new(CI);

    // ---------- persistência ----------
    public static Data Load() => FromJson(File.Exists(FilePath) ? File.ReadAllText(FilePath) : "{}");

    // Cópia do estado inteiro, para o "Desfazer".
    public static string Snapshot(Data d) => JsonSerializer.Serialize(d);

    public static Data FromJson(string json)
    {
        var d = JsonSerializer.Deserialize<Data>(json) ?? new Data();
        d.Prints = new(d.Prints ?? new(), CI);
        foreach (var l in d.Locations) l.Sectors ??= new();
        d.Log ??= new();
        Migrate(d);
        // versões antigas tinham um local fixo ("Caixa global"/"cartinhas"): vazio e com outros binders -> some;
        // com cartas -> vira um box comum chamado "Avulsas" e fica como padrão
        var g = d.Locations.FirstOrDefault(l => l.Kind == "global");
        if (g != null)
        {
            g.Kind = "box";
            if (!d.Stacks.Any(s => Same(s.Loc, g.Name)) && d.Locations.Any(l => l != g && l.Kind != "deck")) d.Locations.Remove(g);
            else
            {
                if (Same(g.Name, "cartinhas") || Same(g.Name, "Caixa global")) RenameLoc(d, g, DefaultName);
                d.DefaultLoc ??= g.Name;
            }
        }
        return d;
    }

    public static void Save(Data d)
    {
        Directory.CreateDirectory(Dir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, true); // escrita atômica, não corrompe se cair no meio
    }

    // v0 (Collection) / v1 (Owned + Decks por prioridade) -> locais e pilhas.
    static void Migrate(Data d)
    {
        if (d.Collection != null)
        {
            d.Owned ??= new(CI);
            foreach (var (c, n) in d.Collection)
            {
                if (!d.Owned.TryGetValue(c, out var p)) d.Owned[c] = p = new();
                var pr = d.Prints.GetValueOrDefault(c, "");
                p[pr] = p.GetValueOrDefault(pr) + n;
            }
        }
        if (d.Owned == null && d.Decks == null) return;

        var pool = (d.Owned ?? new()).SelectMany(x => x.Value.Select(p => new Stack { Card = x.Key, Print = p.Key, Qty = p.Value })).ToList();
        foreach (var (name, cards) in d.Decks ?? new())
        {
            var cmds = d.Commanders?.GetValueOrDefault(name) ?? new();
            d.Locations.Add(new Location
            {
                Name = name, Kind = "deck",
                Wants = cards.Select(kv => new Want { Card = kv.Key, Qty = kv.Value, Board = cmds.Contains(kv.Key, CI) ? Boards.Cmd : Boards.Main }).ToList(),
            });
            foreach (var (c, need) in cards)
            {
                int left = need;
                foreach (var s in pool.Where(s => Same(s.Card, c) && s.Qty > 0))
                {
                    int t = Math.Min(left, s.Qty);
                    if (t == 0) break;
                    s.Qty -= t; left -= t;
                    AddStack(d, c, s.Print, false, t, name, "");
                }
            }
        }
        foreach (var s in pool.Where(s => s.Qty > 0)) AddStack(d, s.Card, s.Print, false, s.Qty, Default(d), "");
        d.Collection = null; d.Owned = null; d.Decks = null; d.Commanders = null;
    }

    // ---------- consultas ----------
    public static Location Loc(Data d, string name) => d.Locations.First(l => Same(l.Name, name));
    public static bool IsDeck(Data d, string loc) => Loc(d, loc).Kind == "deck";
    public static int Have(Data d, string card) => d.Stacks.Where(s => Same(s.Card, card)).Sum(s => s.Qty);
    public static int InLoc(Data d, string card, string loc) => d.Stacks.Where(s => Same(s.Card, card) && Same(s.Loc, loc)).Sum(s => s.Qty);
    public static IEnumerable<Stack> FreeStacks(Data d, string card) => d.Stacks.Where(s => Same(s.Card, card) && !IsDeck(d, s.Loc));
    public static int Free(Data d, string card) => FreeStacks(d, card).Sum(s => s.Qty);

    // counted = só o que conta como "faltando" (Considerando não conta).
    public static int Wanted(Location l, string card, bool counted) =>
        l.Wants.Where(w => Same(w.Card, card) && (!counted || w.Board != Boards.Maybe)).Sum(w => w.Qty);

    public static int Missing(Data d, Location l, string card) => Math.Max(0, Wanted(l, card, true) - InLoc(d, card, l.Name));

    // Distribui as cópias que estão no deck entre os boards (Comandante, Main, Sideboard, Considerando):
    // uma linha por arte/foil, mais uma linha com o que falta.
    public static List<DeckLine> DeckLines(Data d, Location l)
    {
        string[] order = { Boards.Cmd, Boards.Main, Boards.Side, Boards.Maybe };
        var o = new List<DeckLine>();
        foreach (var g in l.Wants.GroupBy(w => w.Card, CI))
        {
            var pool = d.Stacks.Where(s => Same(s.Loc, l.Name) && Same(s.Card, g.Key)).OrderBy(s => s.Print).ThenBy(s => s.Foil).ToList();
            int pi = 0, used = 0;
            foreach (var w in g.OrderBy(w => Array.IndexOf(order, w.Board)))
            {
                int need = w.Qty;
                while (need > 0 && pi < pool.Count)
                {
                    int t = Math.Min(pool[pi].Qty - used, need);
                    o.Add(new DeckLine(w, pool[pi], t, 0));
                    need -= t; used += t;
                    if (used == pool[pi].Qty) { pi++; used = 0; }
                }
                if (need > 0) o.Add(new DeckLine(w, null, need, w.Board == Boards.Maybe ? 0 : need));
            }
            for (; pi < pool.Count; pi++, used = 0) o.Add(new DeckLine(g.First(), pool[pi], pool[pi].Qty - used, 0)); // cópias além da lista
        }
        return o;
    }

    public static int DeckMissing(Data d, Location l) =>
        l.Wants.Where(w => w.Board != Boards.Maybe).Select(w => w.Card).Distinct(CI).Sum(c => Missing(d, l, c));

    // Tudo que falta nos decks, descontando cópias livres em binders/boxes/Caixa global.
    public static Dictionary<string, int> Buylist(Data d, out int coveredByFree)
    {
        var need = NewCards();
        foreach (var l in d.Locations.Where(l => l.Kind == "deck"))
            foreach (var c in l.Wants.Where(w => w.Board != Boards.Maybe).Select(w => w.Card).Distinct(CI))
                need[c] = need.GetValueOrDefault(c) + Missing(d, l, c);
        coveredByFree = need.Sum(kv => Math.Min(kv.Value, Free(d, kv.Key)));
        var buy = NewCards();
        foreach (var (c, n) in need) { int b = n - Free(d, c); if (b > 0) buy[c] = b; }
        return buy;
    }

    // ---------- mudanças ----------
    public static void AddStack(Data d, string card, string print, bool foil, int n, string loc, string sector)
    {
        var s = d.Stacks.FirstOrDefault(x => Same(x.Card, card) && x.Print == print && x.Foil == foil && Same(x.Loc, loc) && Same(x.Sector, sector));
        if (s == null)
        {
            if (n <= 0) return;
            d.Stacks.Add(s = new Stack { Card = card, Print = print, Foil = foil, Loc = loc, Sector = sector });
        }
        s.Qty += n;
        if (s.Qty <= 0) d.Stacks.Remove(s);
    }

    public static string Place(string loc, string sector) => sector == "" ? loc : $"{loc} / {sector}";

    // ponytail: guarda as últimas 5000 movimentações; o resto é descartado
    static void Record(Data d, string card, string print, bool foil, int n, string from, string to)
    {
        if (n <= 0 || from == to) return;
        d.Log.Add(new Moved { When = DateTime.Now, Card = card, Print = print, Foil = foil, Qty = n, From = from, To = to });
        if (d.Log.Count > 5000) d.Log.RemoveRange(0, d.Log.Count - 5000);
    }

    public static void Take(Data d, Stack s, int n)
    {
        s.Qty -= n;
        if (s.Qty <= 0) d.Stacks.Remove(s);
    }

    // Mover = a carta sai de um lugar e entra no outro: tira da lista do deck de origem (sem deixar
    // "rastro" faltando) e entra na lista do deck de destino. keepWants: só muda a cópia física de
    // lugar, sem mexer nas listas (troca de arte, sobras, exclusão de local).
    public static void Move(Data d, Stack s, int n, string to, string sector, bool keepWants = false)
    {
        n = Math.Min(n, s.Qty);
        if (n <= 0) return;
        string card = s.Card, from = s.Loc;
        bool toDeck = IsDeck(d, to);
        if (toDeck) sector = "";
        Record(d, card, s.Print, s.Foil, n, Place(s.Loc, s.Sector), Place(to, sector));
        AddStack(d, card, s.Print, s.Foil, n, to, sector);
        Take(d, s, n);
        if (keepWants || Same(from, to)) return;
        if (IsDeck(d, from))
        {
            var src = Loc(d, from);
            int over = Wanted(src, card, false) - InLoc(d, card, from); // só o que o deck passou a não ter
            if (over > 0) ReduceWant(src, card, null, Math.Min(over, n));
        }
        if (toDeck) FitWants(d, Loc(d, to));
    }

    // Tira n da lista do deck (board null = Main, Sideboard, Considerando e por último Comandante).
    public static void ReduceWant(Location l, string card, string? board, int n)
    {
        string[] order = { Boards.Main, Boards.Side, Boards.Maybe, Boards.Cmd };
        foreach (var w in l.Wants.Where(w => Same(w.Card, card) && (board == null || w.Board == board)).OrderBy(w => Array.IndexOf(order, w.Board)).ToList())
        {
            if (n <= 0) break;
            int t = Math.Min(n, w.Qty);
            w.Qty -= t; n -= t;
        }
        l.Wants.RemoveAll(w => w.Qty <= 0);
    }

    // Remove do deck: tira da lista e manda a cópia física (de preferência 'prefer') pro local padrão.
    public static void RemoveFromDeck(Data d, Location l, string card, string board, int n, Stack? prefer)
    {
        ReduceWant(l, card, board, n);
        int excess = InLoc(d, card, l.Name) - Wanted(l, card, false);
        foreach (var s in d.Stacks.Where(s => Same(s.Loc, l.Name) && Same(s.Card, card)).OrderByDescending(s => s == prefer).ToList())
        {
            if (excess <= 0) break;
            int t = Math.Min(excess, s.Qty);
            Move(d, s, t, Default(d), "", keepWants: true);
            excess -= t;
        }
    }

    // Main <-> Sideboard <-> Considerando <-> Comandante.
    public static void SetBoard(Location l, string card, string from, string to, int n)
    {
        ReduceWant(l, card, from, n);
        var w = l.Wants.FirstOrDefault(w => Same(w.Card, card) && w.Board == to);
        if (w == null) l.Wants.Add(new Want { Card = card, Qty = n, Board = to }); else w.Qty += n;
    }

    // Cópia física num deck sem estar na lista -> entra no Main.
    public static void FitWants(Data d, Location l)
    {
        foreach (var c in d.Stacks.Where(s => Same(s.Loc, l.Name)).Select(s => s.Card).Distinct(CI).ToList())
        {
            int extra = InLoc(d, c, l.Name) - Wanted(l, c, false);
            if (extra <= 0) continue;
            var w = l.Wants.FirstOrDefault(w => Same(w.Card, c) && w.Board == Boards.Main);
            if (w == null) l.Wants.Add(new Want { Card = c, Qty = extra }); else w.Qty += extra;
        }
    }

    // Troca a lista do deck; cópias que sobraram vão pra Caixa global.
    public static void SetWants(Data d, Location l, List<Want> wants)
    {
        l.Wants = wants;
        foreach (var c in d.Stacks.Where(s => Same(s.Loc, l.Name)).Select(s => s.Card).Distinct(CI).ToList())
        {
            int excess = InLoc(d, c, l.Name) - Wanted(l, c, false);
            foreach (var s in d.Stacks.Where(s => Same(s.Loc, l.Name) && Same(s.Card, c)).ToList())
            {
                if (excess <= 0) break;
                int t = Math.Min(excess, s.Qty);
                Move(d, s, t, Default(d), "", keepWants: true);
                excess -= t;
            }
        }
    }

    public static IEnumerable<Stack> OtherDeckStacks(Data d, string card, Location l) =>
        d.Stacks.Where(s => Same(s.Card, card) && !Same(s.Loc, l.Name) && IsDeck(d, s.Loc));

    // Traz até n cópias de 'from' pro deck, preferindo a mesma arte/foil. A cópia física mantém a arte dela.
    // Cartas que faltam no deck e têm cópia livre (fora de decks): quantas dá pra puxar / puxa todas.
    public static int Pullable(Data d, Location l) =>
        l.Wants.Where(w => w.Board != Boards.Maybe).Select(w => w.Card).Distinct(CI).Sum(c => Math.Min(Missing(d, l, c), Free(d, c)));

    public static int PullFree(Data d, Location l) =>
        l.Wants.Where(w => w.Board != Boards.Maybe).Select(w => w.Card).Distinct(CI).ToList()
            .Sum(c => Pull(d, l, FreeStacks(d, c), c, DesiredOf(l, c), false, Missing(d, l, c)));

    public static int Pull(Data d, Location l, IEnumerable<Stack> from, string card, string print, bool foil, int n)
    {
        int got = 0;
        foreach (var s in from.OrderByDescending(s => s.Print == print).ThenByDescending(s => s.Foil == foil).ToList())
        {
            if (got == n) break;
            int t = Math.Min(n - got, s.Qty);
            Record(d, card, s.Print, s.Foil, t, Place(s.Loc, s.Sector), l.Name);
            Take(d, s, t);
            AddStack(d, card, s.Print, s.Foil, t, l.Name, "");
            got += t;
        }
        return got;
    }

    // Coloca no deck o que falta: puxa cópias livres; create: cria o resto como cópias novas.
    public static (int moved, int created) Fill(Data d, Location l, List<Entry> es, bool pull, bool create)
    {
        int moved = 0, created = 0;
        foreach (var e in es.Where(e => e.Board != Boards.Maybe))
        {
            int left = Math.Min(e.N, Missing(d, l, e.Name));
            if (pull) { int t = Pull(d, l, FreeStacks(d, e.Name), e.Name, e.Print, e.Foil, left); left -= t; moved += t; }
            if (create && left > 0) { AddStack(d, e.Name, e.Print, e.Foil, left, l.Name, ""); created += left; }
        }
        return (moved, created);
    }

    // removeCards: as cópias saem da coleção (vendeu); senão vão pra 'target' (padrão: o local padrão).
    public static void DeleteLoc(Data d, Location l, bool removeCards = false, string? target = null)
    {
        d.Locations.Remove(l);
        if (d.DefaultLoc != null && Same(d.DefaultLoc, l.Name)) d.DefaultLoc = null;
        var to = removeCards ? "" : target ?? Default(d);
        if (!removeCards && !d.Locations.Any(x => Same(x.Name, to))) d.Locations.Add(new Location { Name = to, Kind = "box" });
        foreach (var s in d.Stacks.Where(s => Same(s.Loc, l.Name)).ToList())
            if (removeCards) d.Stacks.Remove(s); else Move(d, s, s.Qty, to, "", keepWants: true);
    }

    // Setores de um local: os criados + os que têm carta.
    public static List<string> SectorsOf(Data d, Location l) =>
        l.Sectors.Concat(d.Stacks.Where(s => Same(s.Loc, l.Name) && s.Sector != "").Select(s => s.Sector)).Distinct(CI).OrderBy(s => s).ToList();

    public static void RenameSector(Data d, Location l, string old, string name)
    {
        foreach (var s in d.Stacks.Where(s => Same(s.Loc, l.Name) && Same(s.Sector, old)).ToList())
        {
            s.Sector = name; // pode juntar com uma pilha igual que já estava no setor novo
            var twin = d.Stacks.FirstOrDefault(x => x != s && Same(x.Loc, s.Loc) && Same(x.Sector, name) && Same(x.Card, s.Card) && x.Print == s.Print && x.Foil == s.Foil);
            if (twin != null) { twin.Qty += s.Qty; d.Stacks.Remove(s); }
        }
        l.Sectors.RemoveAll(x => Same(x, old));
        if (!l.Sectors.Contains(name, CI)) l.Sectors.Add(name);
    }

    // Excluir setor: as cartas dele ficam "Sem setor".
    public static void DeleteSector(Data d, Location l, string name)
    {
        foreach (var s in d.Stacks.Where(s => Same(s.Loc, l.Name) && Same(s.Sector, name)).ToList()) Move(d, s, s.Qty, l.Name, "");
        l.Sectors.RemoveAll(x => Same(x, name));
    }

    public static void RenameLoc(Data d, Location l, string name)
    {
        foreach (var s in d.Stacks.Where(s => Same(s.Loc, l.Name))) s.Loc = name;
        if (d.DefaultLoc != null && Same(d.DefaultLoc, l.Name)) d.DefaultLoc = name;
        l.Name = name;
    }

    // Decks antes dos binders; Caixa global sempre por último.
    public static void Insert(Data d, Location l)
    {
        int i = l.Kind == "deck" ? d.Locations.FindIndex(x => x.Kind != "deck") : -1;
        d.Locations.Insert(i < 0 ? d.Locations.Count : i, l);
    }

    // ---------- texto (formato Moxfield) ----------
    // Board: cabeçalhos (SIDEBOARD:, CONSIDERING:...). Comandante: linhas "SB:", senão o bloco depois
    // da última linha em branco, senão (lastLineIsCmd) a última carta.
    public static List<Entry> Parse(string text, bool lastLineIsCmd = false)
    {
        if (text.TrimStart().StartsWith("\"Count\"")) return ParseCsv(text);
        var lines = text.Replace("\r", "").Split('\n').Select(l => l.Trim()).ToList();
        static bool IsSb(string l) => l.StartsWith("SB:", StringComparison.OrdinalIgnoreCase);
        string Strip(string l) => IsSb(l) ? l[3..].Trim() : l;
        bool hasSb = lines.Any(IsSb), hasHeader = lines.Any(l => HeaderRx.IsMatch(l));
        int lastBlank = -1, pending = -1, lastCard = -1;
        bool seen = false;
        for (int i = 0; i < lines.Count; i++)
        {
            if (LineRx.IsMatch(Strip(lines[i]))) { seen = true; lastCard = i; if (pending >= 0) { lastBlank = pending; pending = -1; } }
            else if (lines[i] == "" && seen) pending = i;
        }

        var o = new List<Entry>();
        string board = Boards.Main;
        for (int i = 0; i < lines.Count; i++)
        {
            var h = HeaderRx.Match(lines[i]);
            if (h.Success)
            {
                var k = h.Groups[1].Value.ToLower();
                board = k.StartsWith("commander") ? Boards.Cmd : k == "sideboard" ? Boards.Side : k is "considering" or "maybeboard" ? Boards.Maybe : Boards.Main;
                continue;
            }
            var m = LineRx.Match(Strip(lines[i]));
            if (!m.Success) continue;
            string b = IsSb(lines[i]) ? Boards.Cmd
                : hasHeader ? board
                : !hasSb && (lastBlank >= 0 ? i > lastBlank : lastLineIsCmd && i == lastCard) ? Boards.Cmd
                : Boards.Main;
            var print = m.Groups[4].Success ? $"{m.Groups[3].Value.ToLower()}/{m.Groups[4].Value}" : "";
            bool foil = m.Groups[5].Value.Contains("*F*", StringComparison.OrdinalIgnoreCase);
            o.Add(new Entry(int.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim(), print, foil, b));
        }
        return o;
    }

    // Export CSV da coleção do Moxfield: "Count","Tradelist Count","Name","Edition",...,"Foil",...,"Collector Number","Alter","Proxy",...
    // Etched conta como foil; proxies são ignorados (não são cópias suas).
    static List<Entry> ParseCsv(string text)
    {
        static List<string> Fields(string line)
        {
            var o = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q && c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else if (c == '"') q = !q;
                else if (c == ',' && !q) { o.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(c);
            }
            o.Add(cur.ToString());
            return o;
        }
        var lines = text.Replace("\r", "").Split('\n').Where(l => l.Trim() != "").ToList();
        var h = Fields(lines[0]);
        int col(string n) => h.FindIndex(x => Same(x, n));
        int count = col("Count"), name = col("Name"), set = col("Edition"), foil = col("Foil"), cn = col("Collector Number"), proxy = col("Proxy");
        var o = new List<Entry>();
        foreach (var line in lines.Skip(1))
        {
            var f = Fields(line);
            if (f.Count < h.Count || !int.TryParse(f[count], out var n) || (proxy >= 0 && Same(f[proxy], "True"))) continue;
            var print = set >= 0 && cn >= 0 && f[set] != "" && f[cn] != "" ? $"{f[set].ToLower()}/{f[cn]}" : "";
            o.Add(new Entry(n, f[name], print, foil >= 0 && f[foil] != "", Boards.Main));
        }
        return o;
    }

    public static List<Want> ToWants(IEnumerable<Entry> es) =>
        es.GroupBy(e => (e.Board, e.Name.ToLowerInvariant()))
          .Select(g => new Want { Card = g.First().Name, Board = g.Key.Board, Qty = g.Sum(e => e.N), Print = g.Select(e => e.Print).FirstOrDefault(p => p != "") ?? "" }).ToList();

    static string Line(int n, string name, string print, bool foil = false)
    {
        var line = $"{n} {name}";
        if (print != "") { var s = print.Split('/', 2); line += $" ({s[0].ToUpper()}) {s[1]}"; }
        return foil ? line + " *F*" : line;
    }

    public static string FormatStacks(IEnumerable<Stack> stacks) =>
        string.Join("\r\n", stacks.GroupBy(s => (s.Card.ToLowerInvariant(), s.Print, s.Foil)).OrderBy(g => g.First().Card)
            .Select(g => Line(g.Sum(s => s.Qty), g.First().Card, g.Key.Print, g.Key.Foil)));

    public static string FormatCards(Dictionary<string, int> cards, Dictionary<string, string> prints) =>
        string.Join("\r\n", cards.Where(x => x.Value > 0).OrderBy(x => x.Key).Select(x => Line(x.Value, x.Key, prints.GetValueOrDefault(x.Key, ""))));

    // Arte de uma carta no deck: a da cópia que está nele; senão a arte exibida da carta.
    public static string DeckPrint(Data d, Location l, string card) =>
        d.Stacks.FirstOrDefault(s => Same(s.Loc, l.Name) && Same(s.Card, card) && s.Print != "")?.Print ?? d.Prints.GetValueOrDefault(card, "");

    public static string DesiredOf(Location l, string card) => l.Wants.FirstOrDefault(w => Same(w.Card, card) && w.Print != "")?.Print ?? "";

    // Troca uma cópia do deck pela arte desejada que está livre. Devolve de onde veio (null = não tinha livre).
    public static string? SwapToDesired(Data d, Location l, string card, Stack? old, string print)
    {
        var free = FreeStacks(d, card).FirstOrDefault(s => s.Print == print);
        if (free == null) return null;
        var from = Place(free.Loc, free.Sector);
        Pull(d, l, new[] { free }, card, print, free.Foil, 1);
        if (old != null && d.Stacks.Contains(old)) Move(d, old, 1, Default(d), "", keepWants: true);
        return from;
    }

    // A cópia que está no deck passa a ser a arte que vale: tira a arte desejada que não bate com
    // nenhuma cópia física (ex.: lista do Moxfield com arte errada, mas você usou a sua da coleção).
    public static int AcceptOwnedArt(Data d, Location l, string? card = null)
    {
        int n = 0;
        foreach (var w in l.Wants.Where(w => w.Print != "" && (card == null || Same(w.Card, card))))
        {
            bool inDeck = d.Stacks.Any(s => Same(s.Loc, l.Name) && Same(s.Card, w.Card));
            if (inDeck && !d.Stacks.Any(s => Same(s.Loc, l.Name) && Same(s.Card, w.Card) && s.Print == w.Print)) { w.Print = ""; n++; }
        }
        return n;
    }

    // Artes desejadas que você ainda não tem livre (pra comprar).
    public static Dictionary<string, int> ArtUpgrades(Data d, Location l)
    {
        var o = NewCards();
        foreach (var c in l.Wants.Where(w => w.Print != "" && w.Board != Boards.Maybe).Select(w => w.Card).Distinct(CI))
        {
            var want = DesiredOf(l, c);
            int wrong = d.Stacks.Where(s => Same(s.Loc, l.Name) && Same(s.Card, c) && s.Print != want).Sum(s => s.Qty);
            int free = FreeStacks(d, c).Where(s => s.Print == want).Sum(s => s.Qty);
            if (wrong - free > 0) o[c] = wrong - free;
        }
        return o;
    }

    public static string FormatDeck(Data d, Location l)
    {
        string sec(string b)
        {
            var cards = NewCards();
            foreach (var w in l.Wants.Where(w => w.Board == b)) cards[w.Card] = cards.GetValueOrDefault(w.Card) + w.Qty;
            return FormatCards(cards, cards.Keys.ToDictionary(c => c, c => DesiredOf(l, c) is { Length: > 0 } p ? p : DeckPrint(d, l, c), CI));
        }
        string cmd = sec(Boards.Cmd), main = sec(Boards.Main), side = sec(Boards.Side), maybe = sec(Boards.Maybe);
        if (side == "" && maybe == "") return cmd == "" ? main : main + "\r\n\r\n" + cmd;
        return string.Join("\r\n\r\n", new[] { ("COMMANDER:", cmd), ("MAIN:", main), ("SIDEBOARD:", side), ("CONSIDERING:", maybe) }
            .Where(x => x.Item2 != "").Select(x => x.Item1 + "\r\n" + x.Item2));
    }

    // ---------- self-test (MtgEstoque.exe --test) ----------
    public static bool SelfTest()
    {
        var p = Parse("1 Sol Ring (FIC) 357\n12 Forest (EOE) 276 *F*\n2x sol ring\n1 Bolt Bend (PLST) WAR-115\n\n1 Jodah, Archmage Eternal\n");
        bool parseOk = p.Count == 5 && p.Single(e => e.Name == "Forest").Foil && !p.Single(e => e.Name == "Bolt Bend").Foil
            && p.Single(e => e.Name == "Bolt Bend").Print == "plst/WAR-115"
            && p.Single(e => e.Board == Boards.Cmd).Name == "Jodah, Archmage Eternal"
            && ToWants(p).Single(w => w.Card == "Sol Ring").Qty == 3;

        var sb = Parse("1 Sol Ring\nSB: 1 Be'lakor, the Dark Master\n");
        var hdr = Parse("COMMANDER:\n1 Hulk\n\nMAIN:\n1 Sol Ring\n\nSIDEBOARD:\n1 Duress\n\nCONSIDERING:\n1 Opt");
        bool boardsOk = sb.Single(e => e.Board == Boards.Cmd).Name == "Be'lakor, the Dark Master"
            && hdr.Select(e => e.Board).SequenceEqual(new[] { Boards.Cmd, Boards.Main, Boards.Side, Boards.Maybe })
            && Parse("1 Sol Ring\n1 Bruce Banner", lastLineIsCmd: true).Single(e => e.Board == Boards.Cmd).Name == "Bruce Banner"
            && !Parse("1 Sol Ring\n1 Bruce Banner").Any(e => e.Board == Boards.Cmd);

        var deckLoc = new Location { Kind = "deck", Wants = ToWants(hdr) };
        bool fmtOk = Parse(FormatDeck(new Data(), deckLoc)).Select(e => (e.Name, e.Board)).SequenceEqual(hdr.Select(e => (e.Name, e.Board)));

        // migração v1: 2 Sol Ring, decks A e B pedem 1 cada, C pede 1 -> C fica faltando
        var d = new Data
        {
            Owned = new(CI) { ["Sol Ring"] = new() { ["fic/357"] = 1, [""] = 1 }, ["Opt"] = new() { [""] = 3 } },
            Decks = new() { ["A"] = new(CI) { ["Sol Ring"] = 1 }, ["B"] = new(CI) { ["Sol Ring"] = 1 }, ["C"] = new(CI) { ["Sol Ring"] = 1, ["Opt"] = 1 } },
            Commanders = new() { ["C"] = new() { "Opt" } },
        };
        Migrate(d);
        d.Locations.Add(new Location { Name = "Binder", Kind = "binder" });
        var A = Loc(d, "A"); var C = Loc(d, "C");
        bool migOk = d.Owned == null && Have(d, "Sol Ring") == 2 && InLoc(d, "Opt", "C") == 1 && InLoc(d, "Opt", DefaultName) == 2
            && Missing(d, C, "Sol Ring") == 1 && Missing(d, A, "Sol Ring") == 0 && C.Wants.Single(w => w.Card == "Opt").Board == Boards.Cmd;

        // buylist: C precisa de 1 Sol Ring, não há livre -> compra 1
        var buy = Buylist(d, out int covered);
        bool buyOk = buy["Sol Ring"] == 1 && covered == 0 && buy.Count == 1;

        // tirar Sol Ring da lista de A -> cópia vai pra Caixa global e cobre a falta de C
        SetWants(d, A, new());
        bool wantsOk = InLoc(d, "Sol Ring", "A") == 0 && InLoc(d, "Sol Ring", DefaultName) == 1 && Buylist(d, out covered).Count == 0 && covered == 1;

        // mover do global pro binder com setor, e de volta pra um deck (entra no Main)
        var g = d.Stacks.First(s => s.Card == "Opt" && s.Loc == DefaultName);
        Move(d, g, 1, "Binder", "Azul");
        bool moveOk = InLoc(d, "Opt", "Binder") == 1 && d.Stacks.Any(s => s.Loc == "Binder" && s.Sector == "Azul") && InLoc(d, "Opt", DefaultName) == 1;
        Move(d, d.Stacks.First(s => s.Loc == "Binder"), 1, "A", "x");
        moveOk &= InLoc(d, "Opt", "A") == 1 && Wanted(A, "Opt", true) == 1 && d.Stacks.All(s => s.Loc != "A" || s.Sector == "");

        // Fill: Opt livre no global (1) é puxado mantendo a arte dele; o 2º é criado com a arte da lista
        var D = new Location { Name = "D", Kind = "deck" };
        d.Locations.Add(D);
        var es = new List<Entry> { new(2, "Opt", "eld/59", true, Boards.Main), new(1, "Duress", "", false, Boards.Maybe) };
        D.Wants = ToWants(es);
        var (mv, cr) = Fill(d, D, es, pull: true, create: true);
        bool fillOk = mv == 1 && cr == 1 && InLoc(d, "Opt", "D") == 2 && InLoc(d, "Opt", DefaultName) == 0
            && d.Stacks.Count(s => s.Loc == "D" && s.Print == "" && !s.Foil) == 1 && d.Stacks.Count(s => s.Loc == "D" && s.Print == "eld/59" && s.Foil) == 1
            && InLoc(d, "Duress", "D") == 0;

        var csv = Parse("\"Count\",\"Tradelist Count\",\"Name\",\"Edition\",\"Condition\",\"Language\",\"Foil\",\"Tags\",\"Last Modified\",\"Collector Number\",\"Alter\",\"Proxy\",\"Purchase Price\"\r\n"
            + "\"2\",\"2\",\"Abigale, Poet Laureate // Heroic Stanza\",\"sos\",\"Near Mint\",\"English\",\"\",\"\",\"2026-05-24\",\"170\",\"False\",\"False\",\"\"\r\n"
            + "\"1\",\"1\",\"Sol Ring\",\"fic\",\"Near Mint\",\"English\",\"etched\",\"\",\"x\",\"357\",\"False\",\"False\",\"\"\r\n"
            + "\"1\",\"1\",\"Proxy Card\",\"fic\",\"Near Mint\",\"English\",\"\",\"\",\"x\",\"1\",\"False\",\"True\",\"\"\r\n");
        bool csvOk = csv.Count == 2 && csv[0].N == 2 && csv[0].Name == "Abigale, Poet Laureate // Heroic Stanza" && csv[0].Print == "sos/170" && !csv[0].Foil
            && csv[1].Print == "fic/357" && csv[1].Foil;

        // linhas do deck: 2 Islands de artes diferentes = 2 linhas; pede 3 -> 3ª linha é falta
        var E = new Location { Name = "E", Kind = "deck", Wants = new() { new Want { Card = "Island", Qty = 3 } } };
        d.Locations.Add(E);
        AddStack(d, "Island", "aaa/1", false, 1, "E", "");
        AddStack(d, "Island", "bbb/2", true, 1, "E", "");
        var lines = DeckLines(d, E);
        bool linesOk = lines.Count == 3 && lines.Count(x => x.S != null && x.Qty == 1) == 2 && lines.Single(x => x.S == null).Missing == 1
            && lines.Select(x => x.S?.Print).Distinct().Count() == 3;

        // mover pra fora do deck não deixa rastro; troca de arte entre decks mantém as listas iguais
        var F = new Location { Name = "F", Kind = "deck", Wants = new() { new Want { Card = "Signet", Qty = 1 } } };
        var G = new Location { Name = "G", Kind = "deck", Wants = new() { new Want { Card = "Signet", Qty = 1 } } };
        d.Locations.Add(F); d.Locations.Add(G);
        AddStack(d, "Signet", "m3c/283", true, 1, "F", "");
        AddStack(d, "Signet", "", false, 1, "G", "");
        Move(d, d.Stacks.First(s => s.Loc == "F"), 1, DefaultName, "");
        bool traceOk = Wanted(F, "Signet", false) == 0 && DeckLines(d, F).Count == 0;
        Move(d, d.Stacks.First(s => s.Loc == DefaultName && s.Card == "Signet"), 1, "F", ""); // volta: entra na lista de F
        var a = d.Stacks.First(s => s.Loc == "F"); var b = d.Stacks.First(s => s.Loc == "G");
        Move(d, a, 1, "G", "", keepWants: true); Move(d, b, 1, "F", "", keepWants: true);
        traceOk &= Wanted(F, "Signet", false) == 1 && Wanted(G, "Signet", false) == 1 && DeckLines(d, F).Count == 1 && DeckLines(d, G).Count == 1
            && d.Stacks.Single(s => s.Loc == "G").Foil;
        // remover / mudar de board
        RemoveFromDeck(d, G, "Signet", Boards.Main, 1, null);
        traceOk &= Wanted(G, "Signet", false) == 0 && InLoc(d, "Signet", "G") == 0 && InLoc(d, "Signet", DefaultName) >= 1;
        SetBoard(F, "Signet", Boards.Main, Boards.Side, 1);
        traceOk &= F.Wants.Single().Board == Boards.Side;

        // log: mover do binder/setor pro deck fica registrado com origem e destino
        var log = d.Log.LastOrDefault(x => x.Card == "Opt" && x.From == "Binder / Azul");
        bool logOk = log != null && log.To == "A" && log.Qty == 1;

        // setores: mover entre setores do mesmo binder registra no log; excluir setor devolve pra "Sem setor"
        var bnd = Loc(d, "Binder");
        bnd.Sectors.Add("Vazio");
        AddStack(d, "Duress", "", false, 2, "Binder", "");
        Move(d, d.Stacks.First(s => s.Loc == "Binder" && s.Card == "Duress"), 2, "Binder", "Preto");
        bool secOk = SectorsOf(d, bnd).SequenceEqual(new[] { "Preto", "Vazio" }) && d.Log.Last().From == "Binder" && d.Log.Last().To == "Binder / Preto";
        DeleteSector(d, bnd, "Preto");
        secOk &= d.Stacks.Single(s => s.Loc == "Binder" && s.Card == "Duress").Sector == "" && SectorsOf(d, bnd).SequenceEqual(new[] { "Vazio" });
        // desfazer: snapshot volta exatamente
        var snap = Snapshot(d);
        AddStack(d, "Duress", "", false, 5, "Binder", "");
        secOk &= FromJson(snap).Stacks.Single(s => s.Loc == "Binder" && s.Card == "Duress").Qty == 2;

        // arte desejada: lista guarda a arte; com livre troca na hora (antiga vai pro padrão); sem livre vira "troca de arte"
        var H = new Location { Name = "H", Kind = "deck", Wants = ToWants(Parse("1 Lotus (aaa) 1")) };
        d.Locations.Add(H);
        AddStack(d, "Lotus", "zzz/9", false, 1, "H", "");
        bool artOk = DesiredOf(H, "lotus") == "aaa/1" && ArtUpgrades(d, H)["Lotus"] == 1 && FormatDeck(d, H) == "1 Lotus (AAA) 1";
        AddStack(d, "Lotus", "aaa/1", false, 1, "Binder", "");
        artOk &= ArtUpgrades(d, H).Count == 0;
        var from = SwapToDesired(d, H, "Lotus", d.Stacks.First(s => s.Loc == "H"), "aaa/1");
        artOk &= from == "Binder" && d.Stacks.Single(s => s.Loc == "H").Print == "aaa/1" && InLoc(d, "Lotus", DefaultName) == 1 && Wanted(H, "Lotus", false) == 1;

        // usou a cópia da coleção (outra arte): a arte da lista deixa de ser "desejada"
        var J = new Location { Name = "J", Kind = "deck", Wants = ToWants(Parse("1 Mox (aaa) 1\n1 Opal (bbb) 2")) };
        d.Locations.Add(J);
        AddStack(d, "Mox", "zzz/9", false, 1, "J", "");
        bool ownOk = AcceptOwnedArt(d, J) == 1 && DesiredOf(J, "Mox") == "" && DesiredOf(J, "Opal") == "bbb/2"; // Opal falta: continua querendo
        // Opal livre no binder: aparece como "dá pra puxar" e o botão traz pro deck
        AddStack(d, "Opal", "bbb/2", false, 2, DefaultName, "");
        ownOk &= Pullable(d, J) == 1 && PullFree(d, J) == 1 && InLoc(d, "Opal", "J") == 1 && Pullable(d, J) == 0;

        return parseOk && boardsOk && fmtOk && migOk && buyOk && wantsOk && moveOk && fillOk && csvOk && linesOk && traceOk && logOk && secOk && artOk && ownOk && Types.LigaTest() && Liga.SelfTest();
    }
}
