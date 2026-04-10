using System.Text.Json;

namespace PortableObsidian.Config;

public class AppConfig
{
    public int Port { get; set; } = 30331;
    public string VaultPath { get; set; } = ".";
    public bool IsReadOnly { get; set; } = false;
    public string TunnelToken { get; set; } = ""; // 클라우드플레어 고정 토큰용

    private static readonly string ConfigFileName = "config.json";

    public static AppConfig Load()
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);

        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null) return config;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] 설정 파일을 읽는 중 오류 발생: {ex.Message}");
            }
        }

        // 파일이 없거나 오류 시 기본값으로 생성
        var defaultConfig = new AppConfig();
        Save(defaultConfig);
        return defaultConfig;
    }

    public static void Save(AppConfig config)
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(configPath, json);
        Console.WriteLine($"[Config] 설정 파일이 저장되었습니다: {configPath}");
    }
}