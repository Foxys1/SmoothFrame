using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SmoothFrame.Core.Services;

public class ShaderCleanResult
{
    public bool Success { get; set; }
    public long BytesFreed { get; set; }
    public int FilesDeleted { get; set; }
    public string FormattedSize => FormatBytes(BytesFreed);
    public string Message { get; set; } = string.Empty;

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 MB";
        double mb = bytes / (1024.0 * 1024.0);
        if (mb >= 1024)
        {
            double gb = mb / 1024.0;
            return $"{gb:F2} GB";
        }
        return $"{mb:F1} MB";
    }
}

/// <summary>
/// Safe shader cache cleaner for DirectX 11/12, Vulkan, NVIDIA, AMD, and Intel GPUs.
/// Removes corrupted or stale shader binaries that cause micro-stutters and frame drops.
/// Safely skips active files locked by running display drivers or games.
/// </summary>
public static class ShaderCacheCleaner
{
    private static readonly string[] CacheDirectories = new[]
    {
        // Microsoft DirectX Shader Cache
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D3DSCache"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DirectX Shader Cache"),

        // NVIDIA Driver Shader Caches (DirectX, OpenGL, Compute)
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NVIDIA", "DXCache"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NVIDIA", "GLCache"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NVIDIA", "ComputeCache"),

        // AMD Radeon Driver Shader Caches
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD", "DxCache"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD", "GLCache"),

        // Intel Arc / Graphics Shader Cache
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Intel", "ShaderCache")
    };

    public static ShaderCleanResult CleanShaderCache()
    {
        long totalBytesFreed = 0;
        int totalFilesDeleted = 0;

        foreach (var dir in CacheDirectories)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;

                CleanDirectoryRecursively(dir, ref totalBytesFreed, ref totalFilesDeleted);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShaderCacheCleaner] Error cleaning {dir}: {ex.Message}");
            }
        }

        return new ShaderCleanResult
        {
            Success = true,
            BytesFreed = totalBytesFreed,
            FilesDeleted = totalFilesDeleted,
            Message = totalFilesDeleted > 0
                ? $"Очищено {totalFilesDeleted} файлов кэша шейдеров ({FormatBytes(totalBytesFreed)})."
                : "Кэш шейдеров уже чист."
        };
    }

    private static void CleanDirectoryRecursively(string directoryPath, ref long totalBytesFreed, ref int totalFilesDeleted)
    {
        try
        {
            var files = Directory.GetFiles(directoryPath);
            foreach (var file in files)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    long length = fileInfo.Length;

                    fileInfo.Attributes = FileAttributes.Normal;
                    fileInfo.Delete();

                    totalBytesFreed += length;
                    totalFilesDeleted++;
                }
                catch (IOException)
                {
                    // File is actively locked by display driver or running game — skip safely
                }
                catch (UnauthorizedAccessException)
                {
                    // Access denied — skip safely
                }
                catch { }
            }

            var subdirs = Directory.GetDirectories(directoryPath);
            foreach (var subdir in subdirs)
            {
                CleanDirectoryRecursively(subdir, ref totalBytesFreed, ref totalFilesDeleted);
                try
                {
                    if (Directory.GetFileSystemEntries(subdir).Length == 0)
                    {
                        Directory.Delete(subdir);
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 MB";
        double mb = bytes / (1024.0 * 1024.0);
        if (mb >= 1024)
        {
            return $"{mb / 1024.0:F2} GB";
        }
        return $"{mb:F1} MB";
    }
}
