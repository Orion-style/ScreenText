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
    private CancellationTokenSource? _pdfLoadCts;
    private bool _isMediaUserSeeking;
    private bool _isClosing;
    private double _zoomFactor = 1.0;
    private double _pdfZoomFactor = 1.0;
    private double _lastPdfViewportWidth;
    private uint _pdfTotalPages;
    private uint _pdfLoadedPageCount;
    private bool _isLoadingPdfBatch;

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
        PreviewKeyDown += OnWindowPreviewKeyDown;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        ApplyInitialWindowBounds();
        AnimateWindowOpen();

        // Активируем окно и даем ему фокус, чтобы оно захватило активность
        Activate();
        Focus();

        // Подписываемся на Deactivated только после полной загрузки и активации
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            Deactivated += OnWindowDeactivated;
        }), System.Windows.Threading.DispatcherPriority.Background);

        await LoadContentAsync();
    }

    private void ApplyInitialWindowBounds()
    {
        var monitorArea = WindowPlacementHelper.GetCurrentTargetMonitorWorkArea(_previousForegroundHwnd);
        var bounds = QuickLookLayoutHelper.ComputeTargetWindowBounds(
            0,
            0,
            monitorArea.Width,
            monitorArea.Height,
            _settings.MaxWindowScreenPercent);

        MaxWidth = bounds.MaxWidth;
        MaxHeight = bounds.MaxHeight;

        WindowPlacementHelper.PositionWindowCentered(this, monitorArea, bounds.Width, bounds.Height);
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
                case QuickLookContentType.Archive:
                    await LoadArchivePreviewAsync();
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

    private async Task LoadImagePreviewAsync()
    {
        try
        {
            var monitorArea = WindowPlacementHelper.GetCurrentTargetMonitorWorkArea(_previousForegroundHwnd);
            int maxDim = Math.Max(monitorArea.PhysicalWidth, monitorArea.PhysicalHeight);
            if (maxDim <= 0)
            {
                maxDim = (int)Math.Max(monitorArea.Width, monitorArea.Height);
            }

            var bitmap = await Task.Run(() => QuickLookImageLoader.LoadImage(_filePath, maxDim));
            if (bitmap != null)
            {
                _zoomFactor = 1.0;
                ImgPreview.Source = bitmap;
                ImageViewer.Visibility = Visibility.Visible;

                TxtHeaderTitle.Text = $"{Title} ({bitmap.PixelWidth} × {bitmap.PixelHeight})";

                // Fit window size to image aspect ratio nicely
                AdjustWindowDimensions(bitmap.PixelWidth, bitmap.PixelHeight, extraHeight: 76);
                UpdateImageDisplaySize();
                return;
            }

            _logger.LogWarning($"[QuickLook] Не удалось декодировать изображение '{_filePath}'. Отображение карточки файла.");
            ShowGenericCard(isFailure: true);
        }
        catch (Exception ex)
        {
            _logger.LogError($"[QuickLook] Ошибка предпросмотра изображения '{_filePath}': {ex.Message}", ex);
            ShowGenericCard(isFailure: true);
        }
    }

    private async Task LoadPdfPreviewAsync()
    {
        try
        {
            _pdfLoadCts?.Cancel();
            _pdfLoadCts?.Dispose();
            _pdfLoadCts = new CancellationTokenSource();
            var ct = _pdfLoadCts.Token;

            _pdfZoomFactor = 1.0;
            _pdfLoadedPageCount = 0;
            _pdfTotalPages = 0;
            _lastPdfViewportWidth = 0;

            using var session = await Task.Run(() => WindowsPdfRenderer.PdfSession.OpenAsync(_filePath), ct);
            if (session != null)
            {
                _pdfTotalPages = session.PageCount;
                if (_pdfTotalPages == 0)
                {
                    ShowGenericCard(isFailure: true);
                    return;
                }

                PdfViewer.Visibility = Visibility.Visible;

                var firstPage = await Task.Run(() => session.RenderPageAsync(0), ct);
                if (firstPage == null || _isClosing || ct.IsCancellationRequested)
                {
                    if (!_isClosing && firstPage == null) ShowGenericCard(isFailure: true);
                    return;
                }

                AddPdfPageImage(firstPage.Value.Bitmap);
                _pdfLoadedPageCount = 1;
                UpdatePdfFooterText();

                AdjustWindowDimensions(firstPage.Value.LogicalWidth, firstPage.Value.LogicalHeight, extraHeight: 102);
                UpdatePdfPagesDisplaySize();

                uint initialBatchEnd = Math.Min(_pdfTotalPages, 20);
                if (initialBatchEnd > 1)
                {
                    await LoadPdfPageRangeWithSessionAsync(session, 1, initialBatchEnd, ct);
                }

                return;
            }

            // Fallback when direct stream open is unavailable
            uint pageCount = await WindowsPdfRenderer.GetPageCountAsync(_filePath);
            if (pageCount == 0)
            {
                ShowGenericCard(isFailure: true);
                return;
            }

            _pdfTotalPages = pageCount;
            PdfViewer.Visibility = Visibility.Visible;

            var fallbackFirstPage = await WindowsPdfRenderer.RenderPageAsync(_filePath, 0);
            if (fallbackFirstPage == null || _isClosing || ct.IsCancellationRequested)
            {
                if (!_isClosing && fallbackFirstPage == null) ShowGenericCard(isFailure: true);
                return;
            }

            AddPdfPageImage(fallbackFirstPage);
            _pdfLoadedPageCount = 1;
            UpdatePdfFooterText();

            AdjustWindowDimensions(fallbackFirstPage.PixelWidth, fallbackFirstPage.PixelHeight, extraHeight: 102);
            UpdatePdfPagesDisplaySize();

            uint maxFallbackPages = Math.Min(pageCount, 20);
            for (uint i = 1; i < maxFallbackPages; i++)
            {
                if (_isClosing || ct.IsCancellationRequested) break;
                var pageBmp = await WindowsPdfRenderer.RenderPageAsync(_filePath, i);
                if (pageBmp != null && !_isClosing && !ct.IsCancellationRequested)
                {
                    AddPdfPageImage(pageBmp);
                    _pdfLoadedPageCount++;
                    UpdatePdfFooterText();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Window closed during loading
        }
        catch (Exception ex)
        {
            _logger.LogError($"[QuickLook] Ошибка предпросмотра PDF '{_filePath}': {ex.Message}", ex);
            ShowGenericCard(isFailure: true);
        }
    }

    private async Task LoadPdfPageRangeWithSessionAsync(
        WindowsPdfRenderer.PdfSession session,
        uint startIndex,
        uint endExclusive,
        CancellationToken ct)
    {
        for (uint i = startIndex; i < endExclusive; i++)
        {
            if (_isClosing || ct.IsCancellationRequested) break;

            uint idx = i;
            var rendered = await Task.Run(() => session.RenderPageAsync(idx), ct);
            if (rendered != null && !_isClosing && !ct.IsCancellationRequested)
            {
                AddPdfPageImage(rendered.Value.Bitmap);
                _pdfLoadedPageCount++;
                UpdatePdfFooterText();
            }
        }
    }

    private void AddPdfPageImage(BitmapSource pageBmp)
    {
        var pageImg = new Image
        {
            Source = pageBmp,
            Margin = new Thickness(0, 0, 0, 16),
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(pageImg, BitmapScalingMode.HighQuality);
        ApplyPdfPageImageSize(pageImg);
        PdfPagesPanel.Children.Add(pageImg);
    }

    private void UpdatePdfFooterText()
    {
        if (_pdfTotalPages == 0)
        {
            TxtPdfPageInfo.Text = string.Empty;
            return;
        }

        TxtPdfPageInfo.Text = _pdfLoadedPageCount < _pdfTotalPages
            ? $"Показано страниц: {_pdfLoadedPageCount} из {_pdfTotalPages}"
            : $"Страниц: {_pdfTotalPages}";
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

    private void AdjustWindowDimensions(double contentWidth, double contentHeight, double extraHeight = 76)
    {
        if (contentWidth <= 0 || contentHeight <= 0) return;

        var monitorArea = WindowPlacementHelper.GetCurrentTargetMonitorWorkArea(_previousForegroundHwnd);
        var bounds = QuickLookLayoutHelper.ComputeTargetWindowBounds(
            contentWidth,
            contentHeight,
            monitorArea.Width,
            monitorArea.Height,
            _settings.MaxWindowScreenPercent,
            extraWidth: 34,
            extraHeight: extraHeight);

        MaxWidth = bounds.MaxWidth;
        MaxHeight = bounds.MaxHeight;

        WindowPlacementHelper.PositionWindowCentered(this, monitorArea, bounds.Width, bounds.Height);
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnImageViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateImageDisplaySize();
    }

    private void OnImageMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            e.Handled = true;
            _zoomFactor = 1.0;
            UpdateImageDisplaySize();
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
            UpdateImageDisplaySize();
        }
    }

    private void UpdateImageDisplaySize()
    {
        if (ImgPreview.Source is not BitmapSource bitmap) return;

        double availW = ImageViewer.ActualWidth;
        double availH = ImageViewer.ActualHeight;
        if (availW <= 0 || availH <= 0) return;

        var size = QuickLookLayoutHelper.ComputeImageDisplaySize(
            bitmap.PixelWidth,
            bitmap.PixelHeight,
            availW,
            availH,
            _zoomFactor);

        if (size.Width <= 0 || size.Height <= 0) return;

        ImgPreview.Width = size.Width;
        ImgPreview.Height = size.Height;

        var visibility = size.NeedsScroll ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        ImageViewer.HorizontalScrollBarVisibility = visibility;
        ImageViewer.VerticalScrollBarVisibility = visibility;
    }

    private void OnPdfScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePdfPagesDisplaySize();
    }

    private async void OnPdfScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        double currentVp = PdfScrollViewer.ViewportWidth;
        if (currentVp > 0 && Math.Abs(_lastPdfViewportWidth - currentVp) > 2.0)
        {
            UpdatePdfPagesDisplaySize();
        }

        if (_isClosing || _isLoadingPdfBatch || _pdfLoadedPageCount >= _pdfTotalPages || _pdfLoadCts == null)
        {
            return;
        }

        if (PdfScrollViewer.ExtentHeight > 0 &&
            PdfScrollViewer.VerticalOffset + PdfScrollViewer.ViewportHeight >= PdfScrollViewer.ExtentHeight - 500)
        {
            _isLoadingPdfBatch = true;
            try
            {
                var ct = _pdfLoadCts.Token;
                using var session = await Task.Run(() => WindowsPdfRenderer.PdfSession.OpenAsync(_filePath), ct);
                if (session != null && !_isClosing && !ct.IsCancellationRequested)
                {
                    uint start = _pdfLoadedPageCount;
                    uint end = Math.Min(_pdfTotalPages, start + 15);
                    await LoadPdfPageRangeWithSessionAsync(session, start, end, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Window closed
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[QuickLook] Не удалось догрузить страницы PDF '{_filePath}': {ex.Message}");
            }
            finally
            {
                _isLoadingPdfBatch = false;
            }
        }
    }

    private void OnPdfMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            if (e.Delta > 0)
            {
                _pdfZoomFactor = Math.Min(3.0, _pdfZoomFactor * 1.15);
            }
            else
            {
                _pdfZoomFactor = Math.Max(0.5, _pdfZoomFactor / 1.15);
            }
            UpdatePdfPagesDisplaySize();
        }
    }

    private double GetPdfAvailablePageWidth()
    {
        double vp = PdfScrollViewer.ViewportWidth > 0
            ? PdfScrollViewer.ViewportWidth
            : PdfScrollViewer.ActualWidth - SystemParameters.VerticalScrollBarWidth;
        return Math.Max(80, vp - 24);
    }

    private void UpdatePdfPagesDisplaySize()
    {
        double availW = GetPdfAvailablePageWidth();
        if (availW <= 0) return;

        _lastPdfViewportWidth = PdfScrollViewer.ViewportWidth;

        bool needsHorizScroll = _pdfZoomFactor > 1.001;
        PdfScrollViewer.HorizontalScrollBarVisibility = needsHorizScroll ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;

        foreach (var child in PdfPagesPanel.Children)
        {
            if (child is Image pageImg)
            {
                ApplyPdfPageImageSize(pageImg, availW);
            }
        }
    }

    private void ApplyPdfPageImageSize(Image pageImg, double? availableWidthOverride = null)
    {
        if (pageImg.Source is not BitmapSource bmp) return;

        double availW = availableWidthOverride ?? GetPdfAvailablePageWidth();
        if (availW <= 0) return;

        var size = QuickLookLayoutHelper.ComputePdfPageDisplaySize(
            bmp.PixelWidth,
            bmp.PixelHeight,
            availW,
            _pdfZoomFactor);

        if (size.Width > 0 && size.Height > 0)
        {
            pageImg.Width = size.Width;
            pageImg.Height = size.Height;
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
        // При клике в любое другое окно/приложение или на рабочий стол закрываем окно
        Dispatcher.BeginInvoke(new Action(() =>
        {
            CloseWindowCleanly();
        }));
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
        else if ((e.Key == Key.D0 || e.Key == Key.NumPad0) && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            if (ImageViewer.Visibility == Visibility.Visible)
            {
                _zoomFactor = 1.0;
                UpdateImageDisplaySize();
            }
            else if (PdfViewer.Visibility == Visibility.Visible)
            {
                _pdfZoomFactor = 1.0;
                UpdatePdfPagesDisplaySize();
            }
        }
    }

    public void CloseWindowCleanly()
    {
        if (_isClosing) return;
        _isClosing = true;

        try
        {
            _pdfLoadCts?.Cancel();
            _pdfLoadCts?.Dispose();
            _pdfLoadCts = null;

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

    private async Task LoadArchivePreviewAsync()
    {
        try
        {
            var entries = await Task.Run(() =>
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(_filePath);
                var list = new List<ArchiveEntryItem>();
                long totalUncompressed = 0;
                long totalCompressed = 0;

                foreach (var entry in archive.Entries)
                {
                    totalUncompressed += entry.Length;
                    totalCompressed += entry.CompressedLength;

                    list.Add(new ArchiveEntryItem
                    {
                        FullName = entry.FullName,
                        FormattedSize = FormatFileSize(entry.Length),
                        FormattedCompressedSize = FormatFileSize(entry.CompressedLength),
                        FormattedDateTime = entry.LastWriteTime.LocalDateTime.ToString("dd.MM.yyyy HH:mm")
                    });
                }

                double ratio = totalUncompressed > 0 ? (1.0 - ((double)totalCompressed / totalUncompressed)) * 100.0 : 0;
                return (Items: list, TotalCount: list.Count, TotalUncompressed: totalUncompressed, TotalCompressed: totalCompressed, Ratio: ratio);
            });

            ListArchiveEntries.ItemsSource = entries.Items;
            TxtArchiveFooterCount.Text = $"Файлов в архиве: {entries.TotalCount} (Исходный: {FormatFileSize(entries.TotalUncompressed)})";
            TxtArchiveFooterRatio.Text = $"Сжатый: {FormatFileSize(entries.TotalCompressed)} (Экономия: {entries.Ratio:F1}%)";

            ArchiveViewer.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _logger.LogError($"[QuickLook] Ошибка чтения архива '{_filePath}': {ex.Message}", ex);
            ShowGenericCard(isFailure: true);
        }
    }

    public sealed class ArchiveEntryItem
    {
        public string FullName { get; init; } = string.Empty;
        public string FormattedSize { get; init; } = string.Empty;
        public string FormattedCompressedSize { get; init; } = string.Empty;
        public string FormattedDateTime { get; init; } = string.Empty;
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
