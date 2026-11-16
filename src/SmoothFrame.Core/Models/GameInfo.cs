using System;

namespace SmoothFrame.Core.Models;

public enum GameSource
{
    Steam,
    Epic,
    GOG,
    Ubisoft,
    EA,
    Riot,
    Roblox,
    Custom
}

public class GameInfo : IEquatable<GameInfo>
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private string _executablePath = string.Empty;
    private string _processName = string.Empty;

    public string Id
    {
        get => _id;
        set => _id = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value;
    }

    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    public string ExecutablePath
    {
        get => _executablePath;
        set => _executablePath = value ?? string.Empty;
    }

    public string ProcessName
    {
        get => _processName;
        set => _processName = value ?? string.Empty;
    }

    public string? IconBase64 { get; set; }
    public GameSource Source { get; set; } = GameSource.Custom;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public bool Equals(GameInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        if (!string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(other.Id))
        {
            if (string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(ExecutablePath) && !string.IsNullOrWhiteSpace(other.ExecutablePath))
        {
            return string.Equals(NormalizePath(ExecutablePath), NormalizePath(other.ExecutablePath), StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(Name.Trim(), other.Name.Trim(), StringComparison.OrdinalIgnoreCase) && Source == other.Source;
    }

    public override bool Equals(object? obj) => Equals(obj as GameInfo);

    public override int GetHashCode()
    {
        if (!string.IsNullOrWhiteSpace(ExecutablePath))
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(NormalizePath(ExecutablePath));
        }

        if (!string.IsNullOrWhiteSpace(Name))
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(Name.Trim());
        }

        return StringComparer.OrdinalIgnoreCase.GetHashCode(Id);
    }

    public override string ToString() => $"{Name} ({Source}) - {ExecutablePath}";

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            return System.IO.Path.GetFullPath(path).ToLowerInvariant();
        }
        catch
        {
            return path.Trim().ToLowerInvariant();
        }
    }
}
