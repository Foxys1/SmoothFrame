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

        // 3. GOG Galaxy Games
        games.AddRange(GetGogGames());

        // 4. Ubisoft Connect Games
        games.AddRange(GetUbisoftGames());

        // 5. EA Games
        games.AddRange(GetEaGames());

        // 6. Riot Games
        games.AddRange(GetRiotGames());

        // 7. Roblox Games
        games.AddRange(GetRobloxGames());

        // 8. Custom Games
        games.AddRange(GetCustomGames());

        // Deduplicate by normalized executable path AND by game name
        return games
            .Where(g => !string.IsNullOrWhiteSpace(g.Name) && !IsExcludedGame(g.Name))
            .GroupBy(g => !string.IsNullOrWhiteSpace(g.ExecutablePath) ? NormalizePathSafe(g.ExecutablePath) : g.Id)
            .Select(grp => grp.First())
            .GroupBy(g => g.Name.Trim().ToLowerInvariant())
            .Select(grp => grp.First())
            .OrderBy(g => g.Name)
            .ToList();
    }

    private static string NormalizePathSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            return Path.GetFullPath(path).ToLowerInvariant();
        }
        catch
        {
            return path.Trim().ToLowerInvariant();
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

    public List<GameInfo> GetGogGames()
    {
        var list = new List<GameInfo>();
        var gogKeys = new[] { @"SOFTWARE\GOG.com\Games", @"SOFTWARE\WOW6432Node\GOG.com\Games" };

        foreach (var keyPath in gogKeys)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                if (key == null) continue;

                foreach (var subName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var gameKey = key.OpenSubKey(subName);
                        if (gameKey == null) continue;

                        var name = gameKey.GetValue("gameName") as string;
                        var path = gameKey.GetValue("path") as string;
                        var exe = gameKey.GetValue("exe") as string;

                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;

                        string? fullExe = null;
                        if (!string.IsNullOrWhiteSpace(exe))
                        {
                            var candidate = Path.IsPathRooted(exe) ? exe : Path.Combine(path, exe);
                            if (File.Exists(candidate)) fullExe = candidate;
                        }

                        if (fullExe == null && Directory.Exists(path))
                        {
                            fullExe = FindMainExecutable(path, name);
                        }

                        if (!string.IsNullOrEmpty(fullExe) && File.Exists(fullExe))
                        {
                            list.Add(new GameInfo
                            {
                                Name = name,
                                ExecutablePath = fullExe,
                                ProcessName = Path.GetFileNameWithoutExtension(fullExe),
                                IconBase64 = ExtractIconAsBase64(fullExe),
                                Source = GameSource.GOG
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        return list;
    }

    public List<GameInfo> GetUbisoftGames()
    {
        var list = new List<GameInfo>();
        var ubiKeys = new[] { @"SOFTWARE\Ubisoft\Launcher\Installs", @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs" };

        foreach (var keyPath in ubiKeys)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                if (key == null) continue;

                foreach (var subName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var gameKey = key.OpenSubKey(subName);
                        if (gameKey == null) continue;

                        var installDir = gameKey.GetValue("InstallDir") as string;
                        if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir)) continue;

                        var dirName = Path.GetFileName(installDir.TrimEnd('\\', '/'));
                        var exe = FindMainExecutable(installDir, dirName);
                        if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                        {
                            list.Add(new GameInfo
                            {
                                Name = dirName,
                                ExecutablePath = exe,
                                ProcessName = Path.GetFileNameWithoutExtension(exe),
                                IconBase64 = ExtractIconAsBase64(exe),
                                Source = GameSource.Ubisoft
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        return list;
    }

    public List<GameInfo> GetEaGames()
    {
        var list = new List<GameInfo>();
        var eaKeys = new[]
        {
            @"SOFTWARE\Electronic Arts\EA Core\Installed Games",
            @"SOFTWARE\WOW6432Node\Electronic Arts\EA Core\Installed Games",
            @"SOFTWARE\Origin Games",
            @"SOFTWARE\WOW6432Node\Origin Games"
        };

        foreach (var keyPath in eaKeys)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                if (key == null) continue;

                foreach (var subName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var gameKey = key.OpenSubKey(subName);
                        if (gameKey == null) continue;

                        var installDir = gameKey.GetValue("InstallDir") as string ?? gameKey.GetValue("path") as string;
                        if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir)) continue;

                        var dirName = Path.GetFileName(installDir.TrimEnd('\\', '/'));
                        var exe = FindMainExecutable(installDir, dirName);
                        if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                        {
                            list.Add(new GameInfo
                            {
                                Name = dirName,
                                ExecutablePath = exe,
                                ProcessName = Path.GetFileNameWithoutExtension(exe),
                                IconBase64 = ExtractIconAsBase64(exe),
                                Source = GameSource.EA
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        return list;
    }

    public List<GameInfo> GetRiotGames()
    {
        var list = new List<GameInfo>();
        var commonRiotGames = new (string Name, string ExeRelativePath)[]
        {
            ("VALORANT", @"Riot Games\VALORANT\live\VALORANT.exe"),
            ("League of Legends", @"Riot Games\League of Legends\LeagueClient.exe")
        };

        try
        {
            DriveInfo[] drives;
            try
            {
                drives = DriveInfo.GetDrives();
            }
            catch
            {
                return list;
            }

            foreach (var drive in drives)
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                        continue;

                    foreach (var (name, relPath) in commonRiotGames)
                    {
                        try
                        {
                            var fullPath = Path.Combine(drive.RootDirectory.FullName, relPath);
                            if (File.Exists(fullPath))
                            {
                                list.Add(new GameInfo
                                {
                                    Name = name,
                                    ExecutablePath = fullPath,
                                    ProcessName = Path.GetFileNameWithoutExtension(fullPath),
                                    IconBase64 = ExtractIconAsBase64(fullPath),
                                    Source = GameSource.Riot
                                });
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to discover Riot games: {ex.Message}");
        }

        return list;
    }

    public List<GameInfo> GetRobloxGames()
    {
        var list = new List<GameInfo>();

        try
        {
            string? bestExe = null;

            // 1. Scan %LOCALAPPDATA%\Roblox\Versions (Standard user install)
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
            {
                bestExe = FindLatestRobloxExecutable(Path.Combine(localAppData, "Roblox", "Versions"));
            }

            // 2. Scan %ProgramFiles(x86)%\Roblox\Versions & %ProgramFiles%\Roblox\Versions (System-wide install)
            if (string.IsNullOrEmpty(bestExe))
            {
                var progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (!string.IsNullOrEmpty(progFilesX86))
                {
                    bestExe = FindLatestRobloxExecutable(Path.Combine(progFilesX86, "Roblox", "Versions"));
                }
            }

            if (string.IsNullOrEmpty(bestExe))
            {
                var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrEmpty(progFiles))
                {
                    bestExe = FindLatestRobloxExecutable(Path.Combine(progFiles, "Roblox", "Versions"));
                }
            }

            // 3. Scan Registry Protocol Association (roblox-player://)
            if (string.IsNullOrEmpty(bestExe))
            {
                var protocolKeys = new[]
                {
                    (Registry.CurrentUser, @"Software\Classes\roblox-player\shell\open\command"),
                    (Registry.ClassesRoot, @"roblox-player\shell\open\command")
                };

                foreach (var (rootKey, subPath) in protocolKeys)
                {
                    try
                    {
                        using var key = rootKey.OpenSubKey(subPath);
                        var cmd = key?.GetValue(null) as string;
                        if (!string.IsNullOrWhiteSpace(cmd))
                        {
                            var match = Regex.Match(cmd, "(?:\"(?<path>[^\"]+RobloxPlayer(?:Beta|Launcher)\\.exe)\"|(?<path>[^\\s\"]+RobloxPlayer(?:Beta|Launcher)\\.exe))", RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                var launcherPath = match.Groups["path"].Value;
                                var dir = Path.GetDirectoryName(launcherPath);
                                if (!string.IsNullOrEmpty(dir))
                                {
                                    var playerBeta = Path.Combine(dir, "RobloxPlayerBeta.exe");
                                    if (File.Exists(playerBeta))
                                    {
                                        bestExe = playerBeta;
                                        break;
                                    }
                                    else if (File.Exists(launcherPath))
                                    {
                                        bestExe = launcherPath;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            // 4. Scan Registry Uninstall keys
            if (string.IsNullOrEmpty(bestExe))
            {
                var uninstallKeys = new[]
                {
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                };

                foreach (var baseKey in new[] { Registry.CurrentUser, Registry.LocalMachine })
                {
                    foreach (var uninstPath in uninstallKeys)
                    {
                        try
                        {
                            using var key = baseKey.OpenSubKey(uninstPath);
                            if (key == null) continue;

                            foreach (var subName in key.GetSubKeyNames())
                            {
                                if (!subName.Contains("Roblox", StringComparison.OrdinalIgnoreCase)) continue;

                                using var subKey = key.OpenSubKey(subName);
                                if (subKey == null) continue;

                                var installLoc = subKey.GetValue("InstallLocation") as string;
                                if (!string.IsNullOrEmpty(installLoc) && Directory.Exists(installLoc))
                                {
                                    var candidate = FindLatestRobloxExecutable(installLoc) ?? FindLatestRobloxExecutable(Path.Combine(installLoc, "Versions"));
                                    if (!string.IsNullOrEmpty(candidate))
                                    {
                                        bestExe = candidate;
                                        break;
                                    }
                                }

                                var displayIcon = subKey.GetValue("DisplayIcon") as string;
                                if (!string.IsNullOrEmpty(displayIcon))
                                {
                                    var cleanIconPath = displayIcon.Trim('"', ' ', ',');
                                    if (cleanIconPath.Contains(','))
                                        cleanIconPath = cleanIconPath.Split(',')[0].Trim();

                                    if (File.Exists(cleanIconPath) && cleanIconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var dir = Path.GetDirectoryName(cleanIconPath);
                                        var playerBeta = Path.Combine(dir ?? "", "RobloxPlayerBeta.exe");
                                        bestExe = File.Exists(playerBeta) ? playerBeta : cleanIconPath;
                                        break;
                                    }
                                }
                            }
                            if (!string.IsNullOrEmpty(bestExe)) break;
                        }
                        catch { }
                    }
                    if (!string.IsNullOrEmpty(bestExe)) break;
                }
            }

            // If found, add standalone Roblox Player
            if (!string.IsNullOrEmpty(bestExe) && File.Exists(bestExe))
            {
                list.Add(new GameInfo
                {
                    Name = "Roblox",
                    ExecutablePath = bestExe,
                    ProcessName = Path.GetFileNameWithoutExtension(bestExe),
                    IconBase64 = ExtractIconAsBase64(bestExe),
                    Source = GameSource.Roblox
                });
            }

            // 5. Scan Microsoft Store (UWP) Roblox
            var uwpRoblox = FindUwpRoblox();
            if (uwpRoblox != null)
            {
                list.Add(uwpRoblox);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to discover Roblox games: {ex.Message}");
        }

        return list;
    }

    private static string? FindLatestRobloxExecutable(string versionsDir)
    {
        try
        {
            if (!Directory.Exists(versionsDir)) return null;

            var direct = Path.Combine(versionsDir, "RobloxPlayerBeta.exe");
            if (File.Exists(direct)) return direct;

            var subDirs = Directory.GetDirectories(versionsDir)
                .Select(d => new DirectoryInfo(d))
                .OrderByDescending(d => d.LastWriteTimeUtc)
                .ToList();

            foreach (var dir in subDirs)
            {
                var candidate = Path.Combine(dir.FullName, "RobloxPlayerBeta.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            foreach (var dir in subDirs)
            {
                var candidate = Directory.GetFiles(dir.FullName, "RobloxPlayer*.exe").FirstOrDefault();
                if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch { }

        return null;
    }

    private static GameInfo? FindUwpRoblox()
    {
        var packageRoots = new[]
        {
            (Registry.CurrentUser, @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"),
            (Registry.LocalMachine, @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages")
        };

        foreach (var (rootKey, subPath) in packageRoots)
        {
            try
            {
                using var key = rootKey.OpenSubKey(subPath);
                if (key != null)
                {
                    foreach (var subName in key.GetSubKeyNames())
                    {
                        if (subName.StartsWith("ROBLOXCORPORATION.ROBLOX", StringComparison.OrdinalIgnoreCase))
                        {
                            using var pkgKey = key.OpenSubKey(subName);
                            var rootFolder = pkgKey?.GetValue("PackageRootFolder") as string;
                            if (!string.IsNullOrEmpty(rootFolder) && Directory.Exists(rootFolder))
                            {
                                var exe = Path.Combine(rootFolder, "Windows10Universal.exe");
                                if (File.Exists(exe))
                                {
                                    return new GameInfo
                                    {
                                        Name = "Roblox",
                                        ExecutablePath = exe,
                                        ProcessName = "Windows10Universal",
                                        IconBase64 = ExtractIconAsBase64(exe),
                                        Source = GameSource.Roblox
                                    };
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        return null;
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
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

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
            var cleanGame = Regex.Replace(gameName, @"[^\p{L}\p{N}]", "").ToLowerInvariant();
            string? bestMatch = null;
            if (!string.IsNullOrEmpty(cleanGame))
            {
                bestMatch = exes.FirstOrDefault(p =>
                    Regex.Replace(Path.GetFileNameWithoutExtension(p), @"[^\p{L}\p{N}]", "").ToLowerInvariant().Contains(cleanGame));
            }

            return bestMatch ?? exes.OrderByDescending(p =>
            {
                try { return new FileInfo(p).Length; } catch { return 0L; }
            }).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
