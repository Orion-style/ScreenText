using System.Collections.ObjectModel;
using System.Windows.Input;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.KeyRemap;

public sealed class RuleItemViewModel : ViewModelBase
{
    private readonly KeyRemapRule _rule;
    private readonly KeyRemapSettingsViewModel _parent;

    public KeyRemapRule Rule => _rule;

    public RuleItemViewModel(KeyRemapRule rule, KeyRemapSettingsViewModel parent)
    {
        _rule = rule;
        _parent = parent;
        DeleteCommand = new RelayCommand(() => _parent.DeleteRule(this));
        EditCommand = new RelayCommand(() => _parent.EditRule(this));
    }

    public string Id => _rule.Id;

    public bool IsEnabled
    {
        get => _rule.IsEnabled;
        set
        {
            if (_rule.IsEnabled != value)
            {
                _rule.IsEnabled = value;
                OnPropertyChanged();
                _parent.OnRuleStateChanged();
            }
        }
    }

    public string TriggerText => VirtualKeyHelper.FormatKeyCombination(
        _rule.TriggerVirtualKey,
        _rule.TriggerCtrl,
        _rule.TriggerAlt,
        _rule.TriggerShift,
        _rule.TriggerWin);

    public string ActionSummary
    {
        get
        {
            return _rule.ActionType switch
            {
                KeyRemapActionType.DisableKey => "Отключить клавишу",
                KeyRemapActionType.RemapKey => $"Заменить на: {VirtualKeyHelper.GetKeyName(_rule.TargetVirtualKey)}",
                KeyRemapActionType.RemapKeyCombination => $"Заменить на: {VirtualKeyHelper.FormatKeyCombination(_rule.TargetVirtualKey, _rule.TargetCtrl, _rule.TargetAlt, _rule.TargetShift, _rule.TargetWin)}",
                _ => "Неизвестное действие"
            };
        }
    }

    public string FilterSummary
    {
        get
        {
            if (!_rule.EnableAppFilter || _rule.FilterProcesses.Count == 0)
                return "Все приложения";

            var mode = _rule.AppFilterIsWhitelist ? "Только в:" : "Исключая:";
            return $"{mode} {string.Join(", ", _rule.FilterProcesses)}";
        }
    }

    public bool HasConflict { get; set; }
    public string ConflictWarning { get; set; } = string.Empty;

    public ICommand DeleteCommand { get; }
    public ICommand EditCommand { get; }

    public void RefreshProperties()
    {
        OnPropertyChanged(nameof(TriggerText));
        OnPropertyChanged(nameof(ActionSummary));
        OnPropertyChanged(nameof(FilterSummary));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(HasConflict));
        OnPropertyChanged(nameof(ConflictWarning));
    }
}

public sealed class KeyRemapSettingsViewModel : ViewModelBase
{
    private readonly KeyRemapModule _module;
    private readonly ZenithApp.Core.Settings.ISettingsService _settingsService;

    private bool _emergencyEscapeEnabled;
    private string _emergencyNotification = string.Empty;
    private bool _showEmergencyNotification;

    public KeyRemapSettingsViewModel(
        KeyRemapModule module,
        ZenithApp.Core.Settings.ISettingsService settingsService)
    {
        _module = module;
        _settingsService = settingsService;

        _emergencyEscapeEnabled = _module.Settings.EmergencyEscapeEnabled;

        Rules = new ObservableCollection<RuleItemViewModel>(
            _module.Settings.Rules.Select(r => new RuleItemViewModel(r, this))
        );

        _module.EmergencyEscapeTriggered += OnEmergencyEscapeTriggered;
        _module.RulesChanged += OnModuleRulesChanged;

        ValidateConflicts();
    }

    public ObservableCollection<RuleItemViewModel> Rules { get; }

    public bool EmergencyEscapeEnabled
    {
        get => _emergencyEscapeEnabled;
        set
        {
            if (SetProperty(ref _emergencyEscapeEnabled, value))
            {
                _module.Settings.EmergencyEscapeEnabled = value;
                _settingsService.Save();
            }
        }
    }

    public string EmergencyShortcutText => _module.Settings.EmergencyShortcutText;

    public string EmergencyNotification
    {
        get => _emergencyNotification;
        set => SetProperty(ref _emergencyNotification, value);
    }

    public bool ShowEmergencyNotification
    {
        get => _showEmergencyNotification;
        set => SetProperty(ref _showEmergencyNotification, value);
    }

    public void AddRule(KeyRemapRule rule)
    {
        _module.Settings.Rules.Add(rule);
        var vm = new RuleItemViewModel(rule, this);
        Rules.Add(vm);
        _module.NotifyRulesChanged();
        ValidateConflicts();
    }

    public void DeleteRule(RuleItemViewModel item)
    {
        _module.Settings.Rules.Remove(item.Rule);
        Rules.Remove(item);
        _module.NotifyRulesChanged();
        ValidateConflicts();
    }

    public void EditRule(RuleItemViewModel item)
    {
        OpenEditDialogRequested?.Invoke(item.Rule.Clone(), updatedRule =>
        {
            var idx = _module.Settings.Rules.FindIndex(r => r.Id == item.Rule.Id);
            if (idx >= 0)
            {
                _module.Settings.Rules[idx] = updatedRule;
                var newVm = new RuleItemViewModel(updatedRule, this);
                var uiIdx = Rules.IndexOf(item);
                if (uiIdx >= 0) Rules[uiIdx] = newVm;
                _module.NotifyRulesChanged();
                ValidateConflicts();
            }
        });
    }

    public event Action<KeyRemapRule, Action<KeyRemapRule>>? OpenEditDialogRequested;

    public void OnRuleStateChanged()
    {
        _module.NotifyRulesChanged();
        ValidateConflicts();
    }

    private void OnModuleRulesChanged()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Rules.Clear();
            foreach (var r in _module.Settings.Rules)
            {
                Rules.Add(new RuleItemViewModel(r, this));
            }
            ValidateConflicts();
        });
    }

    private void OnEmergencyEscapeTriggered(string message)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            EmergencyNotification = message;
            ShowEmergencyNotification = true;
            OnModuleRulesChanged();
        });
    }

    public void ValidateConflicts()
    {
        // Check duplicate active triggers
        var activeRules = Rules.Where(r => r.IsEnabled).ToList();
        var groups = activeRules.GroupBy(r => $"{r.Rule.TriggerVirtualKey}_{r.Rule.TriggerCtrl}_{r.Rule.TriggerAlt}_{r.Rule.TriggerShift}_{r.Rule.TriggerWin}").ToList();

        foreach (var ruleVm in Rules)
        {
            ruleVm.HasConflict = false;
            ruleVm.ConflictWarning = string.Empty;

            if (ruleVm.IsEnabled)
            {
                var key = $"{ruleVm.Rule.TriggerVirtualKey}_{ruleVm.Rule.TriggerCtrl}_{ruleVm.Rule.TriggerAlt}_{ruleVm.Rule.TriggerShift}_{ruleVm.Rule.TriggerWin}";
                var matchingGroup = groups.FirstOrDefault(g => g.Key == key);
                if (matchingGroup != null && matchingGroup.Count() > 1)
                {
                    ruleVm.HasConflict = true;
                    ruleVm.ConflictWarning = "Конфликт: одна и та же клавиша или комбинация задана в нескольких активных правилах!";
                }
            }

            ruleVm.RefreshProperties();
        }
    }
}
