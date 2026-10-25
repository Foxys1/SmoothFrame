using System;
using System.IO;
using System.Text.Json;
using SmoothFrame.Core.Models;

namespace SmoothFrame.Core.Safety;

public class SnapshotManager
{
    private static readonly string DefaultAppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmoothFrame"
    );

    public string StorageDirectory { get; }
    public string SnapshotFilePath { get; }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _ioLock = new();

    public SnapshotManager(string? baseDirectory = null)
    {
        StorageDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? DefaultAppDataFolder
            : baseDirectory;

        SnapshotFilePath = Path.Combine(StorageDirectory, "active_snapshot.json");

        if (!Directory.Exists(StorageDirectory))
        {
            Directory.CreateDirectory(StorageDirectory);
        }
    }

    public bool HasDanglingSnapshot()
    {
        return File.Exists(SnapshotFilePath);
    }

    public bool TryLoadActiveSnapshot(out SystemStateSnapshot? snapshot)
    {
        snapshot = LoadActiveSnapshot();
        return snapshot != null;
    }

    public SystemStateSnapshot? LoadActiveSnapshot()
    {
        lock (_ioLock)
        {
            try
            {
                if (!File.Exists(SnapshotFilePath))
                    return null;

                var json = File.ReadAllText(SnapshotFilePath);
                return JsonSerializer.Deserialize<SystemStateSnapshot>(json, JsonOptions);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public bool SaveSnapshot(SystemStateSnapshot snapshot)
    {
        lock (_ioLock)
        {
            var tempPath = SnapshotFilePath + ".tmp";
            try
            {
                if (!Directory.Exists(StorageDirectory))
                {
                    Directory.CreateDirectory(StorageDirectory);
                }

                var json = JsonSerializer.Serialize(snapshot, JsonOptions);
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(fs))
                {
                    writer.Write(json);
                    writer.Flush();
                    fs.Flush(flushToDisk: true);
                }

                if (File.Exists(SnapshotFilePath))
                {
                    try
                    {
                        File.Replace(tempPath, SnapshotFilePath, null, ignoreMetadataErrors: true);
                    }
                    catch
                    {
                        File.Copy(tempPath, SnapshotFilePath, overwrite: true);
                        try { File.Delete(tempPath); } catch { }
                    }
                }
                else
                {
                    try
                    {
                        File.Move(tempPath, SnapshotFilePath);
                    }
                    catch
                    {
                        File.Copy(tempPath, SnapshotFilePath, overwrite: true);
                        try { File.Delete(tempPath); } catch { }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save snapshot atomically: {ex.Message}");
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch { }
                return false;
            }
        }
    }

    public bool ClearSnapshot()
    {
        lock (_ioLock)
        {
            try
            {
                if (File.Exists(SnapshotFilePath))
                {
                    File.Delete(SnapshotFilePath);
                }

                var tempPath = SnapshotFilePath + ".tmp";
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to delete snapshot: {ex.Message}");
                return false;
            }
        }
    }
}
