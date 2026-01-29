using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SendAlerts.Services;

namespace SendAlerts.ViewModels;

public partial class LogViewModel : ViewModelBase
{
    private readonly LocalizationService _loc = LocalizationService.Instance;

    public string Loc_Title => _loc["Log_Title"];
    public string Loc_Clear => _loc["Log_Clear"];
    public string Loc_Close => _loc["Close"];

    public ObservableCollection<string> LogEntries => InMemoryLogSink.Instance.LogEntries;

    [ObservableProperty] private bool _autoScroll = true;

    [RelayCommand]
    private void Clear() => InMemoryLogSink.Instance.Clear();
}
