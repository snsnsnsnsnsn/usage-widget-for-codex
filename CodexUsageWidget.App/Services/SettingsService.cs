using System.Text.Json;
using System.IO;
using CodexUsageWidget.App.Models;

namespace CodexUsageWidget.App.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexUsageWidget");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "settings.json");
    }

    public WidgetSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(_path), JsonOptions)
                       ?? new WidgetSettings();
            }
        }
        catch { }
        return new WidgetSettings();
    }

    public void Save(WidgetSettings settings)
    {
        try
        {
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, _path, true);
        }
        catch { }
    }
}
