using System;
using Avalonia.Controls;
using Avalonia.Input;
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

    // Fixed-length arrays for plots (shared by reference with ScottPlot)
    private readonly double[] _xs;
    private readonly double[] _utilizationData = new double[Capacity];
    private readonly double[] _temperatureData = new double[Capacity];
    private readonly double[] _powerData = new double[Capacity];

    // Chart info bundles
    private ChartInfo? _utilInfo;
    private ChartInfo? _tempInfo;
    private ChartInfo? _powerInfo;

    // Current sampling interval (for tooltip time calculation)
    private int _currentInterval = 1;

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
        var utilizationChart = this.FindControl<AvaPlot>("UtilizationChart");
        var temperatureChart = this.FindControl<AvaPlot>("TemperatureChart");
        var powerChart = this.FindControl<AvaPlot>("PowerChart");

        if (utilizationChart is null || temperatureChart is null || powerChart is null)
            return;

        _vm = DataContext as MainViewModel;
        if (_vm is null) return;

        _currentInterval = ServiceLocator.SettingsService?.Load().SamplingIntervalSeconds ?? 1;

        _utilInfo = SetupChart(utilizationChart, _utilizationData, Colors.LimeGreen, 0, 100);
        _tempInfo = SetupChart(temperatureChart, _temperatureData, Colors.OrangeRed, 0, 100);
        _powerInfo = SetupChart(powerChart, _powerData, Colors.Cyan, 0, _vm.PowerChartYMax);

        _vm.ChartDataUpdated += OnChartDataUpdated;
        _vm.ChartDataCleared += OnChartDataCleared;
        _vm.SamplingIntervalChanged += OnSamplingIntervalChanged;
    }

    private ChartInfo SetupChart(AvaPlot chart, double[] data, Color lineColor, double yMin, double yMax)
    {
        var plot = chart.Plot;

        // Disable benchmark text ("Rendered in X ms")
        plot.Benchmark.IsVisible = false;

        // Disable default user interaction (pan, zoom, right-click menu)
        chart.UserInputProcessor.IsEnabled = false;

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

        // X axis — dynamic time labels based on interval
        var (tickPositions, tickLabels) = CalculateXTicks(_currentInterval);
        plot.Axes.Bottom.SetTicks(tickPositions, tickLabels);
        plot.Axes.Bottom.TickLabelStyle.ForeColor = Color.FromHex("#666666");
        plot.Axes.Bottom.TickLabelStyle.FontSize = 10;
        plot.Axes.Bottom.MajorTickStyle.Length = 4;
        plot.Axes.Bottom.MinorTickStyle.Length = 0;

        // X axis range fixed
        plot.Axes.SetLimitsX(0, Capacity);

        // Padding: left for Y labels, bottom for X time labels
        plot.Layout.Fixed(new PixelPadding(45, 8, 20, 5));

        // Scatter plot with fill
        var scatter = plot.Add.Scatter(_xs, data);
        scatter.Color = lineColor;
        scatter.LineWidth = 1;
        scatter.MarkerSize = 0;

        // Fill under the curve
        scatter.FillY = true;
        scatter.FillYValue = 0;
        scatter.FillYColor = lineColor.WithAlpha(40);

        // Crosshair tooltip (hidden by default)
        var crosshair = plot.Add.Crosshair(0, 0);
        crosshair.IsVisible = false;
        crosshair.LineColor = Colors.White.WithAlpha(120);
        crosshair.LineWidth = 1;
        crosshair.LinePattern = LinePattern.Dashed;
        crosshair.MarkerShape = MarkerShape.FilledCircle;
        crosshair.MarkerSize = 6;
        crosshair.MarkerColor = lineColor;
        crosshair.TextColor = Colors.White;
        crosshair.TextBackgroundColor = lineColor.WithAlpha(200);
        crosshair.FontSize = 12;

        // Tooltip annotation (positioned near crosshair, hidden by default)
        var tooltip = plot.Add.Annotation("");
        tooltip.IsVisible = false;
        tooltip.LabelFontColor = Colors.White;
        tooltip.LabelBackgroundColor = Color.FromHex("#333333").WithAlpha(220);
        tooltip.LabelFontSize = 12;
        tooltip.LabelBorderColor = lineColor;
        tooltip.LabelBorderWidth = 1;
        tooltip.LabelPadding = 4;

        // Hook pointer events for tooltip
        chart.PointerMoved += (_, args) => OnChartPointerMoved(chart, data, crosshair, tooltip, args);
        chart.PointerExited += (_, _) => OnChartPointerExited(chart, crosshair, tooltip);

        chart.Refresh();

        return new ChartInfo(chart, data, scatter, crosshair, tooltip);
    }

    private static (double[] positions, string[] labels) CalculateXTicks(int intervalSeconds)
    {
        var totalSeconds = Capacity * intervalSeconds;
        var positions = new double[7];
        var labels = new string[7];
        for (int i = 0; i < 7; i++)
        {
            positions[i] = Capacity * (6 - i) / 6.0;
            var secsFromEnd = totalSeconds * i / 6;
            labels[i] = secsFromEnd == 0 ? "0"
                      : secsFromEnd < 60 ? $"-{secsFromEnd}s"
                      : secsFromEnd % 3600 == 0 ? $"-{secsFromEnd / 3600}h"
                      : secsFromEnd % 60 == 0 ? $"-{secsFromEnd / 60}m"
                      : $"-{secsFromEnd / 60}m{secsFromEnd % 60}s";
        }
        return (positions, labels);
    }

    private void OnSamplingIntervalChanged(int seconds)
    {
        _currentInterval = seconds;
        var (positions, labels) = CalculateXTicks(seconds);
        UpdateChartXTicks(_utilInfo, positions, labels);
        UpdateChartXTicks(_tempInfo, positions, labels);
        UpdateChartXTicks(_powerInfo, positions, labels);
    }

    private static void UpdateChartXTicks(ChartInfo? info, double[] positions, string[] labels)
    {
        if (info is null) return;
        info.Chart.Plot.Axes.Bottom.SetTicks(positions, labels);
        info.Chart.Refresh();
    }

    private void OnChartPointerMoved(AvaPlot chart, double[] data, Crosshair crosshair, Annotation tooltip, PointerEventArgs args)
    {
        try
        {
            var pos = args.GetPosition(chart);
            var pixel = new Pixel((float)pos.X, (float)pos.Y);
            var coord = chart.Plot.GetCoordinates(pixel, chart.Plot.Axes.Bottom, chart.Plot.Axes.Left);
            var index = (int)Math.Round(coord.X);

            if (index < 0 || index >= Capacity)
            {
                crosshair.IsVisible = false;
                tooltip.IsVisible = false;
                chart.Refresh();
                return;
            }

            var value = data[index];
            crosshair.IsVisible = true;
            crosshair.Position = new Coordinates(index, value);

            // Format tooltip: show value and time offset
            var secondsAgo = (Capacity - index) * _currentInterval;
            var timeLabel = secondsAgo < 60
                ? $"{secondsAgo}s ago"
                : $"{secondsAgo / 60}m {secondsAgo % 60}s ago";
            tooltip.IsVisible = true;
            tooltip.Text = $"{value:F1}  ({timeLabel})";
            // Flip tooltip side: right-half → show left, left-half → show right
            if (index > Capacity / 2)
            {
                tooltip.Alignment = Alignment.UpperRight;
                tooltip.OffsetX = (float)(chart.Bounds.Width - pos.X) + 15;
                tooltip.OffsetY = (float)pos.Y - 10;
            }
            else
            {
                tooltip.Alignment = Alignment.UpperLeft;
                tooltip.OffsetX = (float)pos.X + 15;
                tooltip.OffsetY = (float)pos.Y - 10;
            }

            chart.Refresh();
        }
        catch
        {
            crosshair.IsVisible = false;
            tooltip.IsVisible = false;
        }
    }

    private static void OnChartPointerExited(AvaPlot chart, Crosshair crosshair, Annotation tooltip)
    {
        crosshair.IsVisible = false;
        tooltip.IsVisible = false;
        chart.Refresh();
    }

    private void OnChartDataCleared()
    {
        Array.Clear(_utilizationData);
        Array.Clear(_temperatureData);
        Array.Clear(_powerData);

        // Hide crosshairs/tooltips
        HideCrosshair(_utilInfo);
        HideCrosshair(_tempInfo);
        HideCrosshair(_powerInfo);

        // Refresh with updated Y limits
        if (_vm is not null)
        {
            RefreshChart(_utilInfo, 0, 100);
            RefreshChart(_tempInfo, 0, 100);
            RefreshChart(_powerInfo, 0, _vm.PowerChartYMax);
        }
    }

    private static void HideCrosshair(ChartInfo? info)
    {
        if (info is null) return;
        info.Crosshair.IsVisible = false;
        info.Tooltip.IsVisible = false;
    }

    private void OnChartDataUpdated()
    {
        if (_vm is null) return;

        // Shift left and append new value
        ShiftAndAppend(_utilizationData, _vm.CurrentUtilization);
        ShiftAndAppend(_temperatureData, _vm.CurrentTemperature);
        ShiftAndAppend(_powerData, _vm.CurrentPower);

        // Dynamic Y max for power chart (GPU and Network modes, only-grow)
        if (_powerInfo is not null)
        {
            var current = (double)_vm.CurrentPower;

            // Network mode: KB/s → MB/s 切換 (只向上不縮回)
            if (!_vm.IsGpuMode && !_vm._networkScaleMB && _vm.PowerChartYMax >= 1024)
            {
                _vm._networkScaleMB = true;
                _vm.SecondaryMetricUnit = "MB/s";
                // Reset Y max for MB/s scale
                _vm.PowerChartYMax = Math.Ceiling(current / 1024.0 * 1.5);
                if (_vm.PowerChartYMax < 1) _vm.PowerChartYMax = 1;
            }

            if (!_vm.IsGpuMode && _vm._networkScaleMB)
            {
                // 顯示值已在 KB/s，轉換為 MB/s 用於 Y 軸判斷
                var currentMB = current / 1024.0;
                if (currentMB > _vm.PowerChartYMax * 0.85)
                {
                    var newMax = Math.Ceiling(currentMB * 1.5);
                    if (newMax > _vm.PowerChartYMax)
                        _vm.PowerChartYMax = newMax;
                }
            }
            else
            {
                // GPU mode: 50W 單位; Network (KB/s): 100 單位
                var step = _vm.IsGpuMode ? 50.0 : 100.0;
                if (current > _vm.PowerChartYMax * 0.85)
                {
                    var newMax = Math.Ceiling(current * 1.5 / step) * step;
                    if (newMax > _vm.PowerChartYMax)
                        _vm.PowerChartYMax = newMax;
                }
            }
        }

        // Refresh each chart + update crosshair if visible
        RefreshChart(_utilInfo, 0, 100);
        RefreshChart(_tempInfo, 0, 100);
        RefreshChart(_powerInfo, 0, _vm.PowerChartYMax);
    }

    private void RefreshChart(ChartInfo? info, double yMin, double yMax)
    {
        if (info is null) return;

        info.Chart.Plot.Axes.SetLimitsY(yMin, yMax);

        // If crosshair visible, update Y value (data shifted since last frame)
        if (info.Crosshair.IsVisible)
        {
            var index = (int)Math.Round(info.Crosshair.Position.X);
            if (index >= 0 && index < Capacity)
            {
                var value = info.Data[index];
                info.Crosshair.Position = new Coordinates(index, value);

                // Update tooltip text
                var secondsAgo = (Capacity - index) * _currentInterval;
                var timeLabel = secondsAgo < 60
                    ? $"{secondsAgo}s ago"
                    : $"{secondsAgo / 60}m {secondsAgo % 60}s ago";
                info.Tooltip.Text = $"{value:F1}  ({timeLabel})";
            }
        }

        info.Chart.Refresh();
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
        {
            _vm.ChartDataUpdated -= OnChartDataUpdated;
            _vm.ChartDataCleared -= OnChartDataCleared;
            _vm.SamplingIntervalChanged -= OnSamplingIntervalChanged;
        }

        _vm = DataContext as MainViewModel;

        if (_vm is not null && _utilInfo is not null)
        {
            _vm.ChartDataUpdated += OnChartDataUpdated;
            _vm.ChartDataCleared += OnChartDataCleared;
            _vm.SamplingIntervalChanged += OnSamplingIntervalChanged;
        }
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var settingsService = ServiceLocator.SettingsService ?? new JsonSettingsService();
            var oldInterval = settingsService.Load().SamplingIntervalSeconds;

            var viewModel = new SettingsViewModel(settingsService);
            var settingsWindow = new SettingsWindow(viewModel);

            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow != null)
            {
                await settingsWindow.ShowDialog(parentWindow);

                if (DataContext is MainViewModel mainVm)
                {
                    var settings = settingsService.Load();
                    var newInterval = settings.SamplingIntervalSeconds;

                    if (newInterval != oldInterval)
                    {
                        // Interval changed — show confirm dialog before clearing chart
                        var loc = LocalizationService.Instance;
                        var dialog = new ConfirmDialog(
                            loc["Confirm_IntervalChange_Title"],
                            string.Format(loc["Confirm_IntervalChange_Message"], oldInterval, newInterval),
                            loc["OK"],
                            loc["Cancel"]);
                        await dialog.ShowDialog(parentWindow);

                        if (dialog.IsConfirmed)
                        {
                            mainVm.UpdateSamplingInterval(newInterval);
                        }
                        else
                        {
                            // Revert setting
                            settings.SamplingIntervalSeconds = oldInterval;
                            settingsService.Save(settings);
                        }
                    }
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

    private async void OnDiagnosticsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var diagWindow = new DiagnosticsWindow();
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow != null)
                await diagWindow.ShowDialog(parentWindow);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainView] 開啟診斷視窗失敗");
        }
    }

    private sealed record ChartInfo(AvaPlot Chart, double[] Data, Scatter Scatter, Crosshair Crosshair, Annotation Tooltip);
}
