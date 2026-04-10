namespace PortableObsidian.Services;

public class VaultPathService
{
    public string CurrentPath { get; private set; } = string.Empty;

    public void SetPath(string path)
    {
        CurrentPath = Path.GetFullPath(path);
    }
}