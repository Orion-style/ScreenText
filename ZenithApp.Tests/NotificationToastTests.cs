using Xunit;
using ZenithApp.Modules.Screenshot;

namespace ZenithApp.Tests;

public class NotificationToastTests
{
    [Fact]
    public void AppUserModelId_IsCorrectlyConfigured()
    {
        Assert.Equal("Zenith.Utility", NotificationToast.AppUserModelId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Show_EmptyOrWhitespaceMessage_DoesNotThrow(string? message)
    {
        var ex = Record.Exception(() => NotificationToast.Show(message!, "Title", ToastNotificationType.Info));
        Assert.Null(ex);
    }

    [Fact]
    public void Show_ValidMessage_DoesNotThrow()
    {
        var ex = Record.Exception(() => NotificationToast.Show("Снимок сохранён", "Скриншот", ToastNotificationType.Success));
        Assert.Null(ex);
    }
}
