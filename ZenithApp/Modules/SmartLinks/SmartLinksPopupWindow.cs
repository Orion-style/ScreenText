using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace ZenithApp.Modules.SmartLinks;

public sealed class SmartLinksPopupWindow : Window
{
    private readonly ContentClassification _classification;
    private readonly SmartLinksSettings _settings;
    private readonly System.Drawing.Rectangle _selectionScreenBounds;
    private readonly string _originalText;
    private readonly IntPtr _previousForegroundWindow;

    private DispatcherTimer? _autoCloseTimer;
    private bool _isClosing;
    private bool _hasUserInteracted;

    private TextBox _textBox = null!;
    private Border _contentBorder = null!;
    private Button? _btnOpen;
    private Button? _btnReset;
    private TextBlock _badgeText = null!;
    private Border _badge = null!;

    public SmartLinksPopupWindow(
        ContentClassification classification,
        SmartLinksSettings settings,
        System.Drawing.Rectangle selectionScreenBounds)
    {
        _classification = classification;
        _settings = settings;
        _selectionScreenBounds = selectionScreenBounds;
        _originalText = classification.RawContent;

        // Сохраняем окно, в котором работал пользователь, чтобы вернуть фокус при закрытии
        _previousForegroundWindow = GetForegroundWindow();

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;

        BuildUi();

        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
        Deactivated += OnDeactivated;
        MouseEnter += OnMouseEnter;
        MouseLeave += OnMouseLeave;
    }

    private void BuildUi()
    {
        bool isDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

        var bgBrush = new SolidColorBrush(isDark
            ? Color.FromArgb(242, 28, 28, 30)
            : Color.FromArgb(242, 250, 250, 252));

        var borderBrush = new SolidColorBrush(isDark
            ? Color.FromArgb(50, 255, 255, 255)
            : Color.FromArgb(50, 0, 0, 0));

        var textPrimaryBrush = isDark ? Brushes.White : Brushes.Black;
        var textSecondaryBrush = new SolidColorBrush(isDark
            ? Color.FromArgb(190, 255, 255, 255)
            : Color.FromArgb(170, 0, 0, 0));

        var mainBorder = new Border
        {
            Background = bgBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Margin = new Thickness(12),
            MaxWidth = 440,
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                Opacity = isDark ? 0.45 : 0.25,
                ShadowDepth = 3,
                Color = Colors.Black
            }
        };

        var rootStack = new StackPanel();

        // 1. Заголовок карточки
        var headerPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };

        _badge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 8, 0)
        };

        _badgeText = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        };
        _badge.Child = _badgeText;
        UpdateBadgeUi();

        headerPanel.Children.Add(_badge);

        // Кнопка закрытия (крестик)
        var closeBtn = new Button
        {
            Content = "✕",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = textSecondaryBrush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(4, 0, 4, 0)
        };
        closeBtn.Click += (_, _) => CloseWithAnimation();
        DockPanel.SetDock(closeBtn, System.Windows.Controls.Dock.Right);
        headerPanel.Children.Add(closeBtn);

        rootStack.Children.Add(headerPanel);

        // 2. Многострочное редактируемое поле (TextBox)
        _contentBorder = new Border
        {
            Background = new SolidColorBrush(isDark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(15, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 10)
        };

        _textBox = new TextBox
        {
            Text = _originalText,
            FontSize = 12.5,
            Foreground = textPrimaryBrush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MinHeight = 48,
            MaxHeight = 110, // ~3-6 строк с прокруткой при превышении
            FontFamily = new FontFamily("Segoe UI, Consolas"),
            CaretBrush = textPrimaryBrush
        };

        // События взаимодействия с полем
        _textBox.PreviewMouseDown += OnTextBoxInteraction;
        _textBox.GotKeyboardFocus += OnTextBoxInteraction;
        _textBox.TextChanged += OnTextBoxTextChanged;

        _contentBorder.Child = _textBox;
        rootStack.Children.Add(_contentBorder);

        // 3. Панель кнопок действий
        var buttonsPanel = new WrapPanel { Orientation = Orientation.Horizontal };

        // Если это была ссылка или обычный текст, который может стать ссылкой
        if (_classification.Kind is ContentKind.SingleUrl or ContentKind.PlainText or ContentKind.MultipleUrls)
        {
            _btnOpen = CreateActionButton("Открыть в браузере", () =>
            {
                var text = _textBox.Text.Trim();
                if (ContentClassifier.SafeOpenInBrowser(text))
                {
                    CloseWithAnimation();
                }
            }, isAccent: true);

            buttonsPanel.Children.Add(_btnOpen);
        }

        // Кнопка «Копировать»
        buttonsPanel.Children.Add(CreateActionButton("Копировать", () =>
        {
            Clipboard.SetText(_textBox.Text);
            CloseWithAnimation();
        }, isAccent: _classification.Kind == ContentKind.QrSpecial));

        // Кнопки «Найти в Google» и «Перевести» (для не-спец QR)
        if (_classification.Kind != ContentKind.QrSpecial)
        {
            buttonsPanel.Children.Add(CreateActionButton("Найти в Google", () =>
            {
                SearchInGoogle(_textBox.Text);
                CloseWithAnimation();
            }));

            buttonsPanel.Children.Add(CreateActionButton("Перевести", () =>
            {
                TranslateText(_textBox.Text, _settings.DefaultTranslateLanguage);
                CloseWithAnimation();
            }));
        }

        // Кнопка «Сбросить» (видима только когда текст был изменён)
        _btnReset = CreateActionButton("Сбросить", () =>
        {
            _textBox.Text = _originalText;
            _textBox.SelectAll();
        });
        _btnReset.Visibility = Visibility.Collapsed;
        buttonsPanel.Children.Add(_btnReset);

        rootStack.Children.Add(buttonsPanel);
        mainBorder.Child = rootStack;
        Content = mainBorder;

        // Первичная валидация ссылки при отображении
        ValidateCurrentUrlState();
    }

    private void UpdateBadgeUi()
    {
        if (_classification.IsFromQr)
        {
            _badge.Background = new SolidColorBrush(Color.FromArgb(35, 139, 92, 246));
            _badgeText.Foreground = new SolidColorBrush(Color.FromRgb(167, 139, 250));
            _badgeText.Text = "QR-КОД";
        }
        else if (ContentClassifier.IsValidHttpUrl(_textBox?.Text ?? _originalText, out _))
        {
            _badge.Background = new SolidColorBrush(Color.FromArgb(35, 59, 130, 246));
            _badgeText.Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250));
            _badgeText.Text = "ССЫЛКА";
        }
        else
        {
            _badge.Background = new SolidColorBrush(Color.FromArgb(35, 16, 185, 129));
            _badgeText.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            _badgeText.Text = "ТЕКСТ";
        }
    }

    private void OnTextBoxInteraction(object sender, RoutedEventArgs e)
    {
        // Пользователь нажал или сфокусировался на поле -> останавливаем таймер навсегда
        _hasUserInteracted = true;
        _autoCloseTimer?.Stop();
        _autoCloseTimer = null;
    }

    private void OnTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        _hasUserInteracted = true;
        _autoCloseTimer?.Stop();
        _autoCloseTimer = null;

        bool isModified = !string.Equals(_textBox.Text, _originalText, StringComparison.Ordinal);
        if (_btnReset != null)
        {
            _btnReset.Visibility = isModified ? Visibility.Visible : Visibility.Collapsed;
        }

        ValidateCurrentUrlState();
        UpdateBadgeUi();
    }

    private void ValidateCurrentUrlState()
    {
        if (_btnOpen == null) return;

        var currentText = _textBox.Text.Trim();
        bool isValidUrl = ContentClassifier.IsValidHttpUrl(currentText, out _);

        // Кнопка открыть доступна только если адрес валиден (http/https)
        if (_classification.Kind is ContentKind.SingleUrl or ContentKind.MultipleUrls)
        {
            _btnOpen.IsEnabled = isValidUrl;
            _btnOpen.Opacity = isValidUrl ? 1.0 : 0.45;

            // Подсветка поля при невалидном URL
            if (!isValidUrl)
            {
                _contentBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Красный
                _contentBorder.Background = new SolidColorBrush(Color.FromArgb(25, 239, 68, 68));
            }
            else
            {
                bool isDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
                _contentBorder.BorderBrush = new SolidColorBrush(isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0));
                _contentBorder.Background = new SolidColorBrush(isDark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(15, 0, 0, 0));
            }
        }
        else
        {
            // Для обычного текста: если пользователь ввёл ссылку, кнопка «Открыть» становится видимой и активной
            _btnOpen.Visibility = isValidUrl ? Visibility.Visible : Visibility.Collapsed;
            _btnOpen.IsEnabled = isValidUrl;
        }
    }

    private Button CreateActionButton(string text, Action onClick, bool isAccent = false)
    {
        bool isDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        var textPrimaryBrush = isDark ? Brushes.White : Brushes.Black;

        var btn = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 8, 6),
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 12,
            FontWeight = isAccent ? FontWeights.SemiBold : FontWeights.Normal,
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1)
        };

        if (isAccent)
        {
            btn.Background = new SolidColorBrush(Color.FromRgb(0, 120, 215));
            btn.Foreground = Brushes.White;
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 100, 180));
        }
        else
        {
            btn.Background = new SolidColorBrush(isDark ? Color.FromArgb(20, 255, 255, 255) : Color.FromArgb(20, 0, 0, 0));
            btn.Foreground = textPrimaryBrush;
            btn.BorderBrush = new SolidColorBrush(isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0));
        }

        btn.Click += (_, _) => onClick();
        return btn;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionNearSelectionOrCursor();
        StartAutoCloseTimer();

        // Анимация плавного появления
        Opacity = 0.0;
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200));
        BeginAnimation(OpacityProperty, fadeIn);

        // Активируем окно и фокусируем поле ввода для полноценной работы клавиатуры
        Activate();
        _textBox.Focus();
        _textBox.CaretIndex = _textBox.Text.Length;
    }

    private void PositionNearSelectionOrCursor()
    {
        var source = PresentationSource.FromVisual(this);
        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        double popW = ActualWidth;
        double popH = ActualHeight;

        // Позиция по умолчанию: под областью выделения
        double targetScreenX = _selectionScreenBounds.Left;
        double targetScreenY = _selectionScreenBounds.Bottom + 10;

        // Если выделение пустое, позиционируем у курсора
        if (_selectionScreenBounds.Width <= 0 || _selectionScreenBounds.Height <= 0)
        {
            if (GetCursorPos(out var p))
            {
                targetScreenX = p.X + 10;
                targetScreenY = p.Y + 10;
            }
        }

        double dipX = targetScreenX / dpiX;
        double dipY = targetScreenY / dpiY;

        var workArea = SystemParameters.WorkArea;

        if (dipX + popW > workArea.Right)
        {
            dipX = workArea.Right - popW - 10;
        }
        if (dipX < workArea.Left)
        {
            dipX = workArea.Left + 10;
        }

        if (dipY + popH > workArea.Bottom)
        {
            double topAttempt = (_selectionScreenBounds.Top / dpiY) - popH - 10;
            if (topAttempt >= workArea.Top)
            {
                dipY = topAttempt;
            }
            else
            {
                dipY = workArea.Bottom - popH - 10;
            }
        }

        Left = dipX;
        Top = dipY;
    }

    private void StartAutoCloseTimer()
    {
        if (_hasUserInteracted) return;

        int seconds = Math.Max(3, _settings.PopupTimeoutSeconds);
        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _autoCloseTimer.Tick += (s, e) =>
        {
            if (!_hasUserInteracted)
            {
                _autoCloseTimer?.Stop();
                CloseWithAnimation();
            }
        };
        _autoCloseTimer.Start();
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        _autoCloseTimer?.Stop();
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isClosing && !_hasUserInteracted)
        {
            StartAutoCloseTimer();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // Esc = закрыть без изменений в буфере
            CloseWithAnimation();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            // Ctrl+Enter = «Копировать» отредактированный текст и закрыть
            Clipboard.SetText(_textBox.Text);
            CloseWithAnimation();
            e.Handled = true;
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Клик вне окна -> закрыть подсказку
        CloseWithAnimation();
    }

    private void CloseWithAnimation()
    {
        if (_isClosing) return;
        _isClosing = true;

        _autoCloseTimer?.Stop();
        _autoCloseTimer = null;

        // Возвращаем фокус окну, в котором пользователь работал до этого
        RestorePreviousFocus();

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(160));
        fadeOut.Completed += (_, _) =>
        {
            Close();
            GC.Collect(1);
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void RestorePreviousFocus()
    {
        if (_previousForegroundWindow != IntPtr.Zero && _previousForegroundWindow != new System.Windows.Interop.WindowInteropHelper(this).Handle)
        {
            try
            {
                SetForegroundWindow(_previousForegroundWindow);
            }
            catch { }
        }
    }

    private static void SearchInGoogle(string text)
    {
        try
        {
            var query = Uri.EscapeDataString(text.Trim());
            var url = $"https://www.google.com/search?q={query}";
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { }
    }

    private static void TranslateText(string text, string targetLang)
    {
        try
        {
            var trimmed = text.Trim();
            const int maxLen = 1500;
            if (trimmed.Length > maxLen)
            {
                Screenshot.NotificationToast.Show(
                    $"Текст сокращен до {maxLen} симв. для URL Google Translate.",
                    "Умные подсказки",
                    Screenshot.ToastNotificationType.Info,
                    durationSeconds: 4);
                trimmed = trimmed[..maxLen];
            }

            var query = Uri.EscapeDataString(trimmed);
            var lang = string.IsNullOrWhiteSpace(targetLang) ? "ru" : targetLang.Trim();
            var url = $"https://translate.google.com/?sl=auto&tl={lang}&text={query}&op=translate";
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
