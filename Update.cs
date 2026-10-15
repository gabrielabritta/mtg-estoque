using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace MtgEstoque;

// Atualização: ao abrir, pergunta à API pública do GitHub qual é a última release (grátis, sem servidor).
// Versão nova → oferece baixar o Setup e rodar por cima (os dados ficam onde estão).
public static class Update
{
    const string Api = "https://api.github.com/repos/gabrielabritta/mtg-estoque/releases/latest";

    public static Version Current => typeof(Update).Assembly.GetName().Version ?? new Version(0, 0);

    public static async Task Check(Window owner)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MtgEstoque");
            using var j = JsonDocument.Parse(await http.GetStringAsync(Api));
            var tag = j.RootElement.GetProperty("tag_name").GetString()!.TrimStart('v');
            if (!Version.TryParse(tag, out var latest) || latest <= Current) return;
            var asset = j.RootElement.GetProperty("assets").EnumerateArray()
                .Select(a => a.GetProperty("browser_download_url").GetString()!).FirstOrDefault(u => u.EndsWith(".exe"));
            if (asset == null) return;
            if (!ConfirmDialog.Ask(owner, "Atualização", $"Versão {tag} disponível (você tem {Current.ToString(2)}).\nBaixar e instalar agora? O app fecha e reabre depois.", "Atualizar")) return;
            var file = Path.Combine(Path.GetTempPath(), Path.GetFileName(asset));
            await File.WriteAllBytesAsync(file, await http.GetByteArrayAsync(asset));
            Process.Start(new ProcessStartInfo(file, "/SILENT") { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch { } // sem internet / GitHub fora: segue a vida
    }
}
