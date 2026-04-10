using System.Runtime.InteropServices;
using CliWrap;
using CliWrap.EventStream;

namespace PortableObsidian.Services;

public class TunnelService
{
    private readonly string _cloudflaredPath;
    private readonly string _platform;
    private static string _currentExternalUrl = "Initializing...";
    private static CancellationTokenSource? _cts;

    public static string GetExternalUrl() => _currentExternalUrl;

    public TunnelService()
    {
        _platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : "linux";
        var extension = _platform == "windows" ? ".exe" : "";
        _cloudflaredPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"cloudflared{extension}");
    }

    public async Task StartAsync(int localPort, string token = "")
    {
        try 
        {
            await EnsureBinaryExistsAsync();
            _cts = new CancellationTokenSource();

            Command cmd;
            if (!string.IsNullOrEmpty(token))
            {
                // 고정 주소 모드 (Token 사용)
                Console.WriteLine("[Tunnel] Starting with fixed token...");
                _currentExternalUrl = "Fixed (See Cloudflare Dashboard)";
                cmd = Cli.Wrap(_cloudflaredPath)
                    .WithArguments(new[] { "tunnel", "--no-autoupdate", "run", "--token", token });
            }
            else
            {
                // 랜덤 주소 모드
                Console.WriteLine("[Tunnel] Generating temporary external URL...");
                cmd = Cli.Wrap(_cloudflaredPath)
                    .WithArguments(new[] { "tunnel", "--url", $"http://localhost:{localPort}", "--no-autoupdate" });
            }

            await foreach (var cmdEvent in cmd.ListenAsync(_cts.Token))
            {
                if (cmdEvent is StandardErrorCommandEvent stdErr)
                {
                    var text = stdErr.Text;
                    if (text.Contains(".trycloudflare.com"))
                    {
                        var url = ExtractUrl(text);
                        if (!string.IsNullOrEmpty(url))
                        {
                            _currentExternalUrl = url;
                            PrintReadyMessage(localPort, url);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { /* 정상 종료 */ }
        catch (Exception ex)
        {
            Console.WriteLine($"[Tunnel Error] {ex.Message}");
        }
    }

    public static void Stop()
    {
        _cts?.Cancel();
    }

    private void PrintReadyMessage(int localPort, string url)
    {
        Console.WriteLine("\n" + new string('=', 65));
        Console.WriteLine("Obsidian Vault Bridge is ready!");
        Console.WriteLine("");
        Console.WriteLine($" Internal URL: http://localhost:{localPort}");
        Console.WriteLine($" External URL: {url}");
        Console.WriteLine("");
        Console.WriteLine(" Press Ctrl+C to exit.");
        Console.WriteLine(new string('=', 65) + "\n");
    }

    private string? ExtractUrl(string text)
    {
        var start = text.IndexOf("https://");
        if (start == -1) return null;
        var end = text.IndexOf(".trycloudflare.com", start);
        if (end == -1) return null;
        return text.Substring(start, (end + ".trycloudflare.com".Length) - start);
    }

    private async Task EnsureBinaryExistsAsync()
    {
        if (File.Exists(_cloudflaredPath)) return;
        Console.WriteLine("[Tunnel] Downloading engine...");
        string downloadUrl = _platform == "windows" 
            ? "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe"
            : "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-amd64";
        using var client = new HttpClient();
        var data = await client.GetByteArrayAsync(downloadUrl);
        await File.WriteAllBytesAsync(_cloudflaredPath, data);
        if (_platform == "linux") await Cli.Wrap("chmod").WithArguments("+x " + _cloudflaredPath).ExecuteAsync();
    }
}
