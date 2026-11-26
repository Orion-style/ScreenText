using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ZenithApp.Modules.Screenshot;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.NextSession;

public sealed class NextSessionWindowViewModel : ViewModelBase, IDisposable
{
    private readonly NextSessionModule _module;
    private readonly INextSessionProgramCatalogService _catalogService;
    private readonly List<NextSessionProgramItem> _allPrograms = new();
    private readonly List<NextSessionProgramEntry> _customPrograms = new();

    private string _message = string.Empty;
    private string _searchText = string.Empty;
    private bool _isLoading = true;
    private bool _disposed;

    public NextSessionWindowViewModel(
        NextSessionModule module,
        INextSessionProgramCatalogService catalogService,
        bool loadImmediately = false)
    {
        _module = module;
        _catalogService = catalogService;

        var settings = _module.Settings;
        _message = settings.PendingMessage ?? string.Empty;
        _customPrograms.AddRange(settings.CustomPrograms ?? Enumerable.Empty<NextSessionProgramEntry>());

        FilteredPrograms = new ObservableCollection<NextSessionProgramItem>();

        SelectRunningCommand = new RelayCommand(SelectRunningPrograms);
        ClearSelectionCommand = new RelayCommand(ClearProgramSelection);
        ClearPlanCommand = new RelayCommand(ClearSavedPlan);

        if (loadImmediately)
        {
            PopulateCatalog(_catalogService.LoadAvailablePrograms(_customPrograms, settings.PendingPrograms));
            IsLoading = false;
        }
    }

    public ObservableCollection<NextSessionProgramItem> FilteredPrograms { get; }

    public ICommand SelectRunningCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand ClearPlanCommand { get; }

    public string Message
    {
        get => _message;
        set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public int SelectedCount => _allPrograms.Count(p => p.IsSelected);

    public string SelectedCountText => SelectedCount == 0
        ? "Ничего не выбрано"
        : $"Выбрано: {SelectedCount}";

    public bool HasActivePlan => _module.Settings.HasPendingPlan;

    public string PlanStatusBadgeText => HasActivePlan
        ? "Есть активный план на следующий запуск"
        : "Новый план";

    public bool CanSave => !string.IsNullOrWhiteSpace(Message) || SelectedCount > 0;

    public int StartupDelaySeconds
    {
        get => _module.Settings.StartupDelaySeconds;
        set
        {
            var clamped = Math.Clamp(value, 0, 30);
            if (_module.Settings.StartupDelaySeconds != clamped)
            {
                _module.UpdateStartupDelaySeconds(clamped);
                OnPropertyChanged();
                OnPropertyChanged(nameof(StartupDelayText));
            }
        }
    }

    public string StartupDelayText => StartupDelaySeconds == 0
        ? "Сразу (0 сек)"
        : $"{StartupDelaySeconds} сек";

    public bool TriggerOnResumeFromSleep
    {
        get => _module.Settings.TriggerOnResumeFromSleep;
        set
        {
            if (_module.Settings.TriggerOnResumeFromSleep != value)
            {
                _module.UpdateTriggerOnResumeFromSleep(value);
                OnPropertyChanged();
            }
        }
    }

    public bool HasNoFilteredResults => !IsLoading && FilteredPrograms.Count == 0;

    public async Task LoadProgramsAsync()
    {
        IsLoading = true;
        var customSnapshot = _customPrograms.ToList();
        var pendingSnapshot = _module.Settings.PendingPrograms.ToList();

        var loaded = await Task.Run(() =>
            _catalogService.LoadAvailablePrograms(customSnapshot, pendingSnapshot));

        if (_disposed) return;

        PopulateCatalog(loaded);
        IsLoading = false;
    }

    private void PopulateCatalog(IReadOnlyList<NextSessionProgramItem> loaded)
    {
        foreach (var existing in _allPrograms)
        {
            existing.PropertyChanged -= OnProgramItemPropertyChanged;
        }

        _allPrograms.Clear();

        var pendingKeys = new HashSet<string>(
            (_module.Settings.PendingPrograms ?? Enumerable.Empty<NextSessionProgramEntry>())
                .Select(p => p.GetNormalizedKey()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in loaded)
        {
            if (pendingKeys.Contains(item.Key))
            {
                item.IsSelected = true;
            }

            item.PropertyChanged += OnProgramItemPropertyChanged;
            _allPrograms.Add(item);
        }

        ApplyFilter();
        NotifySelectionMetricsChanged();
    }

    public void AddCustomProgram(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        var trimmedPath = filePath.Trim();
        var name = Path.GetFileNameWithoutExtension(trimmedPath);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = trimmedPath;
        }

        var entry = new NextSessionProgramEntry
        {
            Name = name,
            ExecutablePath = trimmedPath,
            ProcessName = name
        };
        var key = entry.GetNormalizedKey();

        var existing = _allPrograms.FirstOrDefault(p =>
            string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.IsSelected = true;
            NotifySelectionMetricsChanged();
            return;
        }

        if (!_customPrograms.Any(c => string.Equals(c.GetNormalizedKey(), key, StringComparison.OrdinalIgnoreCase)))
        {
            _customPrograms.Add(entry);
        }

        var item = new NextSessionProgramItem
        {
            Key = key,
            Name = name,
            ExecutablePath = trimmedPath,
            ProcessName = name,
            Icon = _catalogService.ResolveIcon(trimmedPath, name),
            IsCustom = true,
            IsSelected = true
        };
        item.PropertyChanged += OnProgramItemPropertyChanged;
        _allPrograms.Insert(0, item);

        ApplyFilter();
        NotifySelectionMetricsChanged();
    }

    public void SelectRunningPrograms()
    {
        foreach (var item in _allPrograms)
        {
            if (item.IsRunning)
            {
                item.IsSelected = true;
            }
        }
        NotifySelectionMetricsChanged();
    }

    public void ClearProgramSelection()
    {
        foreach (var item in _allPrograms)
        {
            item.IsSelected = false;
        }
        NotifySelectionMetricsChanged();
    }

    public bool SavePlan(bool showToast = true)
    {
        var selectedEntries = GetSelectedProgramEntries();

        _module.SavePlan(Message, selectedEntries, _customPrograms);
        OnPropertyChanged(nameof(HasActivePlan));
        OnPropertyChanged(nameof(PlanStatusBadgeText));

        if (showToast && _module.Settings.HasPendingPlan)
        {
            var delayNote = StartupDelaySeconds > 0
                ? $" Таймер запуска: {StartupDelaySeconds} сек."
                : string.Empty;
            NotificationToast.Show(
                $"Запланировано программ: {selectedEntries.Count}.{delayNote} При следующем запуске появится ваше уведомление.",
                "Следующий сеанс сохранён",
                ToastNotificationType.Success,
                durationSeconds: 4);
        }

        return true;
    }

    public bool SaveAndRestart()
    {
        return _module.SaveAndRestart(Message, GetSelectedProgramEntries(), _customPrograms);
    }

    public bool SaveAndShutdown()
    {
        return _module.SaveAndShutdown(Message, GetSelectedProgramEntries(), _customPrograms);
    }

    private List<NextSessionProgramEntry> GetSelectedProgramEntries()
    {
        return _allPrograms
            .Where(p => p.IsSelected)
            .Select(p => p.ToEntry())
            .ToList();
    }

    public void ClearSavedPlan()
    {
        Message = string.Empty;
        ClearProgramSelection();
        _module.ClearPlan();
        OnPropertyChanged(nameof(HasActivePlan));
        OnPropertyChanged(nameof(PlanStatusBadgeText));
    }

    private void ApplyFilter()
    {
        FilteredPrograms.Clear();

        var query = SearchText?.Trim() ?? string.Empty;
        var source = string.IsNullOrEmpty(query)
            ? _allPrograms
            : _allPrograms.Where(p =>
                p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.ExecutablePath.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var item in source)
        {
            FilteredPrograms.Add(item);
        }

        OnPropertyChanged(nameof(HasNoFilteredResults));
    }

    private void OnProgramItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NextSessionProgramItem.IsSelected))
        {
            NotifySelectionMetricsChanged();
        }
    }

    private void NotifySelectionMetricsChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(CanSave));
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var item in _allPrograms)
        {
            item.PropertyChanged -= OnProgramItemPropertyChanged;
        }
        _allPrograms.Clear();
        FilteredPrograms.Clear();
    }
}
