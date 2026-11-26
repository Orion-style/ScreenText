using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace ZenithApp.Modules.StickyNotes;

public partial class StickyNoteWindow : Window
{
    private readonly StickyNoteItem _item;
    private readonly Action<StickyNoteItem> _onChanged;
    private readonly Action<Guid> _onDeleteRequested;
    private readonly Action<Guid> _onHideRequested;

    private StickyNoteHeaderOverlay? _clickThroughHeader;
    private bool _isApplyingModel;
    private bool _isClosingCleanly;

    public StickyNoteWindow(
        StickyNoteItem item,
        Action<StickyNoteItem> onChanged,
        Action<Guid> onDeleteRequested,
        Action<Guid> onHideRequested)
    {
        InitializeComponent();
        _item = item;
        _onChanged = onChanged;
        _onDeleteRequested = onDeleteRequested;
        _onHideRequested = onHideRequested;

        BuildColorSwatches();
        ApplyModelToUi();

        Loaded += OnWindowLoaded;
        LocationChanged += OnWindowLocationChanged;
        Closed += OnWindowClosed;
    }

    public Guid NoteId => _item.Id;

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowMode();
    }

    public void RefreshFromModel()
    {
        ApplyModelToUi();
        if (IsLoaded)
        {
            ApplyWindowMode();
        }
    }

    public void CloseCleanly()
    {
        _isClosingCleanly = true;
        CloseHeaderOverlay();
        Close();
    }

    private void ApplyModelToUi()
    {
        _isApplyingModel = true;
        try
        {
            var clamped = StickyNoteBoundsHelper.ClampToVirtualScreen(
                _item.Left,
                _item.Top,
                _item.Width,
                _item.Height,
                StickyNoteBoundsHelper.GetCurrentVirtualScreen());

            _item.Left = clamped.Left;
            _item.Top = clamped.Top;
            _item.Width = clamped.Width;
            _item.Height = clamped.Height;

            Left = _item.Left;
            Top = _item.Top;
            Width = _item.Width;
            Height = _item.IsCollapsed ? StickyNoteItem.HeaderHeight : _item.Height;
            Topmost = _item.IsPinnedTopmost;

            if (NoteTextBox.Text != _item.Content)
            {
                NoteTextBox.Text = _item.Content;
            }

            NoteTextBox.FontSize = Math.Clamp(_item.FontSize, 10, 28);
            TxtPlaceholder.FontSize = NoteTextBox.FontSize;
            TxtPlaceholder.Visibility = string.IsNullOrEmpty(_item.Content) && !_item.IsCollapsed
                ? Visibility.Visible
                : Visibility.Collapsed;

            BodyGrid.Visibility = _item.IsCollapsed ? Visibility.Collapsed : Visibility.Visible;
            HeaderBorder.CornerRadius = _item.IsCollapsed ? new CornerRadius(7) : new CornerRadius(7, 7, 0, 0);

            if (_item.IsCollapsed)
            {
                var firstLine = (_item.Content ?? string.Empty)
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Trim();
                TxtCollapsedPreview.Text = string.IsNullOrEmpty(firstLine)
                    ? "Пустой стикер"
                    : (firstLine.Length > 36 ? firstLine[..36] + "..." : firstLine);
                TxtCollapsedPreview.Visibility = Visibility.Visible;
            }
            else
            {
                TxtCollapsedPreview.Visibility = Visibility.Collapsed;
            }

            ApplyThemeColors();
        }
        finally
        {
            _isApplyingModel = false;
        }
    }

    private void ApplyThemeColors()
    {
        var theme = StickyNotePalette.Resolve(_item.ColorHex);
        var opacity = Math.Clamp(_item.Opacity, 0.35, 1.0);
        byte bgAlpha = (byte)Math.Clamp((int)Math.Round(opacity * 255), 89, 255);
        byte headerAlpha = (byte)Math.Clamp((int)Math.Round(Math.Min(1.0, opacity + 0.06) * 255), 110, 255);

        var bgBrush = StickyNotePalette.CreateFrozenBrush(theme.BackgroundHex, bgAlpha);
        var headerBrush = StickyNotePalette.CreateFrozenBrush(theme.HeaderHex, headerAlpha);
        var borderBrush = StickyNotePalette.CreateFrozenBrush(theme.BorderHex);
        var fgBrush = StickyNotePalette.CreateFrozenBrush(theme.ForegroundHex);
        var mutedBrush = StickyNotePalette.CreateFrozenBrush(theme.MutedForegroundHex);
        var accentBrush = StickyNotePalette.CreateFrozenBrush("#60CDFF");

        RootBorder.Background = bgBrush;
        RootBorder.BorderBrush = borderBrush;
        HeaderBorder.Background = headerBrush;

        NoteTextBox.Foreground = fgBrush;
        NoteTextBox.CaretBrush = fgBrush;
        TxtPlaceholder.Foreground = mutedBrush;
        TxtCollapsedPreview.Foreground = fgBrush;
        TxtConfirmDelete.Foreground = fgBrush;

        IconDragGrip.Foreground = mutedBrush;
        IconColor.Foreground = mutedBrush;
        IconPin.Foreground = _item.IsPinnedTopmost ? accentBrush : mutedBrush;
        IconPin.Symbol = _item.IsPinnedTopmost ? SymbolRegular.Pin16 : SymbolRegular.PinOff16;
        IconClickThrough.Foreground = _item.IsClickThrough ? accentBrush : mutedBrush;
        IconClickThrough.Symbol = _item.IsClickThrough ? SymbolRegular.LockClosed16 : SymbolRegular.LockOpen16;
        IconCollapse.Foreground = mutedBrush;
        IconCollapse.Symbol = _item.IsCollapsed ? SymbolRegular.ChevronDown16 : SymbolRegular.ChevronUp16;
        IconHide.Foreground = mutedBrush;
        IconDelete.Foreground = mutedBrush;

        StylePopupBorder.Background = StickyNotePalette.CreateFrozenBrush(theme.HeaderHex);
        StylePopupBorder.BorderBrush = borderBrush;
        TxtStyleColorLabel.Foreground = fgBrush;
        TxtOpacityLabel.Foreground = fgBrush;
        TxtFontLabel.Foreground = fgBrush;
        TxtOpacityLabel.Text = $"Прозрачность: {Math.Round(opacity * 100)}%";
        TxtFontLabel.Text = $"Шрифт: {Math.Round(_item.FontSize)} px";
    }

    private void ApplyWindowMode()
    {
        if (_item.IsClickThrough)
        {
            // Тело окна полностью пропускает клики на уровне ОС (WS_EX_TRANSPARENT),
            // а верхняя кликабельная панель перекрывается отдельным мини-окном без WS_EX_TRANSPARENT.
            StickyNoteWindowInterop.ApplyExtendedStyle(this, clickThrough: true, noActivate: true);
            HeaderControlsGrid.Visibility = Visibility.Hidden;
            NoteTextBox.IsReadOnly = true;
            NoteTextBox.IsHitTestVisible = false;
            SetResizeThumbsEnabled(false);
            EnsureHeaderOverlaySynced();
        }
        else
        {
            CloseHeaderOverlay();
            StickyNoteWindowInterop.ApplyExtendedStyle(this, clickThrough: false, noActivate: false);
            HeaderControlsGrid.Visibility = Visibility.Visible;
            NoteTextBox.IsReadOnly = false;
            NoteTextBox.IsHitTestVisible = true;
            SetResizeThumbsEnabled(!_item.IsCollapsed);
        }
    }

    private void SetResizeThumbsEnabled(bool enabled)
    {
        var vis = enabled ? Visibility.Visible : Visibility.Collapsed;
        ResizeRightThumb.Visibility = vis;
        ResizeBottomThumb.Visibility = vis;
        ResizeCornerThumb.Visibility = vis;
    }

    private void EnsureHeaderOverlaySynced()
    {
        if (!IsVisible)
        {
            _clickThroughHeader?.Hide();
            return;
        }

        if (_clickThroughHeader == null)
        {
            _clickThroughHeader = new StickyNoteHeaderOverlay(
                onUnlock: () =>
                {
                    _item.IsClickThrough = false;
                    _item.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    ApplyWindowMode();
                    ApplyThemeColors();
                    _onChanged(_item);
                },
                onToggleCollapse: () => ToggleCollapse(),
                onDelete: () => _onDeleteRequested(_item.Id),
                hasContent: () => !string.IsNullOrWhiteSpace(_item.Content),
                onDragged: (newLeft, newTop) =>
                {
                    _isApplyingModel = true;
                    try
                    {
                        Left = newLeft;
                        Top = newTop;
                        _item.Left = newLeft;
                        _item.Top = newTop;
                        _item.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    }
                    finally
                    {
                        _isApplyingModel = false;
                    }
                    _onChanged(_item);
                });
        }

        _clickThroughHeader.SyncState(
            Left,
            Top,
            Width,
            _item.IsPinnedTopmost,
            _item.IsCollapsed,
            StickyNotePalette.Resolve(_item.ColorHex),
            _item.Opacity);

        if (!_clickThroughHeader.IsVisible)
        {
            _clickThroughHeader.Show();
        }
    }

    private void CloseHeaderOverlay()
    {
        if (_clickThroughHeader != null)
        {
            var overlay = _clickThroughHeader;
            _clickThroughHeader = null;
            overlay.Close();
        }
    }

    private void BuildColorSwatches()
    {
        ColorSwatchesPanel.Children.Clear();
        foreach (var preset in StickyNotePalette.Presets)
        {
            var swatchBtn = new System.Windows.Controls.Button
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(0, 0, 6, 4),
                Background = StickyNotePalette.CreateFrozenBrush(preset.BackgroundHex),
                BorderBrush = StickyNotePalette.CreateFrozenBrush(preset.BorderHex),
                BorderThickness = new Thickness(1.5),
                ToolTip = preset.Name,
                Cursor = Cursors.Hand,
                Tag = preset.BackgroundHex
            };

            swatchBtn.Click += (_, _) =>
            {
                _item.ColorHex = preset.BackgroundHex;
                _item.UpdatedAtUtc = DateTimeOffset.UtcNow;
                ApplyThemeColors();
                _onChanged(_item);
            };

            ColorSwatchesPanel.Children.Add(swatchBtn);
        }
    }

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        if (_isApplyingModel) return;

        _item.Left = Left;
        _item.Top = Top;
        _item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (_item.IsClickThrough)
        {
            _clickThroughHeader?.SyncState(
                Left,
                Top,
                Width,
                _item.IsPinnedTopmost,
                _item.IsCollapsed,
                StickyNotePalette.Resolve(_item.ColorHex),
                _item.Opacity);
        }
        _onChanged(_item);
    }

    private void OnNoteTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isApplyingModel) return;

        _item.Content = NoteTextBox.Text;
        _item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        TxtPlaceholder.Visibility = string.IsNullOrEmpty(_item.Content) && !_item.IsCollapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
        _onChanged(_item);
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleCollapse();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // Ignore if mouse button released before DragMove starts
            }
        }
    }

    private void OnStyleClick(object sender, RoutedEventArgs e)
    {
        ApplyThemeColors();
        StylePopup.IsOpen = !StylePopup.IsOpen;
    }

    private void OnToggleTopmostClick(object sender, RoutedEventArgs e)
    {
        _item.IsPinnedTopmost = !_item.IsPinnedTopmost;
        _item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        Topmost = _item.IsPinnedTopmost;
        ApplyThemeColors();
        _onChanged(_item);
    }

    private void OnToggleClickThroughClick(object sender, RoutedEventArgs e)
    {
        StylePopup.IsOpen = false;
        _item.IsClickThrough = !_item.IsClickThrough;
        _item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        ApplyWindowMode();
        ApplyThemeColors();
        _onChanged(_item);
    }

    private void OnToggleCollapseClick(object sender, RoutedEventArgs e) => ToggleCollapse();

    private void ToggleCollapse()
    {
        StylePopup.IsOpen = false;
        _item.IsCollapsed = !_item.IsCollapsed;
        _item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        ApplyModelToUi();
        if (IsLoaded)
        {
            ApplyWindowMode();
        }
        _onChanged(_item);
    }

    private void OnHideClick(object sender, RoutedEventArgs e)
    {
        StylePopup.IsOpen = false;
        _onHideRequested(_item.Id);
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        StylePopup.IsOpen = false;
        if (string.IsNullOrWhiteSpace(_item.Content))
        {
            _onDeleteRequested(_item.Id);
            return;
        }

        HeaderButtonsPanel.Visibility = Visibility.Collapsed;
        ConfirmDeletePanel.Visibility = Visibility.Visible;
    }

    private void OnConfirmDeleteYesClick(object sender, RoutedEventArgs e)
    {
        ConfirmDeletePanel.Visibility = Visibility.Collapsed;
        HeaderButtonsPanel.Visibility = Visibility.Visible;
        _onDeleteRequested(_item.Id);
    }

    private void OnConfirmDeleteNoClick(object sender, RoutedEventArgs e)
    {
        ConfirmDeletePanel.Visibility = Visibility.Collapsed;
        HeaderButtonsPanel.Visibility = Visibility.Visible;
    }

    private void OnResizeRightDragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeBy(e.HorizontalChange, 0);
    }

    private void OnResizeBottomDragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeBy(0, e.VerticalChange);
    }

    private void OnResizeCornerDragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeBy(e.HorizontalChange, e.VerticalChange);
    }

    private void ResizeBy(double deltaW, double deltaH)
    {
        if (_item.IsCollapsed || _item.IsClickThrough) return;

        var newW = Math.Clamp(Width + deltaW, StickyNoteItem.MinWidth, 1000);
        var newH = Math.Clamp(Height + deltaH, StickyNoteItem.MinHeight, 900);

        Width = newW;
        Height = newH;
        _item.Width = newW;
        _item.Height = newH;
        _item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        _onChanged(_item);
    }

    private void OnOpacityDecreaseClick(object sender, RoutedEventArgs e) => AdjustOpacity(-0.08);
    private void OnOpacityIncreaseClick(object sender, RoutedEventArgs e) => AdjustOpacity(0.08);

    private void AdjustOpacity(double delta)
    {
        _item.Opacity = Math.Clamp(Math.Round(_item.Opacity + delta, 2), 0.35, 1.0);
        _item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        ApplyThemeColors();
        _onChanged(_item);
    }

    private void OnFontDecreaseClick(object sender, RoutedEventArgs e) => AdjustFont(-1);
    private void OnFontIncreaseClick(object sender, RoutedEventArgs e) => AdjustFont(1);

    private void AdjustFont(double delta)
    {
        _item.FontSize = Math.Clamp(_item.FontSize + delta, 10, 28);
        _item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        NoteTextBox.FontSize = _item.FontSize;
        TxtPlaceholder.FontSize = _item.FontSize;
        ApplyThemeColors();
        _onChanged(_item);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        LocationChanged -= OnWindowLocationChanged;
        if (!_isClosingCleanly)
        {
            CloseHeaderOverlay();
        }
    }
}
