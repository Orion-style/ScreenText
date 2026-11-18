using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace ZenithApp.Modules.StartupManager;

public partial class StartupManagerView : UserControl
{
    public StartupManagerView()
    {
        InitializeComponent();
    }
}

public sealed class StartupTabConverter : IValueConverter
{
    public static StartupTabConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int selectedTab && parameter is string paramStr && int.TryParse(paramStr, out int targetTab))
        {
            return selectedTab == targetTab;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is string paramStr && int.TryParse(paramStr, out int targetTab))
        {
            return targetTab;
        }
        return Binding.DoNothing;
    }
}
