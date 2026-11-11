using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using ZenithApp.UI.Common;

namespace ZenithApp.Modules.StartupManager;

public sealed class StartupManagerViewModel : ViewModelBase
{
    private readonly StartupManagerModule _module;
    private readonly IStartupItemsService _service;
    private string _searchFilter = string.Empty;
    private bool _isLoading;

    public StartupManagerViewModel(StartupManagerModule module, IStartupItemsService service)
    {
        _module = module;
        _service = service;

        Items = new ObservableCollection<StartupItemViewModel>();
        FilteredItems = new ObservableCollection<StartupItemViewModel>();

        RefreshCommand = new RelayCommand(Refresh);
        Refresh();
    }

    public ObservableCollection<StartupItemViewModel> Items { get; }
    public ObservableCollection<StartupItemViewModel> FilteredItems { get; }

    public ICommand RefreshCommand { get; }

    public string SearchFilter
    {
        get => _searchFilter;
        set
        {
            if (SetProperty(ref _searchFilter, value))
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

    public int TotalCount => Items.Count;
    public int EnabledCount => Items.Count(i => i.IsEnabled);

    public void Refresh()
    {
        IsLoading = true;
        try
        {
            Items.Clear();
            var entries = _service.GetStartupEntries();
            foreach (var entry in entries.OrderBy(e => e.Name))
            {
                var itemVm = new StartupItemViewModel(entry, _service, OnItemDeleted);
                itemVm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(StartupItemViewModel.IsEnabled))
                    {
                        OnPropertyChanged(nameof(EnabledCount));
                    }
                };
                Items.Add(itemVm);
            }

            ApplyFilter();
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(EnabledCount));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnItemDeleted(StartupItemViewModel item)
    {
        Items.Remove(item);
        FilteredItems.Remove(item);
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(EnabledCount));
    }

    private void ApplyFilter()
    {
        FilteredItems.Clear();
        var filter = _searchFilter?.Trim() ?? string.Empty;

        foreach (var item in Items)
        {
            if (string.IsNullOrEmpty(filter) ||
                item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (item.Publisher != null && item.Publisher.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                (item.FilePath != null && item.FilePath.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            {
                FilteredItems.Add(item);
            }
        }
    }
}
