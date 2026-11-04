using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZenithApp.Core.Logging;

namespace ZenithApp.Modules.QuickLook;

public partial class QuickLookWindow : Window
{
    private readonly string _filePath;
    private readonly QuickLookSettings _settings;
    private readonly IAppLogger _logger;
    private readonly nint _previousForegroundHwnd;

    private DispatcherTimer? _mediaTimer;
    private bool _isMediaUserSeeking;
    private bool _isClosing;
    private double _zoomFactor = 1.0;

    public QuickLookWindow(string filePath, QuickLookSettings settings, IAppLogger logger, nint previousForegroundHwnd)
    {
        InitializeComponent();
        _filePath = filePath;
        _settings = settings;
        _logger = logger;
        _previousForegroundHwnd = previousForegroundHwnd;

        Title = Path.GetFileName(filePath);
        TxtHeaderTitle.Text = Title;
        ImgHeaderIcon.Source = QuickLookIconHelper.GetFileIcon(filePath, 32);

        Loaded += OnWindowLoaded;
        Deactivated += OnWindowDeactivated;
        PreviewKeyDown += OnWindowPreviewKeyDown;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        ApplyInitialWindowBounds();
        AnimateWindowOpen();
        await LoadContentAsync();
    }

    private void ApplyInitialWindowBounds()
    {
        var monitorArea = WindowPlacementHelper.GetCurrentTargetMonitorWorkArea();
        double percent = Math.Clamp(_settings.MaxWindowScreenPercent, 40, 95) / 100.0;
        double maxW = monitorArea.Width * percent;
        double maxH = monitorArea.Height * percent;

        MaxWidth = maxW;
        MaxHeight = maxH;

        // Default comfortable window size
        double targetW = Math.Min(900, maxW);
        double targetH = Math.Min(650, maxH);

        Width = targetW;
        Height = targetH;

        Left = monitorArea.Left + (monitorArea.Width - targetW) / 2.0;
        Top = monitorArea.Top + (monitorArea.Height - targetH) / 2.0;
    }

    private void AnimateWindowOpen()
    {
        Opacity = 0;
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, anim);
    }

    private async Task LoadContentAsync()
    {
        LoadingBar.Visibility = Visibility.Visible;
        try
        {
            if (!File.Exists(_filePath))
            {
                ShowGenericCard(isFailure: true);
                return;
            }

            var contentType = ContentDetector.DetectContentType(_filePath, _settings);
            switch (contentType)
            {
                case QuickLookContentType.Image:
                    await LoadImagePreviewAsync();
                    break;
                case QuickLookContentType.Pdf:
                    await LoadPdfPreviewAsync();
                    break;
                case QuickLookContentType.Video:
                    LoadMediaPreview(isVideo: true);
                    break;
                case QuickLookContentType.Audio:
                    LoadMediaPreview(isVideo: false);
                    break;
                case QuickLookContentType.Text:
                    await LoadTextPreviewAsync();
                    break;
                default:
                    ShowGenericCard(isFailure: false);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"[QuickLook] Ошибка загрузки контента '{_filePath}': {ex.Message}", ex);
            ShowGenericCard(isFailure: true);
        }
        finally
        {
            LoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    private Task LoadImagePreviewAsync()
    {
        try
        {
            var bitmap = new BitmapImage();
            using (var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }
            bitmap.Freeze();

            ImgPreview.Source = bitmap;
            ImageViewer.Visibility = Visibility.Visible;

            // Fit window size to image aspect ratio nicely
            AdjustWindowDimensions(bitmap.PixelWidth, bitmap.PixelHeight);
        }
        catch
        {
            ShowGenericCard(isFailure: true);
        }
        return Task.CompletedTask;
    }

    private async Task LoadPdfPreviewAsync()
    {
        try
        {
            uint pageCount = await WindowsPdfRenderer.GetPageCountAsync(_filePath);
            if (pageCount == 0)
            {
                ShowGenericCard(isFailure: true);
                return;
            }

            TxtPdfPageInfo.Text = $"Страниц: {pageCount}";
            PdfViewer.Visibility = Visibility.Visible;

            // Render first page immediately
            var firstPage = await WindowsPdfRenderer.RenderPageAsync(_filePath, 0);
            if (firstPage != null)
            {
                var img = new Image
                {
                    Source = firstPage,
                    Margin = new Thickness(0, 0, 0, 16),
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                PdfPagesPanel.Children.Add(img);

                AdjustWindowDimensions(firstPage.PixelWidth, firstPage.PixelHeight);
            }

            // Lazy load remaining pages (up to 20 for preview bounds)
            uint maxPagesToRender = Math.Min(pageCount, 25);
            for (uint i = 1; i < maxPagesToRender; i++)
            {
                uint idx = i;
                _ = Task.Run(async () =>
                {
                    var pageBmp = await WindowsPdfRenderer.RenderPageAsync(_filePath, idx);
                    if (pageBmp != null)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (_isClosing) return;
                            var pageImg = new Image
                            {
                                Source = pageBmp,
                                Margin = new Thickness(0, 0, 0, 16),
                                Stretch = Stretch.Uniform,
                                HorizontalAlignment = HorizontalAlignment.Center
                            };
                            PdfPagesPanel.Children.Add(pageImg);
                        });
                    }
                });
            }
        }
        catch
        {
            ShowGenericCard(isFailure: true);
        }
    }

    private void LoadMediaPreview(bool isVideo)
    {
        MediaViewer.Visibility = Visibility.Visible;
        if (!isVideo)
        {
            AudioInfoPanel.Visibility = Visibility.Visible;
            ImgAudioCover.Source = QuickLookIconHelper.GetFileIcon(_filePath, 128);
            TxtAudioTitle.Text = Path.GetFileName(_filePath);
        }

        Player.Source = new Uri(_filePath);

        _mediaTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _mediaTimer.Tick += (s, e) =>
        {
            if (!_isMediaUserSeeking && Player.NaturalDuration.HasTimeSpan)
            {
                SliderProgress.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
                SliderProgress.Value = Player.Position.TotalSeconds;
                TxtMediaTime.Text = $"{Player.Position:mm\\:ss} / {Player.NaturalDuration.TimeSpan:mm\\:ss}";
            }
        };
        _mediaTimer.Start();

        if (_settings.AutoPlayMedia)
        {
            Player.Play();
            BtnPlayPause.Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Pause20);
        }
        else
        {
            BtnPlayPause.Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Play20);
        }

        Player.MediaOpened += (s, e) =>
        {
            if (isVideo && Player.NaturalVideoWidth > 0 && Player.NaturalVideoHeight > 0)
            {
                AdjustWindowDimensions(Player.NaturalVideoWidth, Player.NaturalVideoHeight);
            }
        };

        Player.MediaEnded += (s, e) =>
        {
            Player.Stop();
            BtnPlayPause.Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Play20);
        };
    }

    private async Task LoadTextPreviewAsync()
    {
        try
        {
            int maxBytes = Math.Max(1024, _settings.MaxTextFileSizeKb * 1024);
            var result = await TextFileReader.ReadTextPreviewAsync(_filePath, maxBytes);

            TxtContent.Text = result.Content;
            TxtTextFooterEncoding.Text = $"Кодировка: {result.EncodingName}  |  Размер: {FormatFileSize(result.OriginalSizeBytes)}";
            TxtTextFooterTruncated.Visibility = result.IsTruncated ? Visibility.Visible : Visibility.Collapsed;

            TextViewer.Visibility = Visibility.Visible;
        }
        catch
        {
            ShowGenericCard(isFailure: true);
        }
    }

    private void ShowGenericCard(bool isFailure)
    {
        GenericCardViewer.Visibility = Visibility.Visible;
        ImgGenericIcon.Source = QuickLookIconHelper.GetFileIcon(_filePath, 128);
        TxtGenericFileName.Text = Path.GetFileName(_filePath);

        try
        {
            var fi = new FileInfo(_filePath);
            string ext = fi.Extension.ToUpperInvariant();
            TxtGenericDetails.Text = $"Тип: {(string.IsNullOrEmpty(ext) ? "Файл" : ext)}  •  Размер: {FormatFileSize(fi.Length)}\nИзменён: {fi.LastWriteTime:dd.MM.yyyy HH:mm}";
        }
        catch
        {
            TxtGenericDetails.Text = "Сведения о файле недоступны";
        }

        TxtGenericStatus.Visibility = isFailure ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AdjustWindowDimensions(double contentWidth, double contentHeight)
    {
        if (contentWidth <= 0 || contentHeight <= 0) return;

        var monitorArea = WindowPlacementHelper.GetCurrentTargetMonitorWorkArea();
        double percent = Math.Clamp(_settings.MaxWindowScreenPercent, 40, 95) / 100.0;
        double maxW = monitorArea.Width * percent;
        double maxH = monitorArea.Height * percent;

        // Header height + padding allowance
        double extraHeight = 70;
        double targetW = contentWidth + 32;
        double targetH = contentHeight + extraHeight;

        double scale = Math.Min(1.0, Math.Min(maxW / targetW, maxH / targetH));
        targetW = Math.Max(500, targetW * scale);
        targetH = Math.Max(350, targetH * scale);

        Width = targetW;
        Height = targetH;

        Left = monitorArea.Left + (monitorArea.Width - targetW) / 2.0;
        Top = monitorArea.Top + (monitorArea.Height - targetH) / 2.0;
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnImageMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            if (e.Delta > 0)
            {
                _zoomFactor = Math.Min(5.0, _zoomFactor * 1.15);
            }
            else
            {
                _zoomFactor = Math.Max(0.2, _zoomFactor / 1.15);
            }
            ImgScaleTransform.ScaleX = _zoomFactor;
            ImgScaleTransform.ScaleY = _zoomFactor;
        }
    }

    private void OnPlayerMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        TogglePlayPause();
    }

    private void OnPlayPauseClicked(object sender, RoutedEventArgs e)
    {
        TogglePlayPause();
    }

    private void TogglePlayPause()
    {
        if (Player.Source == null) return;

        if (BtnPlayPause.Icon is Wpf.Ui.Controls.SymbolIcon sym && sym.Symbol == Wpf.Ui.Controls.SymbolRegular.Pause20)
        {
            Player.Pause();
            BtnPlayPause.Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Play20);
        }
        else
        {
            Player.Play();
            BtnPlayPause.Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Pause20);
        }
    }

    private void OnSliderProgressMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isMediaUserSeeking = true;
    }

    private void OnSliderProgressMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isMediaUserSeeking = false;
        Player.Position = TimeSpan.FromSeconds(SliderProgress.Value);
    }

    private void OnSliderProgressValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isMediaUserSeeking)
        {
            TxtMediaTime.Text = $"{TimeSpan.FromSeconds(SliderProgress.Value):mm\\:ss} / {Player.NaturalDuration.TimeSpan:mm\\:ss}";
        }
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Player != null)
        {
            Player.Volume = SliderVolume.Value;
        }
    }

    private void OnOpenDefaultClicked(object sender, RoutedEventArgs e)
    {
        OpenFileInDefaultApp();
    }

    private void OpenFileInDefaultApp()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"[QuickLook] Ошибка открытия в программе по умолчанию '{_filePath}': {ex.Message}", ex);
        }
        finally
        {
            CloseWindowCleanly();
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        CloseWindowCleanly();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        // Close when clicking outside of the preview window
        CloseWindowCleanly();
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space || e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseWindowCleanly();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            OpenFileInDefaultApp();
        }
    }

    public void CloseWindowCleanly()
    {
        if (_isClosing) return;
        _isClosing = true;

        try
        {
            if (_mediaTimer != null)
            {
                _mediaTimer.Stop();
                _mediaTimer = null;
            }

            if (Player.Source != null)
            {
                Player.Stop();
                Player.Close();
                Player.Source = null;
            }

            ImgPreview.Source = null;
            PdfPagesPanel.Children.Clear();
            ImgGenericIcon.Source = null;
            ImgHeaderIcon.Source = null;
            ImgAudioCover.Source = null;

            // Restore focus back to original Explorer/Desktop window
            if (_previousForegroundHwnd != nint.Zero)
            {
                WindowPlacementHelper.SetForegroundWindow(_previousForegroundHwnd);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"[QuickLook] Ошибка при очистке ресурсов окна: {ex.Message}", ex);
        }
        finally
        {
            Close();
            // Free working set
            GC.Collect();
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}
