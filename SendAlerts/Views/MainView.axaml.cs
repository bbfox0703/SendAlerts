using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using SendAlerts.Models;
using SendAlerts.ViewModels;
using SendAlerts.Services;
using Serilog;

namespace SendAlerts.Views;

public partial class MainView : UserControl
{
    private const int Capacity = 1800;

    private readonly double[] _xs;
    private readonly double[][] _chartData = new double[4][];
    private ChartInfo?[] _charts = new ChartInfo?[4];

    private int _currentInterval = 1;
    private MainViewModel? _vm;

    public MainView()
    {
        _xs = new double[Capacity];
        for (int i = 0; i < Capacity; i++) _xs[i] = i;

        for (int i = 0; i < 4; i++)
        {
            _chartData[i] = new double[Capacity];
            // Default 0 — NaN breaks ScottPlot FillY rendering
        }

        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _vm = DataContext as MainViewModel;
        if (_vm is null) return;

        _currentInterval = ServiceLocator.SettingsService?.Load().SamplingIntervalSeconds ?? 1;

        // Find chart controls by name
        var chartControls = new[]
        {
            this.FindControl<AvaPlot>("Chart0"),
            this.FindControl<AvaPlot>("Chart1"),
            this.FindControl<AvaPlot>("Chart2"),
            this.FindControl<AvaPlot>("Chart3")
        };

        for (int i = 0; i < 4; i++)
        {
            if (chartControls[i] is not null)
            {
                var lineColor = Color.FromHex(ChartColors.SlotLineColors[i]);
                var yMax = i switch
                {
                    0 => _vm.Slot0YMax,
                    1 => _vm.Slot1YMax,
                    2 => _vm.Slot2YMax,
                    3 => _vm.Slot3YMax,
                    _ => 100
                };
                _charts[i] = SetupChart(chartControls[i]!, _chartData[i], lineColor, 0, yMax);
            }
        }

        _vm.ChartDataUpdated += OnChartDataUpdated;
        _vm.ChartDataCleared += OnChartDataCleared;
        _vm.SamplingIntervalChanged += OnSamplingIntervalChanged;
        _vm.OpenChartConfigRequested += OnOpenChartConfigRequested;
        _vm.PropertyChanged += OnVmPropertyChanged;

        UpdateRowVisibility();
    }

    private ChartInfo SetupChart(AvaPlot chart, double[] data, Color lineColor, double yMin, double yMax)
    {
        var plot = chart.Plot;

        plot.Benchmark.IsVisible = false;
        chart.UserInputProcessor.IsEnabled = false;

        plot.FigureBackground.Color = Color.FromHex(ChartColors.Background);
        plot.DataBackground.Color = Color.FromHex(ChartColors.Background);
        plot.Axes.Color(Color.FromHex(ChartColors.AxisColor));

        plot.Grid.MajorLineColor = Color.FromHex(ChartColors.GridLine);
        plot.Grid.MajorLineWidth = 1;
        plot.Grid.MajorLinePattern = LinePattern.Dotted;

        plot.Axes.Top.IsVisible = false;
        plot.Axes.Right.IsVisible = false;

        plot.Axes.SetLimitsY(yMin, yMax);

        var (tickPositions, tickLabels) = CalculateXTicks(_currentInterval);
        plot.Axes.Bottom.SetTicks(tickPositions, tickLabels);
        plot.Axes.Bottom.TickLabelStyle.ForeColor = Color.FromHex(ChartColors.TickLabel);
        plot.Axes.Bottom.TickLabelStyle.FontSize = 10;
        plot.Axes.Bottom.MajorTickStyle.Length = 4;
        plot.Axes.Bottom.MinorTickStyle.Length = 0;

        plot.Axes.SetLimitsX(0, Capacity);
        plot.Layout.Fixed(new PixelPadding(45, 8, 20, 5));

        var scatter = plot.Add.Scatter(_xs, data);
        scatter.Color = lineColor;
        scatter.LineWidth = 1;
        scatter.MarkerSize = 0;
        scatter.FillY = true;
        scatter.FillYValue = 0;
        scatter.FillYColor = lineColor.WithAlpha(ChartColors.FillAlpha);

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

        var tooltip = plot.Add.Annotation("");
        tooltip.IsVisible = false;
        tooltip.LabelFontColor = Colors.White;
        tooltip.LabelBackgroundColor = Color.FromHex(ChartColors.TooltipBg).WithAlpha(220);
        tooltip.LabelFontSize = 12;
        tooltip.LabelBorderColor = lineColor;
        tooltip.LabelBorderWidth = 1;
        tooltip.LabelPadding = 4;

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
        for (int i = 0; i < 4; i++)
            UpdateChartXTicks(_charts[i], positions, labels);
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
            if (double.IsNaN(value))
            {
                crosshair.IsVisible = false;
                tooltip.IsVisible = false;
                chart.Refresh();
                return;
            }
            crosshair.IsVisible = true;
            crosshair.Position = new Coordinates(index, value);

            var secondsAgo = (Capacity - index) * _currentInterval;
            var timeLabel = secondsAgo < 60
                ? $"{secondsAgo}s ago"
                : $"{secondsAgo / 60}m {secondsAgo % 60}s ago";
            tooltip.IsVisible = true;
            tooltip.Text = $"{value:F3}  ({timeLabel})";
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
        for (int i = 0; i < 4; i++)
        {
            Array.Clear(_chartData[i]);
            HideCrosshair(_charts[i]);
        }
        RefreshAllCharts();
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

        for (int i = 0; i < 4; i++)
        {
            var slot = _vm._slots[i];
            if (!slot.IsActive || _charts[i] is null) continue;

            ShiftAndAppend(_chartData[i], slot.LastTickValue);

            var yMax = i switch
            {
                0 => _vm.Slot0YMax,
                1 => _vm.Slot1YMax,
                2 => _vm.Slot2YMax,
                3 => _vm.Slot3YMax,
                _ => 100
            };
            RefreshChart(_charts[i], 0, yMax);
        }
    }

    private void RefreshAllCharts()
    {
        if (_vm is null) return;
        var yMaxes = new[] { _vm.Slot0YMax, _vm.Slot1YMax, _vm.Slot2YMax, _vm.Slot3YMax };
        for (int i = 0; i < 4; i++)
            RefreshChart(_charts[i], 0, yMaxes[i]);
    }

    private void RefreshChart(ChartInfo? info, double yMin, double yMax)
    {
        if (info is null) return;

        info.Chart.Plot.Axes.SetLimitsY(yMin, yMax);

        if (info.Crosshair.IsVisible)
        {
            var index = (int)Math.Round(info.Crosshair.Position.X);
            if (index >= 0 && index < Capacity)
            {
                var value = info.Data[index];
                if (double.IsNaN(value))
                {
                    info.Crosshair.IsVisible = false;
                    info.Tooltip.IsVisible = false;
                    info.Chart.Refresh();
                    return;
                }
                info.Crosshair.Position = new Coordinates(index, value);

                var secondsAgo = (Capacity - index) * _currentInterval;
                var timeLabel = secondsAgo < 60
                    ? $"{secondsAgo}s ago"
                    : $"{secondsAgo / 60}m {secondsAgo % 60}s ago";
                info.Tooltip.Text = $"{value:F3}  ({timeLabel})";
            }
        }

        info.Chart.Refresh();
    }

    private static void ShiftAndAppend(double[] data, double newValue)
    {
        Array.Copy(data, 1, data, 0, data.Length - 1);
        data[^1] = newValue;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Slot0IsVisible) or
            nameof(MainViewModel.Slot1IsVisible) or
            nameof(MainViewModel.Slot2IsVisible) or
            nameof(MainViewModel.Slot3IsVisible))
        {
            UpdateRowVisibility();
        }
    }

    private void UpdateRowVisibility()
    {
        var content = this.Content as Avalonia.Controls.DockPanel;
        if (content is null) return;

        Grid? mainGrid = null;
        foreach (var child in content.Children)
        {
            if (child is Grid g && g.RowDefinitions.Count >= 5)
            {
                mainGrid = g;
                break;
            }
        }
        if (mainGrid is null || _vm is null) return;

        var visible = new[] { _vm.Slot0IsVisible, _vm.Slot1IsVisible, _vm.Slot2IsVisible, _vm.Slot3IsVisible };
        for (int i = 0; i < 4; i++)
        {
            mainGrid.RowDefinitions[i].Height = visible[i]
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
        }
    }

    private async void OnOpenChartConfigRequested(int slotIndex)
    {
        if (_vm is null) return;

        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        if (parentWindow is null) return;

        var dialogVm = new ChartConfigDialogViewModel(slotIndex, _vm);
        var dialog = new ChartConfigDialog(dialogVm);
        var result = await dialog.ShowDialog<bool?>(parentWindow);

        if (result == true && dialogVm.Result is { } config)
        {
            // Duplicate check for built-in
            if (config.SourceType == ChartSlotSourceType.BuiltIn)
            {
                var usedBy = _vm.FindPresetUsedBySlot(config.BuiltInPreset, slotIndex);
                if (usedBy >= 0)
                {
                    _vm.StatusText = $"Warning: {config.BuiltInPreset} already used in Chart {usedBy + 1}";
                    return;
                }
            }

            _vm.ApplySlotConfig(slotIndex, config);

            // Clear and reinitialize the chart display data
            Array.Clear(_chartData[slotIndex]);
            if (_charts[slotIndex] is not null)
            {
                HideCrosshair(_charts[slotIndex]);
                var yMax = slotIndex switch
                {
                    0 => _vm.Slot0YMax,
                    1 => _vm.Slot1YMax,
                    2 => _vm.Slot2YMax,
                    3 => _vm.Slot3YMax,
                    _ => 100
                };
                RefreshChart(_charts[slotIndex], 0, yMax);
            }

            UpdateRowVisibility();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm is not null)
        {
            _vm.ChartDataUpdated -= OnChartDataUpdated;
            _vm.ChartDataCleared -= OnChartDataCleared;
            _vm.SamplingIntervalChanged -= OnSamplingIntervalChanged;
            _vm.OpenChartConfigRequested -= OnOpenChartConfigRequested;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = DataContext as MainViewModel;

        if (_vm is not null && _charts[0] is not null)
        {
            _vm.ChartDataUpdated += OnChartDataUpdated;
            _vm.ChartDataCleared += OnChartDataCleared;
            _vm.SamplingIntervalChanged += OnSamplingIntervalChanged;
            _vm.OpenChartConfigRequested += OnOpenChartConfigRequested;
            _vm.PropertyChanged += OnVmPropertyChanged;
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
                            settings.SamplingIntervalSeconds = oldInterval;
                            settingsService.Save(settings);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainView] Failed to open settings window");
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
            Log.Error(ex, "[MainView] Failed to open Alert Actions window");
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
            Log.Error(ex, "[MainView] Failed to open Alert Groups window");
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
            Log.Error(ex, "[MainView] Failed to open diagnostics window");
        }
    }

    private sealed record ChartInfo(AvaPlot Chart, double[] Data, Scatter Scatter, Crosshair Crosshair, Annotation Tooltip);
}
