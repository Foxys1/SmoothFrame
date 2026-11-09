using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SmoothFrame.Core.Models;

namespace SmoothFrame.Core.Services;

public static class MinecraftModAdvisor
{
    private static readonly List<(string Key, string Name, string Category, string Description)> KnownBaseEngines = new()
    {
        (
            "sodium",
            "Sodium / Embeddium",
            "Графика",
            "Современный движок рендеринга чанков на OpenGL."
        ),
        (
            "iris",
            "Iris Shaders",
            "Графика",
            "Быстрый движок шейдеров, полностью совместимый с Sodium."
        ),
        (
            "distanthorizons",
            "Distant Horizons",
            "Дальняя прорисовка",
            "Отрисовка упрощенного ландшафта на сотни чанков вперед с низким влиянием на FPS."
        ),
        (
            "smoothframe",
            "SmoothFrame In-Game Optimizer",
            "Комплексная оптимизация",
            "Фирменный NeoForge мод: сглаживание фреймтайма, куллинг и телеметрия."
        )
    };

    private static readonly List<(string Name, string Category, string Description)> SmoothFrameBuiltinFeatures = new()
    {
        (
            "Frustum & Distance Entity Culling",
            "Рендеринг / CPU",
            "Отсекает невидимых мобов и сущностей за спиной и за пределами дистанции (до +30-40% FPS возле баз и ферм)."
        ),
        (
            "Native JVM String Deduplication",
            "Память (RAM)",
            "Нативная дедупликация строк в куче ZGC Java 21 (экономит 1.5–2 ГБ ОЗУ без сторонних модов)."
        ),
        (
            "Zero-Stall Chunk Streamer",
            "Чанки & Мир",
            "Бесшовная подгрузка чанков без задержек потока рендера — максимальный FPS при полётах на элитрах."
        ),
        (
            "Dynamic FPS & Battery Saver",
            "Фоновые службы",
            "Снижение нагрева GPU и энергопотребления при сворачивании с мгновенным возвратом полной герцовки."
        ),
        (
            "DirectX High-Performance GPU Lock",
            "Аппаратное ускорение",
            "Принудительная фиксация javaw.exe на дискретной видеокарте NVIDIA/AMD через системный реестр Windows."
        )
    };

    public static MinecraftModAdvisorReport AnalyzeModsDirectory(string? modsDirectory)
    {
        var report = new MinecraftModAdvisorReport
        {
            ModsDirectory = modsDirectory ?? string.Empty
        };

        // Populate built-in SmoothFrame features
        foreach (var feature in SmoothFrameBuiltinFeatures)
        {
            report.BuiltinTechnologies.Add(new MinecraftModInfo
            {
                Name = feature.Name,
                Category = feature.Category,
                Description = feature.Description,
                IsInstalled = true,
                RecommendationReason = "Встроено в экосистему SmoothFrame"
            });
        }

        if (string.IsNullOrWhiteSpace(modsDirectory) || !Directory.Exists(modsDirectory))
        {
            return report;
        }

        try
        {
            var jarFiles = Directory.GetFiles(modsDirectory, "*.jar", SearchOption.TopDirectoryOnly);
            report.TotalInstalledModsCount = jarFiles.Length;

            var jarNames = jarFiles.Select(Path.GetFileName).Where(n => n != null).ToList();

            foreach (var item in KnownBaseEngines)
            {
                string key = item.Key;
                bool MatchesMod(string? name) =>
                    name != null && (
                        name.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                        (key == "sodium" && (name.Contains("embeddium", StringComparison.OrdinalIgnoreCase) || name.Contains("rubidium", StringComparison.OrdinalIgnoreCase))) ||
                        (key == "iris" && name.Contains("oculus", StringComparison.OrdinalIgnoreCase)) ||
                        (key == "distanthorizons" && (name.Contains("distant-horizons", StringComparison.OrdinalIgnoreCase) || name.Contains("distanthorizons", StringComparison.OrdinalIgnoreCase)))
                    );

                string? matchedFile = jarNames.FirstOrDefault(MatchesMod);
                if (matchedFile != null)
                {
                    report.DetectedPerformanceMods.Add(new MinecraftModInfo
                    {
                        Name = item.Name,
                        FileName = matchedFile,
                        IsInstalled = true,
                        Category = item.Category,
                        Description = item.Description
                    });
                }
            }

            report.HasSodium = report.DetectedPerformanceMods.Any(m => m.Name.Contains("Sodium"));
            report.HasIris = report.DetectedPerformanceMods.Any(m => m.Name.Contains("Iris"));
            report.HasDistantHorizons = report.DetectedPerformanceMods.Any(m => m.Name.Contains("Distant Horizons"));
            report.HasSmoothFrameMod = report.DetectedPerformanceMods.Any(m => m.Name.Contains("SmoothFrame"));

            report.HasFerriteCore = jarNames.Any(n => n != null && n.Contains("ferritecore", StringComparison.OrdinalIgnoreCase));
            report.HasImmediatelyFast = jarNames.Any(n => n != null && n.Contains("immediatelyfast", StringComparison.OrdinalIgnoreCase));
            report.HasModernFix = jarNames.Any(n => n != null && n.Contains("modernfix", StringComparison.OrdinalIgnoreCase));
            report.HasEntityCulling = jarNames.Any(n => n != null && (n.Contains("entityculling", StringComparison.OrdinalIgnoreCase) || n.Contains("entity-culling", StringComparison.OrdinalIgnoreCase)));

            if (!report.HasFerriteCore)
            {
                report.RecommendedMods.Add(new MinecraftModInfo
                {
                    Name = "FerriteCore",
                    Category = "Память (RAM)",
                    Description = "Оптимизация структур данных Minecraft в памяти (сокращает потребление ОЗУ на 30-50%).",
                    IsInstalled = false,
                    RecommendationReason = "Рекомендуется для сокращения расхода оперативной памяти."
                });
            }

            if (!report.HasImmediatelyFast)
            {
                report.RecommendedMods.Add(new MinecraftModInfo
                {
                    Name = "ImmediatelyFast",
                    Category = "Рендеринг / UI",
                    Description = "Оптимизация рендеринга GUI, текста и сущностей.",
                    IsInstalled = false,
                    RecommendationReason = "Увеличивает FPS при открытом интерфейсе и рендеринге текста/HUD."
                });
            }

            if (!report.HasModernFix)
            {
                report.RecommendedMods.Add(new MinecraftModInfo
                {
                    Name = "ModernFix",
                    Category = "Комплексная оптимизация",
                    Description = "Ускорение загрузки игры и исправление утечек памяти.",
                    IsInstalled = false,
                    RecommendationReason = "Значительно сокращает время запуска и исправляет утечки ресурсов."
                });
            }
        }
        catch { }

        return report;
    }
}
