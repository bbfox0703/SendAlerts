using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using SendAlerts.ViewModels;
using SendAlerts.Services;
using Serilog;

namespace SendAlerts.Views;

public partial class MainView : UserControl
{
    private AvaPlot? _utilizationChart;
    private AvaPlot? _temperatureChart;
    private AvaPlot? _powerChart;

    private DataStreamer? _utilizationStreamer;
    private DataStreamer? _temperatureStreamer;
    private DataStreamer? _powerStreamer;

    // Fixed Y-axis ranges per chart
    private double _powerYMax = 600;

    private MainViewModel? _vm;

    public MainView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _utilizationChart = this.FindControl<AvaPlot>("UtilizationChart");
        _temperatureChart = this.FindControl<AvaPlot>("TemperatureChart");
        _powerChart = this.FindControl<AvaPlot>("PowerChart");

        if (_utilizationChart is null || _temperatureChart is null || _powerChart is null)
            return;

        _vm = DataContext as MainViewModel;
        if (_vm is null) return;

        var capacity = _vm.BufferCount > 0 ? 1800 : 1800; // use max 30 min capacity

        // Initialize streamers
        _utilizationStreamer = SetupChart(_utilizationChart, Colors.LimeGreen, 0, 100);
        _temperatureStreamer = SetupChart(_temperatureChart, Colors.OrangeRed, 0, 100);
        _powerStreamer = SetupChart(_powerChart, Colors.Cyan, 0, 600);

        // Subscribe to data updates
        _vm.ChartDataUpdated += OnChartDataUpdated;
    }

    private DataStreamer SetupChart(AvaPlot chart, Color lineColor, double yMin, double yMax)
    {
        var plot = chart.Plot;

        // Dark theme
        plot.FigureBackground.Color = Color.FromHex("#1a1a1a");
        plot.DataBackground.Color = Color.FromHex("#1a1a1a");
        plot.Axes.Color(Color.FromHex("#888888"));
        plot.Grid.MajorLineColor = Color.FromHex("#333333");
        plot.HideGrid();

        // Hide top/right axes
        plot.Axes.Top.IsVisible = false;
        plot.Axes.Right.IsVisible = false;

        // Y axis limits
        plot.Axes.SetLimitsY(yMin, yMax);

        // Hide X axis labels (time shown in header)
        plot.Axes.Bottom.IsVisible = false;

        // Create DataStreamer with 30-min capacity (at 1s interval = 1800 points)
        var streamer = plot.Add.DataStreamer(1800);
        streamer.Color = lineColor;
        streamer.LineWidth = 1;
        streamer.ViewScrollLeft();
        streamer.ManageAxisLimits = true;

        chart.Refresh();
        return streamer;
    }

    private void OnChartDataUpdated()
    {
        if (_vm is null || _utilizationStreamer is null || _temperatureStreamer is null || _powerStreamer is null)
            return;

        _utilizationStreamer.Add(_vm.CurrentUtilization);
        _temperatureStreamer.Add(_vm.CurrentTemperature);
        _powerStreamer.Add(_vm.CurrentPower);

        // Dynamic Y max for power chart
        if (_vm.CurrentPower > _powerYMax * 0.9)
        {
            _powerYMax = _vm.IsGpuMode
                ? _vm.CurrentPower + 50
                : Math.Max(_vm.CurrentPower * 1.5, _powerYMax + 1000);
        }

        // Refresh then enforce fixed Y ranges (ManageAxisLimits handles X scroll)
        _utilizationChart?.Refresh();
        _utilizationChart?.Plot.Axes.SetLimitsY(0, 100);

        _temperatureChart?.Refresh();
        _temperatureChart?.Plot.Axes.SetLimitsY(0, 100);

        _powerChart?.Refresh();
        _powerChart?.Plot.Axes.SetLimitsY(0, _powerYMax);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Unsubscribe from old VM
        if (_vm is not null)
            _vm.ChartDataUpdated -= OnChartDataUpdated;

        _vm = DataContext as MainViewModel;

        // Re-subscribe if charts are already initialized
        if (_vm is not null && _utilizationStreamer is not null)
            _vm.ChartDataUpdated += OnChartDataUpdated;
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var settingsService = ServiceLocator.SettingsService ?? new JsonSettingsService();
            var viewModel = new SettingsViewModel(settingsService);
            var settingsWindow = new SettingsWindow(viewModel);

            // Get the parent window
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow != null)
            {
                await settingsWindow.ShowDialog(parentWindow);

                // 設定儲存後，套用新的取樣間隔
                if (DataContext is MainViewModel mainVm)
                {
                    var settings = settingsService.Load();
                    mainVm.UpdateSamplingInterval(settings.SamplingIntervalSeconds);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainView] 開啟設定視窗失敗");
        }
    }

    private async void OnAlertActionsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var alertActionsWindow = new AlertActionsWindow();

            // Get the parent window
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow != null)
            {
                await alertActionsWindow.ShowDialog(parentWindow);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainView] 開啟 Alert Actions 視窗失敗");
        }
    }

    private async void OnAlertGroupsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var alertGroupsWindow = new AlertGroupsWindow();

            // Get the parent window
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow != null)
            {
                await alertGroupsWindow.ShowDialog(parentWindow);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainView] 開啟 Alert Groups 視窗失敗");
        }
    }

    private void OnLogClick(object? sender, RoutedEventArgs e)
    {
        var logWindow = new LogWindow();
        logWindow.Show();
    }
}
