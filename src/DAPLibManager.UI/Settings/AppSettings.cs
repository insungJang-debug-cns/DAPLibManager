using System.IO;
using System.Text.Json;

namespace DAPLibManager.UI.Settings;

internal sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DAPLibManager", "settings.json");

    public string? LastFolder { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Settings] Load failed: {ex.GetType().Name}: {ex.Message}"); }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Settings] Save failed: {ex.GetType().Name}: {ex.Message}"); }
    }
}
