using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace ZenithApp.Modules.StickyNotes;

public partial class StickyNoteHeaderOverlay : Window
{
    private readonly Action _onUnlock;
    private readonly Action _onToggleCollapse;
    private readonly Action _onDelete;
    private readonly Func<bool> _hasContent;
    private readonly Action<double, double> _onDragged;

    private bool _isUpdatingPosition;

    public StickyNoteHeaderOverlay(
        Action onUnlock,
        Action onToggleCollapse,
        Action onDelete,
        Func<bool> hasContent,
        Action<double, double> onDragged)
    {
        InitializeComponent();
        _onUnlock = onUnlock;
        _onToggleCollapse = onToggleCollapse;
        _onDelete = onDelete;
        _hasContent = hasContent;
        _onDragged = onDragged;

        Loaded += (_, _) => StickyNoteWindowInterop.ApplyExtendedStyle(this, clickThrough: false, noActivate: true);
        LocationChanged += (_, _) =>
        {
            if (!_isUpdatingPosition)
            {
                _onDragged(Left, Top);
            }
        };
    }

    public void SyncState(
        double left,
        double top,
        double width,
        bool isTopmost,
        bool isCollapsed,
        StickyNoteTheme theme,
        double opacity)
    {
        _isUpdatingPosition = true;
        try
        {
            Left = left;
            Top = top;
            Width = width;
            Height = StickyNoteItem.HeaderHeight;
            Topmost = isTopmost;
        }
        finally
        {
            _isUpdatingPosition = false;
        }

        RootBorder.Background = isCollapsed
            ? StickyNotePalette.CreateBackgroundBrush(theme, opacity)
            : StickyNotePalette.CreateHeaderOverlayBrush(theme, opacity);
        RootBorder.BorderBrush = StickyNotePalette.CreateBorderBrush(theme);
        RootBorder.BorderThickness = isCollapsed ? new Thickness(1) : new Thickness(1, 1, 1, 0);
        RootBorder.CornerRadius = isCollapsed ? new CornerRadius(8) : new CornerRadius(8, 8, 0, 0);

        var mutedBrush = StickyNotePalette.CreateFrozenBrush(theme.MutedForegroundHex);
        var fgBrush = StickyNotePalette.CreateFrozenBrush(theme.ForegroundHex);

        TxtBadge.Foreground = mutedBrush;
        TxtConfirm.Foreground = fgBrush;
        IconUnlock.Foreground = fgBrush;
        IconCollapse.Foreground = mutedBrush;
        IconCollapse.Symbol = isCollapsed ? SymbolRegular.ChevronDown16 : SymbolRegular.ChevronUp16;
        IconDelete.Foreground = mutedBrush;
    }

    private void OnDragMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _onToggleCollapse();
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

    private void OnUnlockClick(object sender, RoutedEventArgs e) => _onUnlock();

    private void OnCollapseClick(object sender, RoutedEventArgs e) => _onToggleCollapse();

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (!_hasContent())
        {
            _onDelete();
            return;
        }

        NormalActionsPanel.Visibility = Visibility.Collapsed;
        ConfirmDeletePanel.Visibility = Visibility.Visible;
    }

    private void OnConfirmDeleteYesClick(object sender, RoutedEventArgs e)
    {
        ConfirmDeletePanel.Visibility = Visibility.Collapsed;
        NormalActionsPanel.Visibility = Visibility.Visible;
        _onDelete();
    }

    private void OnConfirmDeleteNoClick(object sender, RoutedEventArgs e)
    {
        ConfirmDeletePanel.Visibility = Visibility.Collapsed;
        NormalActionsPanel.Visibility = Visibility.Visible;
    }
}
