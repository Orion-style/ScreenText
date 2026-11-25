using System.Drawing;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Xunit;
using ZenithApp.Core.Hotkeys;
using ZenithApp.Core.Settings;
using ZenithApp.Modules.Screenshot;

namespace ZenithApp.Tests;

public class TestHotkeyService : IHotkeyService
{
    public Dictionary<string, Action> RegisteredHotkeys { get; } = new();

    public bool RegisterHotkey(string id, KeyModifiers modifiers, Key key, Action action)
    {
        RegisteredHotkeys[id] = action;
        return true;
    }

    public bool UnregisterHotkey(string id)
    {
        return RegisteredHotkeys.Remove(id);
    }

    public void UnregisterAll()
    {
        RegisteredHotkeys.Clear();
    }

    public void Trigger(string id)
    {
        if (RegisteredHotkeys.TryGetValue(id, out var action))
        {
            action();
        }
    }

    public void Dispose() => RegisteredHotkeys.Clear();
}

public class ScreenshotModuleTests
{
    [Fact]
    public void ScreenshotSettings_DefaultValues_AreProperlyConfigured()
    {
        var settings = new ScreenshotSettings();

        // По умолчанию запоминание последней области включено
        Assert.True(settings.RememberLastArea);
        Assert.Equal(KeyModifiers.Control | KeyModifiers.Shift, settings.ScreenshotModifiers);
        Assert.Equal(Key.S, settings.ScreenshotKey);

        // По умолчанию круглая лупа с сеткой пикселей включена
        Assert.True(settings.MagnifierEnabled);
        Assert.Equal(8, settings.MagnifierZoom);
        Assert.Equal(11, settings.MagnifierGridSize);
    }

    [Fact]
    public void ScreenshotSettings_JsonSerialization_PreservesMagnifierAndAreaSettings()
    {
        var original = new ScreenshotSettings
        {
            RememberLastArea = false,
            MagnifierEnabled = false,
            MagnifierZoom = 14,
            MagnifierGridSize = 15,
            SaveToFileEnabled = true,
            SaveFolderPath = @"C:\Screenshots"
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<ScreenshotSettings>(json);

        Assert.NotNull(deserialized);
        Assert.False(deserialized!.RememberLastArea);
        Assert.False(deserialized.MagnifierEnabled);
        Assert.Equal(14, deserialized.MagnifierZoom);
        Assert.Equal(15, deserialized.MagnifierGridSize);
        Assert.True(deserialized.SaveToFileEnabled);
        Assert.Equal(@"C:\Screenshots", deserialized.SaveFolderPath);
    }

    [Fact]
    public void ScreenshotModule_SavedArea_Management_WorksCorrectly()
    {
        var logger = new TestLogger();
        var settingsService = new TestSettingsService();
        var hotkeyService = new TestHotkeyService();
        var historyService = new OcrHistoryService();
        var handlers = Array.Empty<ITextRecognizedHandler>();

        using var module = new ScreenshotModule(logger, settingsService, hotkeyService, handlers, historyService);

        Assert.False(module.HasSavedArea);
        Assert.Null(module.SavedCropArea);

        int eventFiredCount = 0;
        module.SavedAreaChanged += () => eventFiredCount++;

        // 1. Установка сохранённой области
        var targetArea = new Rectangle(100, 150, 400, 300);
        module.SetSavedArea(targetArea);

        Assert.True(module.HasSavedArea);
        Assert.Equal(targetArea, module.SavedCropArea);
        Assert.Equal(1, eventFiredCount);

        // 2. Сброс сохранённой области
        module.ResetSavedArea(notifyUser: false);

        Assert.False(module.HasSavedArea);
        Assert.Null(module.SavedCropArea);
        Assert.Equal(2, eventFiredCount);
    }

    [Fact]
    public void ScreenshotSettingsViewModel_RememberAreaProperties_SyncAndPersist()
    {
        var logger = new TestLogger();
        var settingsService = new TestSettingsService();
        var hotkeyService = new TestHotkeyService();
        var historyService = new OcrHistoryService();
        var handlers = Array.Empty<ITextRecognizedHandler>();

        using var module = new ScreenshotModule(logger, settingsService, hotkeyService, handlers, historyService);
        using var vm = new ScreenshotSettingsViewModel(module, settingsService);

        Assert.True(vm.RememberLastArea);
        Assert.False(vm.HasSavedArea);
        Assert.Contains("Область не сохранена", vm.SavedAreaStatusText);

        // Устанавливаем область в модуле и проверяем обновление статуса в VM
        module.SetSavedArea(new Rectangle(50, 60, 320, 240));
        Assert.True(vm.HasSavedArea);
        Assert.Contains("320×240", vm.SavedAreaStatusText);

        // Сброс через команду во ViewModel
        vm.ResetSavedAreaCommand.Execute(null);
        Assert.False(vm.HasSavedArea);
        Assert.False(module.HasSavedArea);

        // Отключение запоминания сбрасывает область и сохраняет настройку
        module.SetSavedArea(new Rectangle(10, 20, 200, 100));
        vm.RememberLastArea = false;
        Assert.False(settingsService.Current.Screenshot.RememberLastArea);
        Assert.True(settingsService.Saved);
        Assert.False(module.HasSavedArea);
    }

    [Fact]
    public void SelectionOverlayWindow_MoveAreaPreservingSize_NeverChangesProportions()
    {
        var original = new Rectangle(100, 120, 350, 220);

        // Обычное перемещение
        var moved = SelectionOverlayWindow.MoveAreaPreservingSize(original, 150, -40, 1920, 1080);
        Assert.Equal(250, moved.X);
        Assert.Equal(80, moved.Y);
        Assert.Equal(350, moved.Width);
        Assert.Equal(220, moved.Height);

        // Попытка уйти за левый/верхний край
        var clampedTopLeft = SelectionOverlayWindow.MoveAreaPreservingSize(original, -500, -500, 1920, 1080);
        Assert.Equal(0, clampedTopLeft.X);
        Assert.Equal(0, clampedTopLeft.Y);
        Assert.Equal(350, clampedTopLeft.Width);
        Assert.Equal(220, clampedTopLeft.Height);

        // Попытка уйти за правый/нижний край — ширина и высота остаются неизменными
        var clampedBottomRight = SelectionOverlayWindow.MoveAreaPreservingSize(original, 5000, 5000, 1920, 1080);
        Assert.Equal(1920 - 350, clampedBottomRight.X);
        Assert.Equal(1080 - 220, clampedBottomRight.Y);
        Assert.Equal(350, clampedBottomRight.Width);
        Assert.Equal(220, clampedBottomRight.Height);
    }

    [Fact]
    public void SelectionOverlayWindow_ClampSavedAreaToBounds_HandlesEdgeCases()
    {
        Assert.Null(SelectionOverlayWindow.ClampSavedAreaToBounds(null, 1920, 1080));
        Assert.Null(SelectionOverlayWindow.ClampSavedAreaToBounds(new Rectangle(0, 0, 2, 200), 1920, 1080));
        Assert.Null(SelectionOverlayWindow.ClampSavedAreaToBounds(new Rectangle(0, 0, 2500, 500), 1920, 1080));

        var clamped = SelectionOverlayWindow.ClampSavedAreaToBounds(new Rectangle(1800, 950, 300, 200), 1920, 1080);
        Assert.NotNull(clamped);
        Assert.Equal(1620, clamped!.Value.X);
        Assert.Equal(880, clamped.Value.Y);
        Assert.Equal(300, clamped.Value.Width);
        Assert.Equal(200, clamped.Value.Height);
    }

    [Fact]
    public void CropBitmap_ClampsOutOfBoundsProperly()
    {
        using var source = new Bitmap(200, 100);

        // Полностью внутри
        using var crop1 = ScreenCaptureService.CropBitmap(source, new Rectangle(10, 10, 50, 40));
        Assert.Equal(50, crop1.Width);
        Assert.Equal(40, crop1.Height);

        // Частично выходящая за пределы
        using var crop2 = ScreenCaptureService.CropBitmap(source, new Rectangle(180, 80, 50, 50));
        Assert.Equal(20, crop2.Width);
        Assert.Equal(20, crop2.Height);

        // Полностью снаружи (должен вернуть пустой 1x1 без исключений)
        using var crop3 = ScreenCaptureService.CropBitmap(source, new Rectangle(300, 300, 50, 50));
        Assert.Equal(1, crop3.Width);
        Assert.Equal(1, crop3.Height);
    }

    [Fact]
    public void ScreenshotSettingsViewModel_MagnifierProperties_SyncAndPersist()
    {
        var logger = new TestLogger();
        var settingsService = new TestSettingsService();
        var hotkeyService = new TestHotkeyService();
        var historyService = new OcrHistoryService();
        var handlers = Array.Empty<ITextRecognizedHandler>();

        using var module = new ScreenshotModule(logger, settingsService, hotkeyService, handlers, historyService);
        using var vm = new ScreenshotSettingsViewModel(module, settingsService);

        // Начальные значения
        Assert.True(vm.MagnifierEnabled);
        Assert.Equal(8, vm.MagnifierZoom);
        Assert.Equal(11, vm.MagnifierGridSize);

        // Изменение MagnifierEnabled
        vm.MagnifierEnabled = false;
        Assert.False(settingsService.Current.Screenshot.MagnifierEnabled);
        Assert.True(settingsService.Saved);

        // Изменение MagnifierZoom и ограничение диапазона (2-24)
        vm.MagnifierZoom = 16;
        Assert.Equal(16, settingsService.Current.Screenshot.MagnifierZoom);

        vm.MagnifierZoom = 50; // Должно ограничиться 24
        Assert.Equal(24, vm.MagnifierZoom);
        Assert.Equal(24, settingsService.Current.Screenshot.MagnifierZoom);

        vm.MagnifierZoom = 1; // Должно ограничиться 2
        Assert.Equal(2, vm.MagnifierZoom);
        Assert.Equal(2, settingsService.Current.Screenshot.MagnifierZoom);

        // Изменение размера сетки (только нечетные и в диапазоне 5-25)
        vm.MagnifierGridSize = 13;
        Assert.Equal(13, settingsService.Current.Screenshot.MagnifierGridSize);

        vm.MagnifierGridSize = 12; // Четное число округляется до 13
        Assert.Equal(13, vm.MagnifierGridSize);
        Assert.Equal(13, settingsService.Current.Screenshot.MagnifierGridSize);
    }

    [Fact]
    public void ScreenshotMagnifier_ZoomAndGridClamping_AndCircularGeometry_WorkCorrectly()
    {
        var thread = new Thread(() =>
        {
            var magnifier = new ScreenshotMagnifier(initialZoom: 8, initialGridSize: 11);
            Assert.Equal(8, magnifier.Zoom);
            Assert.Equal(11, magnifier.GridSize);

            // Лупа содержит только чистый элемент сетки без текстовых плашек
            magnifier.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.Equal(magnifier.DesiredSize.Width, magnifier.DesiredSize.Height);
            Assert.True(magnifier.Child is FrameworkElement);

            // Регулировка зума
            magnifier.AdjustZoom(+2);
            Assert.Equal(10, magnifier.Zoom);

            magnifier.AdjustZoom(-5);
            Assert.Equal(5, magnifier.Zoom);

            // Ограничения
            magnifier.Zoom = 100;
            Assert.Equal(24, magnifier.Zoom);

            magnifier.Zoom = 0;
            Assert.Equal(2, magnifier.Zoom);

            // Сетка - нечетные размеры
            magnifier.GridSize = 10;
            Assert.Equal(11, magnifier.GridSize);

            magnifier.GridSize = 30;
            Assert.Equal(25, magnifier.GridSize);

            magnifier.GridSize = 2;
            Assert.Equal(5, magnifier.GridSize);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void ScreenshotMagnifier_PixelSampling_DoesNotThrowNearScreenBorders()
    {
        var thread = new Thread(() =>
        {
            using var bmp = new Bitmap(100, 100);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Blue);
            bmp.SetPixel(0, 0, Color.Red);
            bmp.SetPixel(99, 99, Color.Green);

            var magnifier = new ScreenshotMagnifier(8, 11);

            // Выборка в левом верхнем углу (0,0) - пиксели вне границ не должны вызывать исключений
            magnifier.Update(bmp, 0, 0, new System.Windows.Point(0, 0), 100, 100);

            // Выборка в правом нижнем углу (99,99)
            magnifier.Update(bmp, 99, 99, new System.Windows.Point(99, 99), 100, 100);

            // Выборка за пределами изображения
            magnifier.Update(bmp, -10, -10, new System.Windows.Point(0, 0), 100, 100);
            magnifier.Update(bmp, 200, 200, new System.Windows.Point(100, 100), 100, 100);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}
