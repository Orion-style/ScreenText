using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using ZenithApp.Core.Logging;

namespace ZenithApp.Modules.Dock;

public partial class DockWindow : Window
{
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int GWL_EXSTYLE = -20;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    private readonly DockModule _module;
    private readonly IAppLogger _logger;
    private bool _isMouseInside;
    private bool _isDockVisible = true;

    public ObservableCollection<DockItemViewModel> Items { get; } = new();

    public DockWindow(DockModule module, IAppLogger logger)
    {
        InitializeComponent();
        _module = module;
        _logger = logger;
        DataContext = this;

        Loaded += OnLoaded;
        MouseEnter += OnDockMouseEnter;
        MouseLeave += OnDockMouseLeave;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Set window styles: WS_EX_NOACTIVATE ensures window NEVER steals keyboard focus from active application
        var helper = new WindowInteropHelper(this);
        var exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
        SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

        ReloadItems();
        UpdatePosition();
    }

    public void ReloadItems()
    {
        Items.Clear();
        foreach (var item in _module.Settings.Items)
        {
            var vm = new DockItemViewModel(item);
            Items.Add(vm);
        }
        UpdatePosition();
    }

    public void RefreshRunningStates()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            Items[i].UpdateRunningState();
        }
    }

    public void UpdatePosition()
    {
        Dispatcher.Invoke(() =>
        {
            var screenBounds = ScreenHelper.GetTargetScreenBounds(_module.Settings.MonitorTarget, this);

            // Measure desirable size
            Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var dockWidth = ActualWidth > 0 ? ActualWidth : DesiredSize.Width;
            var dockHeight = ActualHeight > 0 ? ActualHeight : DesiredSize.Height;

            Left = screenBounds.WorkingLeft + (screenBounds.WorkingWidth - dockWidth) / 2;
            Top = screenBounds.WorkingTop + screenBounds.WorkingHeight - dockHeight - _module.Settings.BottomMargin;
        });
    }

    public void ShowDockAnimated()
    {
        if (_isDockVisible) return;
        _isDockVisible = true;

        UpdatePosition();
        RefreshRunningStates();

        var anim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, anim);
        Visibility = Visibility.Visible;
    }

    public void HideDockAnimated()
    {
        if (!_isDockVisible) return;
        _isDockVisible = false;

        var anim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, _) =>
        {
            if (!_isDockVisible)
            {
                Visibility = Visibility.Collapsed;
            }
        };
        BeginAnimation(OpacityProperty, anim);
    }

    public void ToggleDockVisibility()
    {
        if (_isDockVisible && Visibility == Visibility.Visible)
        {
            HideDockAnimated();
        }
        else
        {
            ShowDockAnimated();
        }
    }

    private void OnDockMouseEnter(object sender, MouseEventArgs e)
    {
        _isMouseInside = true;
        RefreshRunningStates();
    }

    private void OnDockMouseLeave(object sender, MouseEventArgs e)
    {
        _isMouseInside = false;

        // Reset scales
        for (int i = 0; i < Items.Count; i++)
        {
            Items[i].Scale = 1.0;
        }

        if (_module.Settings.AutoHide)
        {
            HideDockAnimated();
        }
    }

    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (!_module.Settings.EnableMagnification || Items.Count == 0) return;

        if (sender is FrameworkElement elem && elem.DataContext is DockItemViewModel hoveredItem)
        {
            var maxFactor = _module.Settings.MagnificationFactor;

            var hoveredIndex = Items.IndexOf(hoveredItem);
            if (hoveredIndex < 0) return;

            for (int i = 0; i < Items.Count; i++)
            {
                var distance = Math.Abs(i - hoveredIndex);
                if (distance == 0)
                {
                    Items[i].Scale = maxFactor;
                }
                else if (distance == 1)
                {
                    Items[i].Scale = 1.0 + (maxFactor - 1.0) * 0.45;
                }
                else if (distance == 2)
                {
                    Items[i].Scale = 1.0 + (maxFactor - 1.0) * 0.15;
                }
                else
                {
                    Items[i].Scale = 1.0;
                }
            }
        }
    }

    private void OnItemMouseLeave(object sender, MouseEventArgs e)
    {
        // If left dock item area
        if (!_isMouseInside)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                Items[i].Scale = 1.0;
            }
        }
    }

    private void OnItemLeftClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is DockItemViewModel itemVm)
        {
            if (_module.Settings.EnableBounceAnimation)
            {
                PlayBounceAnimation(itemVm);
            }

            ProcessInteractionService.LaunchOrActivate(itemVm.Item);
            itemVm.UpdateRunningState();
        }
    }

    private void OnItemRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.ContextMenu != null)
        {
            elem.ContextMenu.PlacementTarget = elem;
            elem.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void PlayBounceAnimation(DockItemViewModel item)
    {
        var sw = Stopwatch.StartNew();
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        timer.Tick += (_, _) =>
        {
            var ms = sw.ElapsedMilliseconds;
            if (ms >= 440)
            {
                item.BounceOffsetY = 0;
                timer.Stop();
            }
            else if (ms < 120)
            {
                item.BounceOffsetY = -18 * (ms / 120.0);
            }
            else if (ms < 240)
            {
                item.BounceOffsetY = -18 * (1.0 - (ms - 120) / 120.0);
            }
            else if (ms < 340)
            {
                item.BounceOffsetY = -10 * ((ms - 240) / 100.0);
            }
            else
            {
                item.BounceOffsetY = -10 * (1.0 - (ms - 340) / 100.0);
            }
        };
        timer.Start();
    }

    #region Context Menu Handlers

    private void OnMenuOpenClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is DockItemViewModel itemVm)
        {
            ProcessInteractionService.LaunchOrActivate(itemVm.Item);
            itemVm.UpdateRunningState();
        }
    }

    private void OnMenuShowInFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is DockItemViewModel itemVm)
        {
            var path = itemVm.TargetPath;
            if (File.Exists(path))
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else if (Directory.Exists(path))
            {
                Process.Start("explorer.exe", $"\"{path}\"");
            }
        }
    }

    private void OnMenuCloseGracefullyClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is DockItemViewModel itemVm)
        {
            if (ProcessInteractionService.IsRunning(itemVm.TargetPath, out var proc) && proc != null)
            {
                ProcessInteractionService.CloseGracefully(proc);
                Task.Delay(500).ContinueWith(_ => Dispatcher.Invoke(itemVm.UpdateRunningState));
            }
        }
    }

    private void OnMenuKillForcefullyClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is DockItemViewModel itemVm)
        {
            var res = MessageBox.Show(
                $"Завершить принудительно процесс \"{itemVm.Title}\"? Несохраненные данные будут утеряны.",
                "Принудительное завершение",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res == MessageBoxResult.Yes)
            {
                if (ProcessInteractionService.IsRunning(itemVm.TargetPath, out var proc) && proc != null)
                {
                    ProcessInteractionService.KillForcefully(proc);
                    Task.Delay(300).ContinueWith(_ => Dispatcher.Invoke(itemVm.UpdateRunningState));
                }
            }
        }
    }

    private void OnMenuRemoveFromDockClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is DockItemViewModel itemVm)
        {
            _module.RemoveItem(itemVm.Item);
            Items.Remove(itemVm);
            UpdatePosition();
        }
    }

    #endregion

    #region Drag & Drop To Add Items

    private void OnWindowDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files != null)
            {
                foreach (var file in files)
                {
                    _module.AddItem(file);
                }
                ReloadItems();
            }
        }
    }

    #endregion
}
