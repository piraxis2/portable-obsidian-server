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

    public VaultHub(VaultPathService pathService, IConfiguration config, IHubContext<VaultHub> hubContext)
    {
        _pathService = pathService;
        _staticContext = hubContext;

        if (config.GetValue<bool>("IS_READ_ONLY_INIT")) {
            _isReadOnly = true;
        }

        // 워처 초기화 (한 번만 실행)
        InitializeWatcher();
    }

    private void InitializeWatcher()
    {
        if (_watcher != null) return;

        _watcher = new FileSystemWatcher(_pathService.CurrentPath, "*.md")
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

    // 볼트 경로가 바뀌었을 때 워처 경로 갱신용 (정적 메서드)
    public static void UpdateWatcherPath(string newPath)
    {
        if (_watcher != null)
        {
            _watcher.Path = newPath;
        }
    }

    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            // 파일이 사용 중일 수 있으므로 잠시 대기 후 읽기 (옵시디언 저장 완료 대기)
            await Task.Delay(500);
            
            var relativePath = Path.GetRelativePath(_pathService.CurrentPath, e.FullPath);
            var content = await File.ReadAllTextAsync(e.FullPath);
            
            // 변경된 내용을 모든 클라이언트에 브로드캐스트
            await _staticContext!.Clients.All.SendAsync("FileUpdated", relativePath, content);
        }
        catch { /* 파일 접근 중인 경우 무시 */ }
    }

    private string VaultRoot => _pathService.CurrentPath;

    public static void SetReadOnly(bool readOnly) => _isReadOnly = readOnly;
    public bool GetIsReadOnly() => _isReadOnly;

    public Task<VaultItem> GetVaultTree()
    {
        var root = new DirectoryInfo(VaultRoot);
        return Task.FromResult(BuildTree(root, ""));
    }

    public async Task<string> ReadFile(string relativePath)
    {
        var fullPath = Path.Combine(VaultRoot, relativePath);
        if (!File.Exists(fullPath)) return "파일을 찾을 수 없습니다.";
        return await File.ReadAllTextAsync(fullPath);
    }

    public async Task SaveFile(string relativePath, string content)
    {
        if (_isReadOnly) throw new HubException("현재 읽기 전용 모드입니다.");

        var fullPath = Path.Combine(VaultRoot, relativePath);
        
        if (!Path.GetFullPath(fullPath).StartsWith(Path.GetFullPath(VaultRoot)))
            throw new HubException("접근 권한이 없는 경로입니다.");

        // 워처 이벤트를 잠시 끄고 저장 (내가 저장한 건 내가 다시 받지 않도록)
        if (_watcher != null) _watcher.EnableRaisingEvents = false;
        
        await File.WriteAllTextAsync(fullPath, content);
        
        if (_watcher != null) _watcher.EnableRaisingEvents = true;

        await Clients.Others.SendAsync("FileUpdated", relativePath, content);
    }

    public async Task CreateFile(string relativePath)
    {
        if (_isReadOnly) throw new HubException("현재 읽기 전용 모드입니다.");
        var fullPath = Path.Combine(VaultRoot, relativePath);
        if (!fullPath.EndsWith(".md")) fullPath += ".md";
        if (File.Exists(fullPath)) throw new HubException("이미 존재하는 파일명입니다.");
        await File.WriteAllTextAsync(fullPath, "");
        await Clients.All.SendAsync("VaultChanged");
    }

    public async Task CreateDirectory(string relativePath)
    {
        if (_isReadOnly) throw new HubException("현재 읽기 전용 모드입니다.");
        var fullPath = Path.Combine(VaultRoot, relativePath);
        if (Directory.Exists(fullPath)) throw new HubException("이미 존재하는 폴더명입니다.");
        Directory.CreateDirectory(fullPath);
        await Clients.All.SendAsync("VaultChanged");
    }

    public async Task RenameFile(string oldRelativePath, string newName)
    {
        if (_isReadOnly) throw new HubException("현재 읽기 전용 모드입니다.");
        var oldFullPath = Path.Combine(VaultRoot, oldRelativePath);
        if (!File.Exists(oldFullPath)) throw new HubException("변경할 파일을 찾을 수 없습니다.");
        if (!newName.ToLower().EndsWith(".md")) newName += ".md";
        var directory = Path.GetDirectoryName(oldFullPath) ?? VaultRoot;
        var newFullPath = Path.Combine(directory, newName);
        if (File.Exists(newFullPath)) throw new HubException("동일한 이름의 파일이 이미 존재합니다.");
        File.Move(oldFullPath, newFullPath);
        var newRelativePath = Path.GetRelativePath(VaultRoot, newFullPath);
        await Clients.All.SendAsync("VaultChanged");
        await Clients.All.SendAsync("FileRenamed", oldRelativePath, newRelativePath, newName);
    }

    // 파일 이동
    public async Task MoveFile(string fileRelativePath, string targetDirectoryRelativePath)
    {
        if (_isReadOnly) throw new HubException("현재 읽기 전용 모드입니다.");

        var oldFullPath = Path.Combine(VaultRoot, fileRelativePath);
        var targetDirFullPath = Path.Combine(VaultRoot, targetDirectoryRelativePath);
        
        if (!File.Exists(oldFullPath)) throw new HubException("이동할 파일을 찾을 수 없습니다.");
        if (!Directory.Exists(targetDirFullPath)) throw new HubException("대상 폴더를 찾을 수 없습니다.");

        var fileName = Path.GetFileName(oldFullPath);
        var newFullPath = Path.Combine(targetDirFullPath, fileName);

        if (File.Exists(newFullPath)) throw new HubException("대상 폴더에 동일한 이름의 파일이 이미 존재합니다.");

        File.Move(oldFullPath, newFullPath);
        
        await Clients.All.SendAsync("VaultChanged");
    }

    // 파일 및 내용 검색
    public async Task<List<VaultItem>> SearchVault(string query)
    {
        var results = new List<VaultItem>();
        if (string.IsNullOrWhiteSpace(query)) return results;

        var root = new DirectoryInfo(VaultRoot);
        var files = root.GetFiles("*.md", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(VaultRoot, file.FullName);
            
            // 1. 파일 이름 검색
            if (file.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new VaultItem { Name = file.Name, RelativePath = relativePath, IsDirectory = false });
                continue;
            }

            // 2. 파일 내용 검색
            try
            {
                var content = await File.ReadAllTextAsync(file.FullName);
                if (content.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new VaultItem { Name = file.Name, RelativePath = relativePath, IsDirectory = false });
                }
            }
            catch { /* 읽기 오류 무시 */ }
        }

        return results;
    }

    // 파일명만으로 실제 경로 찾기 (이미지 등)
    public string ResolveFilePath(string fileName)
    {
        var root = new DirectoryInfo(VaultRoot);
        var file = root.GetFiles("*", SearchOption.AllDirectories)
                       .FirstOrDefault(f => f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        
        if (file != null)
        {
            var relativePath = Path.GetRelativePath(VaultRoot, file.FullName);
            return relativePath.Replace("\\", "/");
        }
        return string.Empty;
    }

    // 이미지 파일을 Base64로 변환하여 직접 전송
    public async Task<string> GetImageBase64(string fileName)
    {
        var root = new DirectoryInfo(VaultRoot);
        var file = root.GetFiles("*", SearchOption.AllDirectories)
                       .FirstOrDefault(f => f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));

        if (file != null)
        {
            var bytes = await File.ReadAllBytesAsync(file.FullName);
            var extension = Path.GetExtension(file.Name).ToLower().TrimStart('.');
            if (extension == "jpg") extension = "jpeg";
            
            return $"data:image/{extension};base64,{Convert.ToBase64String(bytes)}";
        }
        return string.Empty;
    }

    private VaultItem BuildTree(DirectoryInfo directory, string relativePath)
    {
        var item = new VaultItem { Name = directory.Name, RelativePath = relativePath, IsDirectory = true };
        if (!directory.Exists) return item;
        foreach (var dir in directory.GetDirectories())
        {
            if (dir.Name.StartsWith(".")) continue;
            item.Children.Add(BuildTree(dir, Path.Combine(relativePath, dir.Name)));
        }
        foreach (var file in directory.GetFiles("*.md"))
        {
            item.Children.Add(new VaultItem { Name = file.Name, RelativePath = Path.Combine(relativePath, file.Name), IsDirectory = false });
        }
        return item;
    }
}