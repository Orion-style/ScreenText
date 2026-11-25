using System.Drawing;
using System.Text.Json;
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

        // По умолчанию для удобства пользователя двухшаговый режим включен:
        // 1-й вызов — выбор области, 2-й вызов — мгновенный снимок
        Assert.True(settings.TwoStepCaptureEnabled);
        Assert.True(settings.CaptureOnFirstCall);
        Assert.True(settings.TwoStepResetAfterCapture);
        Assert.Equal(KeyModifiers.Control | KeyModifiers.Shift, settings.ScreenshotModifiers);
        Assert.Equal(Key.S, settings.ScreenshotKey);

        // По умолчанию лупа с сеткой пикселей включена
        Assert.True(settings.MagnifierEnabled);
        Assert.Equal(8, settings.MagnifierZoom);
        Assert.Equal(11, settings.MagnifierGridSize);
        Assert.True(settings.MagnifierShowColorInfo);
    }

    [Fact]
    public void ScreenshotSettings_JsonSerialization_PreservesMagnifierSettings()
    {
        var original = new ScreenshotSettings
        {
            MagnifierEnabled = false,
            MagnifierZoom = 14,
            MagnifierGridSize = 15,
            MagnifierShowColorInfo = false
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<ScreenshotSettings>(json);

        Assert.NotNull(deserialized);
        Assert.False(deserialized!.MagnifierEnabled);
        Assert.Equal(14, deserialized.MagnifierZoom);
        Assert.Equal(15, deserialized.MagnifierGridSize);
        Assert.False(deserialized.MagnifierShowColorInfo);
    }

    [Fact]
    public void ScreenshotSettings_JsonSerialization_PreservesAllTwoStepSettings()
    {
        var original = new ScreenshotSettings
        {
            TwoStepCaptureEnabled = true,
            CaptureOnFirstCall = false,
            TwoStepResetAfterCapture = false,
            SaveToFileEnabled = true,
            SaveFolderPath = @"C:\Screenshots"
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<ScreenshotSettings>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized!.TwoStepCaptureEnabled);
        Assert.False(deserialized.CaptureOnFirstCall);
        Assert.False(deserialized.TwoStepResetAfterCapture);
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

        // 1. Установка сохраненной области (как после 1-го вызова)
        var targetArea = new Rectangle(100, 150, 400, 300);
        module.SetSavedArea(targetArea);

        Assert.True(module.HasSavedArea);
        Assert.Equal(targetArea, module.SavedCropArea);
        Assert.Equal(1, eventFiredCount);

        // 2. Сброс сохраненной области
        module.ResetSavedArea(notifyUser: false);

        Assert.False(module.HasSavedArea);
        Assert.Null(module.SavedCropArea);
        Assert.Equal(2, eventFiredCount);
    }

    [Fact]
    public void ScreenshotSettingsViewModel_TwoStepProperties_SyncAndPersist()
    {
        var logger = new TestLogger();
        var settingsService = new TestSettingsService();
        var hotkeyService = new TestHotkeyService();
        var historyService = new OcrHistoryService();
        var handlers = Array.Empty<ITextRecognizedHandler>();

        using var module = new ScreenshotModule(logger, settingsService, hotkeyService, handlers, historyService);
        using var vm = new ScreenshotSettingsViewModel(module, settingsService);

        // Проверяем начальные значения из settings
        Assert.True(vm.TwoStepCaptureEnabled);
        Assert.True(vm.CaptureOnFirstCall);
        Assert.True(vm.TwoStepResetAfterCapture);
        Assert.False(vm.HasSavedArea);
        Assert.Contains("Область не сохранена", vm.SavedAreaStatusText);

        // Изменяем CaptureOnFirstCall
        vm.CaptureOnFirstCall = false;
        Assert.False(settingsService.Current.Screenshot.CaptureOnFirstCall);
        Assert.True(settingsService.Saved);

        // Изменяем TwoStepResetAfterCapture
        vm.TwoStepResetAfterCapture = false;
        Assert.False(settingsService.Current.Screenshot.TwoStepResetAfterCapture);

        // Устанавливаем область в модуле и проверяем обновление статуса в VM
        module.SetSavedArea(new Rectangle(50, 60, 320, 240));
        Assert.True(vm.HasSavedArea);
        Assert.Contains("320×240", vm.SavedAreaStatusText);

        // Сброс через команду во ViewModel
        vm.ResetSavedAreaCommand.Execute(null);
        Assert.False(vm.HasSavedArea);
        Assert.False(module.HasSavedArea);
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
        Assert.True(vm.MagnifierShowColorInfo);

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

        // Изменение ShowColorInfo
        vm.MagnifierShowColorInfo = false;
        Assert.False(settingsService.Current.Screenshot.MagnifierShowColorInfo);
    }

    [Fact]
    public void ScreenshotMagnifier_ZoomAndGridClamping_WorksCorrectly()
    {
        var thread = new Thread(() =>
        {
            var magnifier = new ScreenshotMagnifier(initialZoom: 8, initialGridSize: 11, showColorInfo: true);
            Assert.Equal(8, magnifier.Zoom);
            Assert.Equal(11, magnifier.GridSize);
            Assert.True(magnifier.ShowColorInfo);

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

            var magnifier = new ScreenshotMagnifier(8, 11, true);

            // Выборка в левом верхнем углу (0,0) - пиксели вне границ не должны вызывать исключений
            magnifier.Update(bmp, 0, 0, new System.Windows.Point(0, 0), 100, 100);

            // Выборка в правом нижнем углу (99,99) с активным выделением
            magnifier.Update(bmp, 99, 99, new System.Windows.Point(99, 99), 100, 100, 50, 40);

            // Выборка за пределами изображения
            magnifier.Update(bmp, -10, -10, new System.Windows.Point(0, 0), 100, 100);
            magnifier.Update(bmp, 200, 200, new System.Windows.Point(100, 100), 100, 100);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}
