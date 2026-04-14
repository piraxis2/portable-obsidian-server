using Microsoft.AspNetCore.SignalR;
using PortableObsidian.Components;
using PortableObsidian.Hubs;
using PortableObsidian.Services;
using PortableObsidian.Config;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppDomain.CurrentDomain.BaseDirectory // 실행 파일 경로를 루트로 강제 설정
});

// 1. 설정 및 서비스 등록
var config = AppConfig.Load();
var pathService = new VaultPathService();

// 2. 초기 경로 설정
string initialPath = args.Length > 0 ? args[0] : config.VaultPath;
if (initialPath == "." || string.IsNullOrWhiteSpace(initialPath))
{
    initialPath = AppDomain.CurrentDomain.BaseDirectory;
}
pathService.SetPath(initialPath);

builder.Services.AddSingleton(pathService);
builder.Configuration["IS_READ_ONLY_INIT"] = config.IsReadOnly.ToString();

const int localPort = 30331;
builder.WebHost.UseUrls($"http://0.0.0.0:{localPort}");

builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();
builder.Services.AddSignalR();
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

// 3. 정적 파일 설정 보강
app.UseStaticFiles(); // 기본 wwwroot (실행 파일 옆의 폴더)

// 옵시디언 볼트 내의 파일들을 /vault 경로로 서빙
if (Directory.Exists(pathService.CurrentPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(pathService.CurrentPath),
        RequestPath = "/vault"
    });
}

app.UseAntiforgery();
app.UseCors();

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(PortableObsidian.Client._Imports).Assembly);

app.MapHub<VaultHub>("/vaulthub");

// VaultHub의 정적 컨텍스트 초기화 (파일 감시용)
try 
{
    using (var scope = app.Services.CreateScope())
    {
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<VaultHub>>();
        VaultHub.InitializeStaticContext(hubContext, pathService.CurrentPath);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[Warn] Failed to initialize file watcher: {ex.Message}");
}

Console.WriteLine($"\n[Server] Obsidian Bridge is running...");
Console.WriteLine($"[Server] Port: {localPort}");
Console.WriteLine($"[Server] Vault: {pathService.CurrentPath}");

// 백그라운드에서 터널 시작
_ = Task.Run(async () => {
    await Task.Delay(2000);
    var tunnel = new TunnelService();
    await tunnel.StartAsync(localPort, config.TunnelToken);
});

AppDomain.CurrentDomain.ProcessExit += (s, e) => TunnelService.Stop();

// 콘솔 명령어 입력 루프
_ = Task.Run(async () => {
    await Task.Delay(3000);
    using var scope = app.Services.CreateScope();
    var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<VaultHub>>();
    
    while (true)
    {
        var line = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(line)) continue;

        var parts = line.Split(' ', 2);
        var cmd = parts[0].ToLower();
        var arg = parts.Length > 1 ? parts[1].Trim('"') : "";

        if (cmd == "path" && !string.IsNullOrEmpty(arg))
        {
            if (Directory.Exists(arg))
            {
                pathService.SetPath(arg);
                config.VaultPath = pathService.CurrentPath;
                AppConfig.Save(config);
                VaultHub.UpdateWatcherPath(pathService.CurrentPath);
                await hubContext.Clients.All.SendAsync("VaultPathChanged");
                Console.WriteLine($"[System] Vault path changed: {pathService.CurrentPath}");
            }
            else Console.WriteLine($"[Error] Path '{arg}' not found.");
        }
        else if (cmd == "ro")
        {
            VaultHub.SetReadOnly(true);
            await hubContext.Clients.All.SendAsync("ModeChanged", true);
            Console.WriteLine("[System] Mode: ReadOnly");
        }
        else if (cmd == "rw")
        {
            VaultHub.SetReadOnly(false);
            await hubContext.Clients.All.SendAsync("ModeChanged", false);
            Console.WriteLine("[System] Mode: ReadWrite");
        }
        else if (cmd == "url")
        {
            Console.WriteLine($"\n Internal URL: http://localhost:{localPort}");
            Console.WriteLine($" External URL: {TunnelService.GetExternalUrl()}\n");
        }
        else if (cmd == "help")
        {
            Console.WriteLine("\n--- Available Commands ---");
            Console.WriteLine("path [path] : Change Obsidian vault path");
            Console.WriteLine("ro          : Switch to ReadOnly mode");
            Console.WriteLine("rw          : Switch to ReadWrite mode");
            Console.WriteLine("url         : Show current URLs");
            Console.WriteLine("--------------------------\n");
        }
    }
});

app.Run();
