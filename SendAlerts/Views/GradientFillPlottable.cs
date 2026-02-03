using System;
using System.Collections.Generic;
using ScottPlot;
using SkiaSharp;

namespace SendAlerts.Views;

/// <summary>
/// Custom IPlottable that draws a gradient-filled area under a scatter line.
/// The gradient is anchored to the chart frame (Y=max → Y=0), not the data line,
/// producing a "container fill" effect like Windows Task Manager.
/// </summary>
public sealed class GradientFillPlottable : IPlottable
{
    private readonly double[] _xs;
    private readonly double[] _ys;
    private readonly SKColor _colorTop;
    private readonly SKColor _colorBottom;

    public bool IsVisible { get; set; } = true;
    public IAxes Axes { get; set; } = ScottPlot.Axes.Default;
    public IEnumerable<LegendItem> LegendItems => [];

    public GradientFillPlottable(double[] xs, double[] ys, Color lineColor, byte alphaTop, byte alphaBottom)
    {
        _xs = xs;
        _ys = ys;
        _colorTop = new SKColor(lineColor.Red, lineColor.Green, lineColor.Blue, alphaTop);
        _colorBottom = new SKColor(lineColor.Red, lineColor.Green, lineColor.Blue, alphaBottom);
    }

    public AxisLimits GetAxisLimits() => AxisLimits.NoLimits;

    public void Render(RenderPack rp)
    {
        var dataRect = Axes.DataRect;
        if (dataRect.Width < 1 || dataRect.Height < 1) return;

        var canvas = rp.Canvas;

        // Build a path: line data points → bottom-right → bottom-left → close
        using var path = new SKPath();
        bool started = false;

        for (int i = 0; i < _xs.Length; i++)
        {
            var y = _ys[i];
            if (double.IsNaN(y)) y = 0;

            var px = Axes.GetPixelX(_xs[i]);
            var py = Axes.GetPixelY(y);

            if (!started)
            {
                path.MoveTo(px, py);
                started = true;
            }
            else
            {
                path.LineTo(px, py);
            }
        }

        if (!started) return;

        // Close polygon along the bottom edge (Y=0)
        var pxRight = Axes.GetPixelX(_xs[^1]);
        var pyBottom = Axes.GetPixelY(0);
        var pxLeft = Axes.GetPixelX(_xs[0]);

        path.LineTo(pxRight, pyBottom);
        path.LineTo(pxLeft, pyBottom);
        path.Close();

        // Gradient anchored to chart frame: top of data area → bottom of data area
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, dataRect.Top),
            new SKPoint(0, dataRect.Bottom),
            [_colorTop, _colorBottom],
            [0f, 1f],
            SKShaderTileMode.Clamp);

        using var paint = new SKPaint
        {
            Shader = shader,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        canvas.Save();
        canvas.ClipRect(new SKRect(dataRect.Left, dataRect.Top, dataRect.Right, dataRect.Bottom));
        canvas.DrawPath(path, paint);
        canvas.Restore();
    }
}
