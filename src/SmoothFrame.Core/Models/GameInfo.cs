using System;

namespace SmoothFrame.Core.Models;

public enum GameSource
{
    Steam,
    Epic,
    Custom
}

public class GameInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string? IconBase64 { get; set; }
    public GameSource Source { get; set; } = GameSource.Custom;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
