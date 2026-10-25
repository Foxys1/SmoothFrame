using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmoothFrame.UI.Models;

/// <summary>
/// Persisted per-user UI preferences (first-run flag + language).
/// Stored as %LocalAppData%\SmoothFrame\ui_settings.json, written atomically.
/// </summary>
public sealed class UiSettings
{
    private static readonly string Directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmoothFrame");

    private static readonly string FilePath = Path.Combine(Directory, "ui_settings.json");

    [JsonPropertyName("onboardingCompleted")]
    public bool OnboardingCompleted { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; } = "ru";

    public static UiSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<UiSettings>(json);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UiSettings load error: {ex.Message}");
        }

        return new UiSettings();
    }

    private static readonly object _saveLock = new();

    public void Save()
    {
        lock (_saveLock)
        {
            string? temp = null;
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

                // Atomic replace: write to a unique temp file, flush, then move over the target.
                temp = Path.Combine(Directory, $"settings_{Guid.NewGuid():N}.tmp");
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(true);
                }

                File.Move(temp, FilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UiSettings save error: {ex.Message}");
                try
                {
                    if (temp != null && File.Exists(temp))
                    {
                        File.Delete(temp);
                    }
                }
                catch { }
            }
        }
    }
}
