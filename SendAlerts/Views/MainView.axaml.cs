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
    private const int Capacity = 1800; // 30 min at 1s interval

    private AvaPlot? _utilizationChart;
    private AvaPlot? _temperatureChart;
    private AvaPlot? _powerChart;

    // Fixed-length arrays for plots (shared by reference with ScottPlot)
    private readonly double[] _xs;
    private readonly double[] _utilizationData = new double[Capacity];
    private readonly double[] _temperatureData = new double[Capacity];
    private readonly double[] _powerData = new double[Capacity];

    private Scatter? _utilizationPlot;
    private Scatter? _temperaturePlot;
    private Scatter? _powerPlot;

    private MainViewModel? _vm;

    public MainView()
    {
        _xs = new double[Capacity];
        for (int i = 0; i < Capacity; i++) _xs[i] = i;

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

        _utilizationPlot = SetupChart(_utilizationChart, _utilizationData, Colors.LimeGreen, 0, 100);
        _temperaturePlot = SetupChart(_temperatureChart, _temperatureData, Colors.OrangeRed, 0, 100);
        _powerPlot = SetupChart(_powerChart, _powerData, Colors.Cyan, 0, _vm.PowerChartYMax);

        _vm.ChartDataUpdated += OnChartDataUpdated;
    }

    private Scatter SetupChart(AvaPlot chart, double[] data, Color lineColor, double yMin, double yMax)
    {
        var plot = chart.Plot;

        // Dark theme
        plot.FigureBackground.Color = Color.FromHex("#1a1a1a");
        plot.DataBackground.Color = Color.FromHex("#1a1a1a");
        plot.Axes.Color(Color.FromHex("#888888"));

        // Grid lines — dotted
        plot.Grid.MajorLineColor = Color.FromHex("#333333");
        plot.Grid.MajorLineWidth = 1;
        plot.Grid.MajorLinePattern = LinePattern.Dotted;

        // Hide top/right axes
        plot.Axes.Top.IsVisible = false;
        plot.Axes.Right.IsVisible = false;

        // Y axis
        plot.Axes.SetLimitsY(yMin, yMax);

        // X axis — hide tick labels but keep grid lines
        plot.Axes.Bottom.TickLabelStyle.IsVisible = false;
        plot.Axes.Bottom.MajorTickStyle.Length = 0;
        plot.Axes.Bottom.MinorTickStyle.Length = 0;

        // X axis range fixed
        plot.Axes.SetLimitsX(0, Capacity);

        // Padding to avoid Y label clipping
        plot.Layout.Fixed(new PixelPadding(45, 8, 5, 5));

        // Scatter plot with fill
        var scatter = plot.Add.Scatter(_xs, data);
        scatter.Color = lineColor;
        scatter.LineWidth = 1;
        scatter.MarkerSize = 0;

        // Fill under the curve
        scatter.FillY = true;
        scatter.FillYValue = 0;
        scatter.FillYColor = lineColor.WithAlpha(40);

        chart.Refresh();
        return scatter;
    }

    private void OnChartDataUpdated()
    {
        if (_vm is null) return;

        // Shift left and append new value
        ShiftAndAppend(_utilizationData, _vm.CurrentUtilization);
        ShiftAndAppend(_temperatureData, _vm.CurrentTemperature);
        ShiftAndAppend(_powerData, _vm.CurrentPower);

        // Dynamic Y max for power chart (CPU/Network mode)
        if (!_vm.IsGpuMode && _powerChart is not null)
        {
            if (_vm.CurrentPower > _vm.PowerChartYMax * 0.85)
            {
                _vm.PowerChartYMax = Math.Ceiling(_vm.CurrentPower * 1.5 / 100) * 100;
            }
        }

        // Refresh and enforce Y limits
        if (_utilizationChart is not null)
        {
            _utilizationChart.Plot.Axes.SetLimitsY(0, 100);
            _utilizationChart.Refresh();
        }

        if (_temperatureChart is not null)
        {
            _temperatureChart.Plot.Axes.SetLimitsY(0, 100);
            _temperatureChart.Refresh();
        }

        if (_powerChart is not null)
        {
            _powerChart.Plot.Axes.SetLimitsY(0, _vm.PowerChartYMax);
            _powerChart.Refresh();
        }
    }

    private static void ShiftAndAppend(double[] data, double newValue)
    {
        Array.Copy(data, 1, data, 0, data.Length - 1);
        data[^1] = newValue;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm is not null)
            _vm.ChartDataUpdated -= OnChartDataUpdated;

        _vm = DataContext as MainViewModel;

        if (_vm is not null && _utilizationPlot is not null)
            _vm.ChartDataUpdated += OnChartDataUpdated;
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var settingsService = ServiceLocator.SettingsService ?? new JsonSettingsService();
            var viewModel = new SettingsViewModel(settingsService);
            var settingsWindow = new SettingsWindow(viewModel);

            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow != null)
            {
                await settingsWindow.ShowDialog(parentWindow);

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
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow != null)
                await alertActionsWindow.ShowDialog(parentWindow);
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
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow != null)
                await alertGroupsWindow.ShowDialog(parentWindow);
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
