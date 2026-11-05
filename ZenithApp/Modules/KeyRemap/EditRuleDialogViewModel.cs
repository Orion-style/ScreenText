using System.Windows.Input;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.KeyRemap;

public sealed class EditRuleDialogViewModel : ViewModelBase
{
    private readonly KeyRemapRule _originalRule;
    private string _ruleName = string.Empty;
    private int _triggerVk;
    private bool _triggerCtrl;
    private bool _triggerAlt;
    private bool _triggerShift;
    private bool _triggerWin;

    private int _actionTypeIndex; // 0 = RemapKey, 1 = RemapKeyCombination, 2 = DisableKey

    private int _targetVk;
    private bool _targetCtrl;
    private bool _targetAlt;
    private bool _targetShift;
    private bool _targetWin;

    private bool _enableAppFilter;
    private bool _appFilterIsWhitelist = true;
    private string _filterProcessesText = string.Empty;

    private string _validationError = string.Empty;
    private bool _hasValidationError;

    public EditRuleDialogViewModel(KeyRemapRule rule)
    {
        _originalRule = rule;
        _ruleName = rule.Name;
        _triggerVk = rule.TriggerVirtualKey;
        _triggerCtrl = rule.TriggerCtrl;
        _triggerAlt = rule.TriggerAlt;
        _triggerShift = rule.TriggerShift;
        _triggerWin = rule.TriggerWin;

        _actionTypeIndex = (int)rule.ActionType;

        _targetVk = rule.TargetVirtualKey;
        _targetCtrl = rule.TargetCtrl;
        _targetAlt = rule.TargetAlt;
        _targetShift = rule.TargetShift;
        _targetWin = rule.TargetWin;

        _enableAppFilter = rule.EnableAppFilter;
        _appFilterIsWhitelist = rule.AppFilterIsWhitelist;
        _filterProcessesText = string.Join(", ", rule.FilterProcesses);
    }

    public string Title => _triggerVk == 0 ? "Добавить правило переназначения" : "Редактировать правило";

    public string RuleName
    {
        get => _ruleName;
        set => SetProperty(ref _ruleName, value);
    }

    public string TriggerDisplay => VirtualKeyHelper.FormatKeyCombination(_triggerVk, _triggerCtrl, _triggerAlt, _triggerShift, _triggerWin);

    public bool TriggerCtrl
    {
        get => _triggerCtrl;
        set
        {
            if (SetProperty(ref _triggerCtrl, value))
            {
                OnPropertyChanged(nameof(TriggerDisplay));
                Validate();
            }
        }
    }

    public bool TriggerAlt
    {
        get => _triggerAlt;
        set
        {
            if (SetProperty(ref _triggerAlt, value))
            {
                OnPropertyChanged(nameof(TriggerDisplay));
                Validate();
            }
        }
    }

    public bool TriggerShift
    {
        get => _triggerShift;
        set
        {
            if (SetProperty(ref _triggerShift, value))
            {
                OnPropertyChanged(nameof(TriggerDisplay));
                Validate();
            }
        }
    }

    public bool TriggerWin
    {
        get => _triggerWin;
        set
        {
            if (SetProperty(ref _triggerWin, value))
            {
                OnPropertyChanged(nameof(TriggerDisplay));
                Validate();
            }
        }
    }

    public int ActionTypeIndex
    {
        get => _actionTypeIndex;
        set
        {
            if (SetProperty(ref _actionTypeIndex, value))
            {
                OnPropertyChanged(nameof(IsTargetVisible));
                OnPropertyChanged(nameof(IsComboModifiersVisible));
                OnPropertyChanged(nameof(TargetDisplay));
                Validate();
            }
        }
    }

    public bool IsTargetVisible => ActionTypeIndex != 2; // Not DisableKey
    public bool IsComboModifiersVisible => ActionTypeIndex == 1; // RemapKeyCombination

    public string TargetDisplay => ActionTypeIndex switch
    {
        2 => "Клавиша или комбинация отключена (подавление)",
        1 => VirtualKeyHelper.FormatKeyCombination(_targetVk, _targetCtrl, _targetAlt, _targetShift, _targetWin),
        _ => VirtualKeyHelper.GetKeyName(_targetVk)
    };

    public bool TargetCtrl
    {
        get => _targetCtrl;
        set
        {
            if (SetProperty(ref _targetCtrl, value))
            {
                OnPropertyChanged(nameof(TargetDisplay));
                Validate();
            }
        }
    }

    public bool TargetAlt
    {
        get => _targetAlt;
        set
        {
            if (SetProperty(ref _targetAlt, value))
            {
                OnPropertyChanged(nameof(TargetDisplay));
                Validate();
            }
        }
    }

    public bool TargetShift
    {
        get => _targetShift;
        set
        {
            if (SetProperty(ref _targetShift, value))
            {
                OnPropertyChanged(nameof(TargetDisplay));
                Validate();
            }
        }
    }

    public bool TargetWin
    {
        get => _targetWin;
        set
        {
            if (SetProperty(ref _targetWin, value))
            {
                OnPropertyChanged(nameof(TargetDisplay));
                Validate();
            }
        }
    }

    public bool EnableAppFilter
    {
        get => _enableAppFilter;
        set => SetProperty(ref _enableAppFilter, value);
    }

    public bool AppFilterIsWhitelist
    {
        get => _appFilterIsWhitelist;
        set => SetProperty(ref _appFilterIsWhitelist, value);
    }

    public string FilterProcessesText
    {
        get => _filterProcessesText;
        set => SetProperty(ref _filterProcessesText, value);
    }

    public string ValidationError
    {
        get => _validationError;
        set => SetProperty(ref _validationError, value);
    }

    public bool HasValidationError
    {
        get => _hasValidationError;
        set => SetProperty(ref _hasValidationError, value);
    }

    public void SetTrigger(int vk, bool ctrl, bool alt, bool shift, bool win)
    {
        _triggerVk = vk;
        _triggerCtrl = ctrl;
        _triggerAlt = alt;
        _triggerShift = shift;
        _triggerWin = win;

        OnPropertyChanged(nameof(TriggerCtrl));
        OnPropertyChanged(nameof(TriggerAlt));
        OnPropertyChanged(nameof(TriggerShift));
        OnPropertyChanged(nameof(TriggerWin));
        OnPropertyChanged(nameof(TriggerDisplay));
        Validate();
    }

    public void SetTriggerModifiers(bool ctrl, bool alt, bool shift, bool win)
    {
        _triggerCtrl = ctrl;
        _triggerAlt = alt;
        _triggerShift = shift;
        _triggerWin = win;

        OnPropertyChanged(nameof(TriggerCtrl));
        OnPropertyChanged(nameof(TriggerAlt));
        OnPropertyChanged(nameof(TriggerShift));
        OnPropertyChanged(nameof(TriggerWin));
        OnPropertyChanged(nameof(TriggerDisplay));
        Validate();
    }

    public void SetTarget(int vk, bool ctrl, bool alt, bool shift, bool win)
    {
        _targetVk = vk;
        if (ctrl || alt || shift || win)
        {
            ActionTypeIndex = 1;
            _targetCtrl = ctrl;
            _targetAlt = alt;
            _targetShift = shift;
            _targetWin = win;
            OnPropertyChanged(nameof(TargetCtrl));
            OnPropertyChanged(nameof(TargetAlt));
            OnPropertyChanged(nameof(TargetShift));
            OnPropertyChanged(nameof(TargetWin));
        }
        else if (ActionTypeIndex == 1)
        {
            _targetCtrl = false;
            _targetAlt = false;
            _targetShift = false;
            _targetWin = false;
            OnPropertyChanged(nameof(TargetCtrl));
            OnPropertyChanged(nameof(TargetAlt));
            OnPropertyChanged(nameof(TargetShift));
            OnPropertyChanged(nameof(TargetWin));
        }
        else
        {
            _targetCtrl = false;
            _targetAlt = false;
            _targetShift = false;
            _targetWin = false;
        }

        OnPropertyChanged(nameof(TargetDisplay));
        Validate();
    }

    public void SetTargetModifiers(bool ctrl, bool alt, bool shift, bool win)
    {
        ActionTypeIndex = 1;
        _targetCtrl = ctrl;
        _targetAlt = alt;
        _targetShift = shift;
        _targetWin = win;

        OnPropertyChanged(nameof(TargetCtrl));
        OnPropertyChanged(nameof(TargetAlt));
        OnPropertyChanged(nameof(TargetShift));
        OnPropertyChanged(nameof(TargetWin));
        OnPropertyChanged(nameof(TargetDisplay));
        Validate();
    }

    public bool Validate()
    {
        HasValidationError = false;
        ValidationError = string.Empty;

        if (_triggerVk == 0)
        {
            ValidationError = "Необходимо нажать и задать исходную клавишу или сочетание.";
            HasValidationError = true;
            return false;
        }

        // Security check: Guard against disabling or remapping critical system keys like Ctrl+Alt+Del
        if (_triggerCtrl && _triggerAlt && _triggerVk == VirtualKeyHelper.VK_DELETE)
        {
            ValidationError = "Переназначение критического системного сочетания Ctrl+Alt+Delete запрещено для безопасности.";
            HasValidationError = true;
            return false;
        }

        // Emergency escape hotkey protection (Ctrl+Shift+Esc)
        if (_triggerCtrl && _triggerShift && _triggerVk == VirtualKeyHelper.VK_ESCAPE && ActionTypeIndex == 2)
        {
            ValidationError = "Отключение сочетания Ctrl+Shift+Esc запрещено, так как оно используется для аварийного выхода.";
            HasValidationError = true;
            return false;
        }

        if (ActionTypeIndex != 2 && _targetVk == 0 && !_targetCtrl && !_targetAlt && !_targetShift && !_targetWin)
        {
            ValidationError = "Необходимо задать целевую клавишу или сочетание.";
            HasValidationError = true;
            return false;
        }

        return true;
    }

    public KeyRemapRule BuildResult()
    {
        var procList = FilterProcessesText
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .ToList();

        var res = _originalRule.Clone();
        res.Name = string.IsNullOrWhiteSpace(RuleName) ? TriggerDisplay : RuleName;
        res.TriggerVirtualKey = _triggerVk;
        res.TriggerCtrl = _triggerCtrl;
        res.TriggerAlt = _triggerAlt;
        res.TriggerShift = _triggerShift;
        res.TriggerWin = _triggerWin;

        res.ActionType = (KeyRemapActionType)ActionTypeIndex;
        res.TargetVirtualKey = _targetVk;
        res.TargetCtrl = _targetCtrl;
        res.TargetAlt = _targetAlt;
        res.TargetShift = _targetShift;
        res.TargetWin = _targetWin;

        res.EnableAppFilter = EnableAppFilter;
        res.AppFilterIsWhitelist = AppFilterIsWhitelist;
        res.FilterProcesses = procList;

        return res;
    }
}
