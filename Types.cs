using System.IO;
using System.Text.Json;

namespace MtgEstoque;

// Tipo de cada carta (type_line do Scryfall), cacheado em %APPDATA%\MtgEstoque\types.json.
public static class Types
{
    static readonly string FilePath = System.IO.Path.Combine(Store.Dir, "types.json");
    static readonly Dictionary<string, string> Map = Load();
    static bool busy;

    public static readonly string[] Order =
        { "Comandante", "Batalha", "Planeswalker", "Criatura", "Instantâneo", "Feitiço", "Encantamento", "Artefato", "Terreno", "Outros", "Sideboard", "Considerando" };

    // ---------- nomes no formato da LigaMagic ----------
    // Dupla-face (transform, MDFC, flip, meld): só a frente ("Bruce Banner").
    // Duas metades na mesma face (split, adventure, rooms): nome inteiro com " // ".
    static readonly string LayoutPath = System.IO.Path.Combine(Store.Dir, "layouts.json");
    static readonly Dictionary<string, string> Layouts = LoadLayouts();
    static readonly HashSet<string> FrontOnly = new() { "transform", "modal_dfc", "flip", "meld", "reversible_card" };

    static Dictionary<string, string> LoadLayouts()
    {
        try { return new(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(LayoutPath))!, Store.CI); }
        catch { return new(Store.CI); }
    }

    public static void LearnLayout(string card, string layout) => Layouts[card] = layout;

    public static string LigaName(string card) =>
        card.Contains(" // ") && Layouts.TryGetValue(card, out var l) && FrontOnly.Contains(l) ? card.Split(" // ")[0] : card;

    // Só cartas com " // " precisam do layout; busca as que faltam (75 por request) e guarda.
    public static async Task EnsureLayouts(IEnumerable<string> cards)
    {
        var missing = cards.Where(c => c.Contains(" // ") && !Layouts.ContainsKey(c)).Distinct(Store.CI).ToList();
        if (missing.Count == 0) return;
        foreach (var chunk in missing.Chunk(75))
        {
            var body = new { identifiers = chunk.Select(n => new { name = n.Split(" // ")[0] }) }; // o Scryfall só acha cartas de duas partes pelo nome da frente
            using var doc = JsonDocument.Parse(await Img.PostJson("https://api.scryfall.com/cards/collection", body));
            foreach (var c in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                var name = c.GetProperty("name").GetString()!;
                var layout = c.GetProperty("layout").GetString()!;
                foreach (var r in chunk.Where(r => Store.Same(name, r) || name.StartsWith(r.Split(" // ")[0] + " // ", StringComparison.OrdinalIgnoreCase)))
                    Layouts[r] = layout;
            }
            foreach (var r in chunk) Layouts.TryAdd(r, ""); // não achou: mantém o nome inteiro
        }
        Directory.CreateDirectory(Store.Dir);
        await File.WriteAllTextAsync(LayoutPath, JsonSerializer.Serialize(Layouts));
    }

    public static bool LigaTest()
    {
        LearnLayout("Bruce Banner // The Incredible Hulk", "transform");
        LearnLayout("Unholy Annex // Ritual Chamber", "split");
        LearnLayout("Bonecrusher Giant // Stomp", "adventure");
        return LigaName("Bruce Banner // The Incredible Hulk") == "Bruce Banner"
            && LigaName("Unholy Annex // Ritual Chamber") == "Unholy Annex // Ritual Chamber"
            && LigaName("Bonecrusher Giant // Stomp") == "Bonecrusher Giant // Stomp"
            && LigaName("Sol Ring") == "Sol Ring";
    }

    static Dictionary<string, string> Load()
    {
        try
        {
            var m = new Dictionary<string, string>(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath))!, Store.CI);
            // versões antigas não achavam cartas de duas partes: tenta de novo
            foreach (var k in m.Where(x => x.Value == "" && x.Key.Contains(" // ")).Select(x => x.Key).ToList()) m.Remove(k);
            return m;
        }
        catch { return new(Store.CI); }
    }

    // Criatura ganha de Artefato/Terreno (artifact creature, Dryad Arbor), como no Moxfield.
    public static string Group(string card)
    {
        if (!Map.TryGetValue(card, out var t) || t == "") return "Outros";
        var f = t.Split(" // ")[0];
        foreach (var (en, pt) in new[] { ("Creature", "Criatura"), ("Planeswalker", "Planeswalker"), ("Battle", "Batalha"), ("Instant", "Instantâneo"),
                                         ("Sorcery", "Feitiço"), ("Artifact", "Artefato"), ("Enchantment", "Encantamento"), ("Land", "Terreno") })
            if (f.Contains(en)) return pt;
        return "Outros";
    }

    // Busca em lote (75 por request) as que ainda não conhece. true = aprendeu algo novo.
    public static async Task<bool> Fetch(IEnumerable<string> cards)
    {
        var missing = cards.Where(c => !Map.ContainsKey(c)).Distinct(Store.CI).ToList();
        if (missing.Count == 0 || busy) return false;
        busy = true;
        try
        {
            foreach (var chunk in missing.Chunk(75))
            {
                var body = new { identifiers = chunk.Select(n => new { name = n.Split(" // ")[0] }) }; // o Scryfall só acha cartas de duas partes pelo nome da frente
                using var doc = JsonDocument.Parse(await Img.PostJson("https://api.scryfall.com/cards/collection", body));
                foreach (var c in doc.RootElement.GetProperty("data").EnumerateArray())
                {
                    var name = c.GetProperty("name").GetString()!;
                    var type = c.TryGetProperty("type_line", out var tl) ? tl.GetString()! : c.GetProperty("card_faces")[0].GetProperty("type_line").GetString()!;
                    foreach (var r in chunk.Where(r => Store.Same(name, r) || name.StartsWith(r.Split(" // ")[0] + " // ", StringComparison.OrdinalIgnoreCase)))
                        Map[r] = type;
                }
                foreach (var r in chunk) Map.TryAdd(r, ""); // não achou: não pergunta de novo
            }
            Save();
            return true;
        }
        finally { busy = false; }
    }

    public static void Learn(string card, string typeLine) => Map[card] = typeLine;

    public static void Save()
    {
        Directory.CreateDirectory(Store.Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Map));
    }
}
