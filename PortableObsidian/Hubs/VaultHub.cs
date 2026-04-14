using Microsoft.AspNetCore.SignalR;
using PortableObsidian.Client.Models;
using PortableObsidian.Services;

namespace PortableObsidian.Hubs;

public class VaultHub : Hub
{
    private readonly VaultPathService _pathService;
    private static bool _isReadOnly = false;
    private static FileSystemWatcher? _watcher;
    private static IHubContext<VaultHub>? _staticContext;

    public VaultHub(VaultPathService pathService, IConfiguration config)
    {
        _pathService = pathService;

        if (config.GetValue<bool>("IS_READ_ONLY_INIT")) {
            _isReadOnly = true;
        }
    }

    // Program.cs에서 서버 시작 시 컨텍스트를 설정해줌
    public static void InitializeStaticContext(IHubContext<VaultHub> hubContext, string path)
    {
        _staticContext = hubContext;
        if (_watcher == null)
        {
            _watcher = new FileSystemWatcher(path, "*.md")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnFileChanged;
            _watcher.Created += (s, e) => _staticContext?.Clients.All.SendAsync("VaultChanged");
            _watcher.Deleted += (s, e) => _staticContext?.Clients.All.SendAsync("VaultChanged");
            _watcher.Renamed += (s, e) => _staticContext?.Clients.All.SendAsync("VaultChanged");
        }
    }

    public static void UpdateWatcherPath(string newPath)
    {
        if (_watcher != null) _watcher.Path = newPath;
    }

    private static async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            await Task.Delay(500);
            var content = await File.ReadAllTextAsync(e.FullPath);
            // static 필드에서 직접 경로를 가져오기 어려우므로 클라이언트가 경로 체크를 하도록 함
            await _staticContext!.Clients.All.SendAsync("FileUpdated", e.Name?.Replace("\\", "/"), content);
        }
        catch { }
    }

    private string VaultRoot => _pathService.CurrentPath;
    public static void SetReadOnly(bool readOnly) => _isReadOnly = readOnly;
    public bool GetIsReadOnly() => _isReadOnly;

    public override async Task OnConnectedAsync()
    {
        var ip = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        Console.WriteLine($"[Access] Connected: {ip} (ID: {Context.ConnectionId})");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var ip = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        Console.WriteLine($"[Access] Disconnected: {ip} (ID: {Context.ConnectionId})");
        await base.OnDisconnectedAsync(exception);
    }

    public Task<VaultItem> GetVaultTree()
    {
        return Task.FromResult(BuildTree(new DirectoryInfo(VaultRoot), ""));
    }

    public async Task<string> ReadFile(string relativePath)
    {
        var ip = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        Console.WriteLine($"[View] {ip} is reading: {relativePath}");
        var fullPath = Path.Combine(VaultRoot, relativePath);
        if (!File.Exists(fullPath)) return "File not found.";
        return await File.ReadAllTextAsync(fullPath);
    }

    public async Task SaveFile(string relativePath, string content)
    {
        if (_isReadOnly) throw new HubException("ReadOnly mode.");
        var fullPath = Path.Combine(VaultRoot, relativePath);
        if (!Path.GetFullPath(fullPath).StartsWith(Path.GetFullPath(VaultRoot))) throw new HubException("Access denied.");
        if (_watcher != null) _watcher.EnableRaisingEvents = false;
        await File.WriteAllTextAsync(fullPath, content);
        if (_watcher != null) _watcher.EnableRaisingEvents = true;
        await Clients.Others.SendAsync("FileUpdated", relativePath, content);
    }

    public async Task CreateFile(string relativePath)
    {
        if (_isReadOnly) throw new HubException("ReadOnly mode.");
        var fullPath = Path.Combine(VaultRoot, relativePath);
        if (!fullPath.EndsWith(".md")) fullPath += ".md";
        await File.WriteAllTextAsync(fullPath, "");
        await Clients.All.SendAsync("VaultChanged");
    }

    public async Task CreateDirectory(string relativePath)
    {
        if (_isReadOnly) throw new HubException("ReadOnly mode.");
        Directory.CreateDirectory(Path.Combine(VaultRoot, relativePath));
        await Clients.All.SendAsync("VaultChanged");
    }

    public async Task RenameFile(string oldRelativePath, string newName)
    {
        if (_isReadOnly) throw new HubException("ReadOnly mode.");
        var oldFullPath = Path.Combine(VaultRoot, oldRelativePath);
        if (!newName.ToLower().EndsWith(".md")) newName += ".md";
        var newFullPath = Path.Combine(Path.GetDirectoryName(oldFullPath)!, newName);
        File.Move(oldFullPath, newFullPath);
        var newRelativePath = Path.GetRelativePath(VaultRoot, newFullPath).Replace("\\", "/");
        await Clients.All.SendAsync("VaultChanged");
        await Clients.All.SendAsync("FileRenamed", oldRelativePath, newRelativePath, newName);
    }

    public async Task MoveFile(string fileRelativePath, string targetDirectoryRelativePath)
    {
        if (_isReadOnly) throw new HubException("ReadOnly mode.");
        var oldFullPath = Path.Combine(VaultRoot, fileRelativePath);
        var newFullPath = Path.Combine(VaultRoot, targetDirectoryRelativePath, Path.GetFileName(oldFullPath));
        File.Move(oldFullPath, newFullPath);
        await Clients.All.SendAsync("VaultChanged");
    }

    public async Task<List<VaultItem>> SearchVault(string query)
    {
        var results = new List<VaultItem>();
        var root = new DirectoryInfo(VaultRoot);
        foreach (var file in root.GetFiles("*.md", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(VaultRoot, file.FullName);
            if (file.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || (await File.ReadAllTextAsync(file.FullName)).Contains(query, StringComparison.OrdinalIgnoreCase))
                results.Add(new VaultItem { Name = file.Name, RelativePath = rel.Replace("\\", "/"), IsDirectory = false });
        }
        return results;
    }

    public async Task<string> GetImageBase64(string fileName)
    {
        var root = new DirectoryInfo(VaultRoot);
        var file = root.GetFiles("*", SearchOption.AllDirectories).FirstOrDefault(f => f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (file != null)
        {
            var bytes = await File.ReadAllBytesAsync(file.FullName);
            var ext = Path.GetExtension(file.Name).ToLower().TrimStart('.');
            return $"data:image/{(ext == "jpg" ? "jpeg" : ext)};base64,{Convert.ToBase64String(bytes)}";
        }
        return string.Empty;
    }

    public string ResolveFilePath(string fileName)
    {
        var root = new DirectoryInfo(VaultRoot);
        var file = root.GetFiles("*", SearchOption.AllDirectories).FirstOrDefault(f => f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        return file != null ? Path.GetRelativePath(VaultRoot, file.FullName).Replace("\\", "/") : string.Empty;
    }

    private VaultItem BuildTree(DirectoryInfo directory, string relativePath)
    {
        var item = new VaultItem { Name = directory.Name, RelativePath = relativePath, IsDirectory = true };
        if (!directory.Exists) return item;
        foreach (var dir in directory.GetDirectories().Where(d => !d.Name.StartsWith(".")))
            item.Children.Add(BuildTree(dir, Path.Combine(relativePath, dir.Name).Replace("\\", "/")));
        foreach (var file in directory.GetFiles("*.md"))
            item.Children.Add(new VaultItem { Name = file.Name, RelativePath = Path.Combine(relativePath, file.Name).Replace("\\", "/"), IsDirectory = false });
        return item;
    }
}
