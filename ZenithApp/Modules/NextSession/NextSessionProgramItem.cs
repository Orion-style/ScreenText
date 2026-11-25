using System.Windows.Media;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.NextSession;

public sealed class NextSessionProgramItem : ViewModelBase
{
    private bool _isSelected;
    private bool _isRunning;
    private ImageSource? _icon;

    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public bool IsCustom { get; init; }

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (SetProperty(ref _icon, value))
            {
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }

    public bool HasIcon => Icon != null;

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(ToolTipText));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string ToolTipText
    {
        get
        {
            var status = IsRunning ? " (запущено сейчас)" : string.Empty;
            if (!string.IsNullOrWhiteSpace(ExecutablePath))
            {
                return $"{Name}{status}\n{ExecutablePath}";
            }

            return $"{Name}{status}";
        }
    }

    public NextSessionProgramEntry ToEntry()
    {
        return new NextSessionProgramEntry
        {
            Name = Name,
            ExecutablePath = ExecutablePath,
            ProcessName = ProcessName,
            Arguments = Arguments
        };
    }
}
