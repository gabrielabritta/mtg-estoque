using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MtgEstoque;

// Importa deck público do Moxfield pela API que o próprio site usa (não oficial, pode mudar).
public static class Moxfield
{
    const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";
    static readonly HttpClient Http = new() { DefaultRequestHeaders = { { "User-Agent", UA }, { "Accept", "application/json" } } };

    static readonly Dictionary<string, string> BoardMap = new()
    {
        ["commanders"] = Boards.Cmd, ["mainboard"] = Boards.Main, ["companions"] = Boards.Side,
        ["sideboard"] = Boards.Side, ["maybeboard"] = Boards.Maybe,
    };

    public static string? IdOf(string url) => Regex.Match(url, @"moxfield\.com/decks/([A-Za-z0-9_-]+)") is { Success: true } m ? m.Groups[1].Value : null;

    public static async Task<(string name, List<Entry> entries)> Load(string url)
    {
        var id = IdOf(url) ?? throw new ArgumentException("Link do Moxfield inválido");
        var api = $"https://api2.moxfield.com/v3/decks/all/{id}";
        string json;
        try { json = await Http.GetStringAsync(api); }
        catch (HttpRequestException) { json = await Curl(api); } // Cloudflare às vezes barra o HttpClient
        return Parse(json);
    }

    static async Task<string> Curl(string url)
    {
        var psi = new ProcessStartInfo("curl.exe", $"-s -f -A \"{UA}\" -H \"Accept: application/json\" \"{url}\"")
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
        using var p = Process.Start(psi)!;
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new HttpRequestException("Moxfield recusou o acesso (deck privado?)");
        return output;
    }

    public static (string name, List<Entry> entries) Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var o = new List<Entry>();
        foreach (var board in root.GetProperty("boards").EnumerateObject())
        {
            if (!BoardMap.TryGetValue(board.Name, out var b)) continue;
            foreach (var c in board.Value.GetProperty("cards").EnumerateObject().Select(x => x.Value))
            {
                var card = c.GetProperty("card");
                var name = card.GetProperty("name").GetString()!;
                var print = $"{card.GetProperty("set").GetString()!.ToLower()}/{card.GetProperty("cn").GetString()}";
                if (card.TryGetProperty("type_line", out var tl)) Types.Learn(name, tl.GetString()!);
                o.Add(new Entry(c.GetProperty("quantity").GetInt32(), name, print, c.GetProperty("isFoil").GetBoolean(), b));
            }
        }
        Types.Save();
        return (root.GetProperty("name").GetString()!, o);
    }
}
