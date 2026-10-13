using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace MtgEstoque;

// Imagens do Scryfall com cache em disco: %APPDATA%\MtgEstoque\img. Cada arquivo é baixado uma vez só.
public static class Img
{
    public record Art(string Print, string Small);
    record ArtCache(List<Art> Arts, bool Dfc);

    static readonly string Dir = System.IO.Path.Combine(Store.Dir, "img");
    static readonly HttpClient Http = new() { DefaultRequestHeaders = { { "User-Agent", "MtgEstoque/1.0" }, { "Accept", "*/*" } } };
    static readonly SemaphoreSlim ApiGate = new(1); // API pede ~100ms entre requests
    static readonly SemaphoreSlim CdnGate = new(8); // CDN de imagens não tem esse limite

    static string FileFor(string key, string ext) =>
        System.IO.Path.Combine(Dir, string.Concat(key.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ext);

    static async Task<byte[]> Fetch(string url)
    {
        await ApiGate.WaitAsync();
        try { return await Http.GetByteArrayAsync(url); }
        finally { await Task.Delay(100); ApiGate.Release(); }
    }

    static async Task<string> Cached(string file, Func<Task<byte[]>> download)
    {
        if (File.Exists(file)) return file;
        var bytes = await download();
        Directory.CreateDirectory(Dir);
        await File.WriteAllBytesAsync(file, bytes);
        return file;
    }

    // ---------- links das imagens (CDN) ----------
    // A API do Scryfall tem limite (~10/s), a CDN das imagens (cards.scryfall.io) não. Então: descobre os links
    // de até 75 cartas por request (/cards/collection), guarda em uris.json, e baixa as imagens direto da CDN.
    public record Uris(string Normal, string Small, string Crop, string? Back);
    static readonly string UrisFile = System.IO.Path.Combine(Store.Dir, "uris.json");
    static readonly Dictionary<string, Uris> UriMap = LoadUris();

    static Dictionary<string, Uris> LoadUris()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, Uris>>(File.ReadAllText(UrisFile)) ?? new(); }
        catch { return new(); }
    }

    static string Key(string name, string? print) => string.IsNullOrEmpty(print) ? "n:" + name.ToLowerInvariant() : print;

    static Uris? UrisOf(JsonElement c)
    {
        static Uris? of(JsonElement iu, string? back) => iu.TryGetProperty("normal", out var n)
            ? new Uris(n.GetString()!, iu.GetProperty("small").GetString()!, iu.GetProperty("art_crop").GetString()!, back) : null;
        if (c.TryGetProperty("image_uris", out var top)) return of(top, null);
        if (!c.TryGetProperty("card_faces", out var f) || !f[0].TryGetProperty("image_uris", out var front)) return null;
        var back = f.GetArrayLength() > 1 && f[1].TryGetProperty("image_uris", out var b) ? b.GetProperty("normal").GetString() : null;
        return of(front, back);
    }

    public static async Task Resolve(IEnumerable<(string name, string? print)> cards)
    {
        var need = cards.Where(c => !UriMap.ContainsKey(Key(c.name, c.print))).DistinctBy(c => Key(c.name, c.print)).ToList();
        if (need.Count == 0) return;
        foreach (var chunk in need.Chunk(75))
        {
            var ids = chunk.Select(c => string.IsNullOrEmpty(c.print) ? (object)new { name = c.name }
                : new { set = c.print.Split('/', 2)[0], collector_number = c.print.Split('/', 2)[1] });
            using var doc = JsonDocument.Parse(await PostJson("https://api.scryfall.com/cards/collection", new { identifiers = ids }));
            foreach (var card in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                if (UrisOf(card) is not { } u) continue;
                UriMap[$"{card.GetProperty("set").GetString()}/{card.GetProperty("collector_number").GetString()}"] = u;
                var name = card.GetProperty("name").GetString()!;
                foreach (var c in chunk.Where(c => string.IsNullOrEmpty(c.print) && (Store.Same(name, c.name) || name.StartsWith(c.name + " // ", StringComparison.OrdinalIgnoreCase))))
                    UriMap[Key(c.name, null)] = u;
            }
        }
        Directory.CreateDirectory(Store.Dir);
        await File.WriteAllTextAsync(UrisFile, JsonSerializer.Serialize(UriMap));
    }

    // null = ainda não sabe (links não resolvidos)
    public static bool? IsDfc(string name, string? print) => UriMap.TryGetValue(Key(name, print), out var u) ? u.Back != null : null;

    static async Task<byte[]> Cdn(string url)
    {
        await CdnGate.WaitAsync();
        try { return await Http.GetByteArrayAsync(url); }
        finally { CdnGate.Release(); }
    }

    // print = "eoe/251" ou null (arte padrão); back = verso de carta dupla-face; version: normal | art_crop.
    public static async Task<string> Get(string name, string? print, bool back = false, string version = "normal")
    {
        var file = FileFor((print ?? name) + (back ? "-back" : "") + (version == "normal" ? "" : "-" + version), ".jpg");
        if (File.Exists(file)) return file;
        var key = Key(name, print);
        if (!UriMap.ContainsKey(key)) await Resolve(new[] { (name, print) });
        if (!UriMap.TryGetValue(key, out var u)) throw new InvalidOperationException("carta não encontrada no Scryfall");
        var url = version == "art_crop" ? u.Crop : back ? u.Back ?? throw new InvalidOperationException("sem verso") : u.Normal;
        return await Cached(file, () => Cdn(url));
    }

    // Baixa em segundo plano as imagens de um deck (links em 1-2 requests, imagens em paralelo pela CDN).
    public static async Task Prefetch(List<(string name, string? print)> cards)
    {
        try
        {
            await Resolve(cards);
            await Task.WhenAll(cards.Select(async c => { try { await Get(c.name, c.print); } catch { } }));
        }
        catch { }
    }

    public static async Task<byte[]> PostJson(string url, object body)
    {
        await ApiGate.WaitAsync();
        try
        {
            using var resp = await Http.PostAsync(url, new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"));
            return await resp.Content.ReadAsByteArrayAsync();
        }
        finally { await Task.Delay(100); ApiGate.Release(); }
    }

    public static Task<string> Thumb(Art a) =>
        Cached(FileFor(a.Print + "-s", ".jpg"), async () =>
        {
            if (a.Small.Contains("api.scryfall.com")) return await Fetch(a.Small); // API tem limite; CDN não
            await CdnGate.WaitAsync();
            try { return await Http.GetByteArrayAsync(a.Small); }
            finally { CdnGate.Release(); }
        });

    // Nomes de carta que começam/contêm o texto (busca enquanto digita).
    public static async Task<List<string>> Autocomplete(string q)
    {
        using var doc = JsonDocument.Parse(await Fetch($"https://api.scryfall.com/cards/autocomplete?q={Uri.EscapeDataString(q)}"));
        return doc.RootElement.GetProperty("data").EnumerateArray().Select(x => x.GetString()!).ToList();
    }

    public static Art SmallOf(string print) =>
        new(print, UriMap.TryGetValue(print, out var u) ? u.Small : $"https://api.scryfall.com/cards/{print}?format=image&version=small");

    // Impressões da carta, uma página do Scryfall (175) por vez; More() busca a próxima.
    // Quando chega na última, grava o cache e as próximas aberturas são instantâneas.
    public class ArtFeed
    {
        public List<Art> Arts = new();
        public string? Next;
        public bool Dfc;
        public int Total;
        internal string File = "";

        public async Task More()
        {
            if (Next == null) return;
            using var doc = JsonDocument.Parse(await Fetch(Next));
            var root = doc.RootElement;
            Total = root.GetProperty("total_cards").GetInt32();
            foreach (var card in root.GetProperty("data").EnumerateArray())
            {
                // dupla-face tem imagem por face (card_faces[].image_uris); split/adventure têm no topo
                bool top = card.TryGetProperty("image_uris", out var iu);
                if (Arts.Count == 0) Dfc = !top && card.TryGetProperty("card_faces", out _);
                if (!top && !(card.TryGetProperty("card_faces", out var f) && f[0].TryGetProperty("image_uris", out iu))) continue;
                Arts.Add(new Art($"{card.GetProperty("set").GetString()}/{card.GetProperty("collector_number").GetString()}", iu.GetProperty("small").GetString()!));
            }
            Next = root.GetProperty("has_more").GetBoolean() ? root.GetProperty("next_page").GetString() : null;
            if (Next == null)
            {
                Directory.CreateDirectory(Dir);
                await System.IO.File.WriteAllBytesAsync(File, JsonSerializer.SerializeToUtf8Bytes(new ArtCache(Arts, Dfc)));
            }
        }
    }

    // ponytail: cache não expira; apague a pasta img se quiser artes de sets novos
    public static async Task<ArtFeed> Feed(string name)
    {
        var file = FileFor("arts3-" + name, ".json");
        if (File.Exists(file))
        {
            var c = JsonSerializer.Deserialize<ArtCache>(await File.ReadAllBytesAsync(file))!;
            return new ArtFeed { Arts = c.Arts, Dfc = c.Dfc, Total = c.Arts.Count, File = file };
        }
        var feed = new ArtFeed { File = file, Next = $"https://api.scryfall.com/cards/search?q={Uri.EscapeDataString($"!\"{name}\"")}&unique=prints&order=released&dir=desc" };
        await feed.More();
        return feed;
    }

    public static BitmapImage Load(string path, int width = 0)
    {
        var b = new BitmapImage();
        b.BeginInit();
        b.CacheOption = BitmapCacheOption.OnLoad;
        if (width > 0) b.DecodePixelWidth = width;
        b.UriSource = new Uri(path);
        b.EndInit();
        b.Freeze();
        return b;
    }
}
