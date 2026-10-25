using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SmoothFrame.Core.Models;

namespace SmoothFrame.Core.Services;

public class GameDiscoveryService
{
    private readonly string _appDataFolder;
    private readonly string _customGamesPath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public GameDiscoveryService(string? baseDirectory = null)
    {
        _appDataFolder = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmoothFrame"
        );
        _customGamesPath = Path.Combine(_appDataFolder, "custom_games.json");

        if (!Directory.Exists(_appDataFolder))
        {
            Directory.CreateDirectory(_appDataFolder);
        }
    }

    private static readonly HashSet<string> ExcludedGameNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Steamworks Common Redistributables",
        "SteamVR",
        "Steam Linux Runtime",
        "Proton",
        "Dedicated Server"
    };

    public List<GameInfo> GetAllGames()
    {
        var games = new List<GameInfo>();

        // 1. Steam Games
        games.AddRange(GetSteamGames());

        // 2. Epic Games
        games.AddRange(GetEpicGames());

        // 3. Custom Games
        games.AddRange(GetCustomGames());

        // Deduplicate by normalized executable path AND by game name
        return games
            .Where(g => !IsExcludedGame(g.Name))
            .GroupBy(g => NormalizePathSafe(g.ExecutablePath))
            .Select(grp => grp.First())
            .GroupBy(g => g.Name.Trim().ToLowerInvariant())
            .Select(grp => grp.First())
            .OrderBy(g => g.Name)
            .ToList();
    }

    private static string NormalizePathSafe(string path)
    {
        try
        {
            return Path.GetFullPath(path).ToLowerInvariant();
        }
        catch
        {
            return (path ?? string.Empty).Trim().ToLowerInvariant();
        }
    }

    private static bool IsExcludedGame(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        foreach (var excluded in ExcludedGameNames)
        {
            if (name.Contains(excluded, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public List<GameInfo> GetSteamGames()
    {
        var list = new List<GameInfo>();

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key == null) return list;

            var steamPath = key.GetValue("SteamPath") as string;
            if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
                return list;

            steamPath = Path.GetFullPath(steamPath.Replace('/', '\\'));
            var libraryFoldersVdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            var libraryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.Combine(steamPath, "steamapps")
            };

            if (File.Exists(libraryFoldersVdf))
            {
                var content = File.ReadAllText(libraryFoldersVdf);
                var matches = Regex.Matches(content, @"""path""\s+""([^""]+)""");
                foreach (Match m in matches)
                {
                    var p = m.Groups[1].Value.Replace(@"\\", @"\").Replace('/', '\\');
                    if (Directory.Exists(p))
                    {
                        var steamapps = Path.GetFullPath(Path.Combine(p, "steamapps"));
                        if (Directory.Exists(steamapps))
                        {
                            libraryPaths.Add(steamapps);
                        }
                    }
                }
            }

            foreach (var lib in libraryPaths)
            {
                if (!Directory.Exists(lib)) continue;

                var manifests = Directory.GetFiles(lib, "appmanifest_*.acf");
                foreach (var manifest in manifests)
                {
                    try
                    {
                        var text = File.ReadAllText(manifest);
                        var nameMatch = Regex.Match(text, @"""name""\s+""([^""]+)""");
                        var dirMatch = Regex.Match(text, @"""installdir""\s+""([^""]+)""");

                        if (nameMatch.Success && dirMatch.Success)
                        {
                            var gameName = nameMatch.Groups[1].Value;
                            var installDirName = dirMatch.Groups[1].Value;
                            var fullInstallPath = Path.Combine(lib, "common", installDirName);

                            if (Directory.Exists(fullInstallPath))
                            {
                                var exe = FindMainExecutable(fullInstallPath, gameName);
                                if (!string.IsNullOrEmpty(exe))
                                {
                                    list.Add(new GameInfo
                                    {
                                        Name = gameName,
                                        ExecutablePath = exe,
                                        ProcessName = Path.GetFileNameWithoutExtension(exe),
                                        IconBase64 = ExtractIconAsBase64(exe),
                                        Source = GameSource.Steam
                                    });
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Skip unreadable manifest
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to discover Steam games: {ex.Message}");
        }

        return list;
    }

    public List<GameInfo> GetEpicGames()
    {
        var list = new List<GameInfo>();

        try
        {
            var manifestsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests"
            );

            if (!Directory.Exists(manifestsPath))
                return list;

            var items = Directory.GetFiles(manifestsPath, "*.item");
            foreach (var file in items)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("DisplayName", out var nameProp) &&
                        root.TryGetProperty("InstallLocation", out var locProp) &&
                        root.TryGetProperty("LaunchExecutable", out var exeProp))
                    {
                        var name = nameProp.GetString();
                        var location = locProp.GetString();
                        var exeName = exeProp.GetString();

                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(location) && !string.IsNullOrEmpty(exeName))
                        {
                            var fullExe = Path.Combine(location, exeName);
                            if (File.Exists(fullExe))
                            {
                                list.Add(new GameInfo
                                {
                                    Name = name,
                                    ExecutablePath = fullExe,
                                    ProcessName = Path.GetFileNameWithoutExtension(fullExe),
                                    IconBase64 = ExtractIconAsBase64(fullExe),
                                    Source = GameSource.Epic
                                });
                            }
                        }
                    }
                }
                catch
                {
                    // Skip corrupt item
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to discover Epic games: {ex.Message}");
        }

        return list;
    }

    private static readonly object CustomGamesLock = new();

    public List<GameInfo> GetCustomGames()
    {
        lock (CustomGamesLock)
        {
            try
            {
                if (!File.Exists(_customGamesPath))
                    return new List<GameInfo>();

                var json = File.ReadAllText(_customGamesPath);
                return JsonSerializer.Deserialize<List<GameInfo>>(json, JsonOptions) ?? new List<GameInfo>();
            }
            catch
            {
                return new List<GameInfo>();
            }
        }
    }

    private void SaveCustomGames(List<GameInfo> list)
    {
        lock (CustomGamesLock)
        {
            var tempPath = _customGamesPath + ".tmp";
            try
            {
                var dir = Path.GetDirectoryName(_customGamesPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(list, JsonOptions);
                File.WriteAllText(tempPath, json);

                if (File.Exists(_customGamesPath))
                {
                    try
                    {
                        File.Replace(tempPath, _customGamesPath, null, ignoreMetadataErrors: true);
                    }
                    catch
                    {
                        File.Copy(tempPath, _customGamesPath, overwrite: true);
                        try { File.Delete(tempPath); } catch { }
                    }
                }
                else
                {
                    try
                    {
                        File.Move(tempPath, _customGamesPath);
                    }
                    catch
                    {
                        File.Copy(tempPath, _customGamesPath, overwrite: true);
                        try { File.Delete(tempPath); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to atomically save custom games: {ex.Message}");
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
    }

    public void AddCustomGame(string executablePath, string? customName = null)
    {
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Файл не найден", executablePath);

        lock (CustomGamesLock)
        {
            var list = GetCustomGames();
            var existing = list.FirstOrDefault(g => g.ExecutablePath.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                return;

            var name = !string.IsNullOrWhiteSpace(customName)
                ? customName
                : Path.GetFileNameWithoutExtension(executablePath);

            list.Add(new GameInfo
            {
                Name = name,
                ExecutablePath = executablePath,
                ProcessName = Path.GetFileNameWithoutExtension(executablePath),
                IconBase64 = ExtractIconAsBase64(executablePath),
                Source = GameSource.Custom
            });

            SaveCustomGames(list);
        }
    }

    public void RemoveCustomGame(string id)
    {
        lock (CustomGamesLock)
        {
            var list = GetCustomGames();
            list.RemoveAll(g => g.Id == id);
            SaveCustomGames(list);
        }
    }

    public static string? ExtractIconAsBase64(string filePath)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(filePath);
            if (icon == null) return null;

            using var bmp = icon.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static string? FindMainExecutable(string rootDir, string gameName)
    {
        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                ReturnSpecialDirectories = false
            };

            var exes = Directory.EnumerateFiles(rootDir, "*.exe", options)
                .Where(p =>
                {
                    var fname = Path.GetFileName(p).ToLowerInvariant();
                    return !fname.Contains("crash") &&
                           !fname.Contains("unins") &&
                           !fname.Contains("redist") &&
                           !fname.Contains("setup") &&
                           !fname.Contains("config") &&
                           !fname.Contains("launcher");
                })
                .ToList();

            if (!exes.Any())
                return null;

            // Prefer exe that matches folder or game name closely
            var cleanGame = Regex.Replace(gameName, @"[^a-zA-Z0-9]", "").ToLowerInvariant();
            var bestMatch = exes.FirstOrDefault(p =>
                Regex.Replace(Path.GetFileNameWithoutExtension(p), @"[^a-zA-Z0-9]", "").ToLowerInvariant().Contains(cleanGame));

            return bestMatch ?? exes.OrderByDescending(p => new FileInfo(p).Length).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
