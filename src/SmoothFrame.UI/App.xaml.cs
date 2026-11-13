using System.Windows;
using System.Windows.Media;
using SmoothFrame.UI.Localization;
using SmoothFrame.UI.Models;

namespace SmoothFrame.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            MessageBox.Show($"Произошла ошибка в приложении:\n\n{e.Exception.Message}", "SmoothFrame", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"Критическая ошибка приложения:\n\n{ex.Message}", "SmoothFrame", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Load persisted UI preferences (language, theme, onboarding state) before any window is created.
        var settings = UiSettings.Load();
        ApplyTheme(settings.IsDarkTheme);

        if (!LocalizationManager.IsInitialized)
        {
            LocalizationManager.Initialize(settings.Language);
        }
    }

    public static void ApplyTheme(bool isDark)
    {
        var app = Current;
        if (app == null)
        {
            return;
        }

        var resources = app.Resources;

        void SetSolid(string key, string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }

        void SetGradient(string key, string startHex, string endHex)
        {
            var start = (Color)ColorConverter.ConvertFromString(startHex);
            var end = (Color)ColorConverter.ConvertFromString(endHex);
            var brush = new LinearGradientBrush(start, end, new Point(0, 0), new Point(1, 1));
            brush.Freeze();
            resources[key] = brush;
        }

        if (isDark)
        {
            SetSolid("BrushBgWindow", "#13141D");
            SetSolid("BrushBgApp", "#13141D");
            SetSolid("BrushBgSidebar", "#171823");
            SetSolid("BrushBgSide", "#171823");
            SetSolid("BrushBgTitleBar", "#151620");
            SetSolid("BrushCardBg", "#1C1E2B");
            SetSolid("BrushBgCard", "#1C1E2B");
            SetGradient("BrushHeroBg", "#201E33", "#1A1C29");
            SetGradient("BrushHeroCardBg", "#201E33", "#1A1C29");
            SetGradient("BrushMinecraftHeroBg", "#201E33", "#1A1C29");
            SetSolid("BrushBgInset", "#161722");
            SetSolid("BrushCardHover", "#242638");
            SetSolid("BrushBgCardHover", "#242638");
            SetSolid("BrushNavActiveBg", "#252242");
            SetSolid("BrushBadgeSoft", "#23203C");
            SetSolid("BrushAccentSoft", "#23203C");
            SetSolid("BrushOverlayScrim", "#B8090A12");

            SetSolid("BrushBorder", "#282B3E");
            SetSolid("BrushBorderSubtle", "#282B3E");
            SetSolid("BrushBorderStrong", "#30334A");
            SetSolid("BrushBorderCard", "#30334A");
            SetSolid("BrushBorderGlow", "#4D427E");
            SetSolid("BrushBorderAccent", "#6D5BD0");

            SetSolid("BrushTextPrimary", "#ECEEF6");
            SetSolid("BrushTextSecondary", "#AEB4C9");
            SetSolid("BrushTextMuted", "#7B8199");

            SetSolid("BrushAccent", "#7C6AE8");
            SetSolid("BrushAccentHover", "#8D7DF2");
            SetSolid("BrushAccentPressed", "#6957D4");
            SetSolid("BrushVioletText", "#B8ACFF");
            SetSolid("BrushOnAccent", "#FFFFFF");

            SetSolid("BrushSuccess", "#4CC38A");
            SetSolid("BrushSuccessSoft", "#182E26");
            SetSolid("BrushSuccessTint", "#182E26");
            SetSolid("BrushSuccessBorder", "#285442");

            SetSolid("BrushWarning", "#E0A854");
            SetSolid("BrushWarningSoft", "#2E2416");
            SetSolid("BrushWarningTint", "#2E2416");
            SetSolid("BrushWarningBorder", "#5B4421");

            SetSolid("BrushDanger", "#E26D7A");
            SetSolid("BrushDangerHover", "#EB8590");
            SetSolid("BrushDangerPressed", "#C85865");
            SetSolid("BrushDangerSoft", "#301A20");
            SetSolid("BrushDangerTint", "#301A20");
            SetSolid("BrushDangerBorder", "#5E2B36");

            SetSolid("BrushInfo", "#6EA8F5");

            SetSolid("BrushScrollThumb", "#363A54");
            SetSolid("BrushScrollThumbHover", "#5C5194");
            SetSolid("BrushScrollTrack", "#151621");

            SetSolid("BrushHeroRingOuter", "#167C6AE8");
            SetSolid("BrushHeroRingMid", "#2E7C6AE8");
            SetSolid("BrushHeroRingCore", "#24213B");
        }
        else
        {
            SetSolid("BrushBgWindow", "#F4F3F9");
            SetSolid("BrushBgApp", "#F4F3F9");
            SetSolid("BrushBgSidebar", "#EAE7F4");
            SetSolid("BrushBgSide", "#EAE7F4");
            SetSolid("BrushBgTitleBar", "#E5E1F0");
            SetSolid("BrushCardBg", "#FFFFFF");
            SetSolid("BrushBgCard", "#FFFFFF");
            SetGradient("BrushHeroBg", "#FBF9FF", "#F2EFFC");
            SetGradient("BrushHeroCardBg", "#FBF9FF", "#F2EFFC");
            SetGradient("BrushMinecraftHeroBg", "#FBF9FF", "#F2EFFC");
            SetSolid("BrushBgInset", "#F5F3FA");
            SetSolid("BrushCardHover", "#EBE7F7");
            SetSolid("BrushBgCardHover", "#EBE7F7");
            SetSolid("BrushNavActiveBg", "#E4DEFA");
            SetSolid("BrushBadgeSoft", "#EDE8FA");
            SetSolid("BrushAccentSoft", "#EDE8FA");
            SetSolid("BrushOverlayScrim", "#8815122B");

            SetSolid("BrushBorder", "#DCD8EC");
            SetSolid("BrushBorderSubtle", "#DCD8EC");
            SetSolid("BrushBorderStrong", "#CFC9E4");
            SetSolid("BrushBorderCard", "#D4CEE8");
            SetSolid("BrushBorderGlow", "#BCAEEA");
            SetSolid("BrushBorderAccent", "#7C6AE8");

            SetSolid("BrushTextPrimary", "#1C1A29");
            SetSolid("BrushTextSecondary", "#504D6B");
            SetSolid("BrushTextMuted", "#787494");

            SetSolid("BrushAccent", "#6D5BD0");
            SetSolid("BrushAccentHover", "#5C49BF");
            SetSolid("BrushAccentPressed", "#4D3CA8");
            SetSolid("BrushVioletText", "#5E4BC4");
            SetSolid("BrushOnAccent", "#FFFFFF");

            SetSolid("BrushSuccess", "#1F8E5A");
            SetSolid("BrushSuccessSoft", "#E8F6EE");
            SetSolid("BrushSuccessTint", "#E8F6EE");
            SetSolid("BrushSuccessBorder", "#BFE4CF");

            SetSolid("BrushWarning", "#C47B1B");
            SetSolid("BrushWarningSoft", "#FDF4E5");
            SetSolid("BrushWarningTint", "#FDF4E5");
            SetSolid("BrushWarningBorder", "#EBD3A7");

            SetSolid("BrushDanger", "#C94A58");
            SetSolid("BrushDangerHover", "#BA3847");
            SetSolid("BrushDangerPressed", "#9E2937");
            SetSolid("BrushDangerSoft", "#FBECEF");
            SetSolid("BrushDangerTint", "#FBECEF");
            SetSolid("BrushDangerBorder", "#E9BFC6");

            SetSolid("BrushInfo", "#3A78CC");

            SetSolid("BrushScrollThumb", "#C6C1DC");
            SetSolid("BrushScrollThumbHover", "#8B7CE0");
            SetSolid("BrushScrollTrack", "#ECE9F5");

            SetSolid("BrushHeroRingOuter", "#186D5BD0");
            SetSolid("BrushHeroRingMid", "#286D5BD0");
            SetSolid("BrushHeroRingCore", "#EDE9FA");
        }
    }
}
