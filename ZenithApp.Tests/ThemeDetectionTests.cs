using Xunit;
using ZenithApp.Core.Settings;
using ZenithApp.UI.ViewModels;

namespace ZenithApp.Tests;

public class ThemeDetectionTests
{
    [Fact]
    public void IsSystemDarkTheme_DoesNotThrow()
    {
        // Should execute cleanly on any Windows system
        var exception = Record.Exception(() =>
        {
            bool isDark = SettingsViewModel.IsSystemDarkTheme();
            Assert.True(isDark || !isDark);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void AppThemeMode_EnumValues_MatchExpectedIndices()
    {
        Assert.Equal(0, (int)AppThemeMode.System);
        Assert.Equal(1, (int)AppThemeMode.Light);
        Assert.Equal(2, (int)AppThemeMode.Dark);
    }
}
