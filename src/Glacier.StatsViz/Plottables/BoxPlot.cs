namespace Glacier.StatsViz.Plottables;

using System;
using Glacier.Plot.Core;
using Glacier.Plot.Plottables;
using Glacier.StatsViz.Stats;
using SkiaSharp;

/// <summary>
/// Classical Tukey box-and-whisker plot displaying medians, IQR boxes, whiskers, and outliers.
/// </summary>
public sealed class BoxPlot : IPlottable
{
    private readonly float _centerX;
    private readonly float _boxWidth;
    private readonly SummaryStats _stats;

    public string? Label { get; set; }
    public PlotStyle Style { get; set; } = new() { Color = Colors.SteelBlue, FillAlpha = 180 };
    public bool ShowOutliers { get; set; } = true;

    public BoxPlot(float centerX, ReadOnlySpan<float> samples, float boxWidth = 0.6f, float whiskerMultiplier = 1.5f, PlotStyle? style = null)
    {
        _centerX = centerX;
        _boxWidth = boxWidth;
        _stats = DescriptiveStats.Compute(samples, whiskerMultiplier);
        if (style != null) Style = style;
    }

    public AxisLimits GetLimits()
    {
        float minX = _centerX - _boxWidth * 0.5f;
        float maxX = _centerX + _boxWidth * 0.5f;
        float minY = _stats.Min;
        float maxY = _stats.Max;

        return new AxisLimits(minX, maxX, minY, maxY).WithPadding(0.05, 0.05);
    }

    public void Render(SKCanvas canvas, CoordinateConverter converter, PlotTheme theme)
    {
        if (_stats.Count == 0) return;

        float pxCenter = converter.GetPixelX(_centerX);
        float pxLeft = converter.GetPixelX(_centerX - _boxWidth * 0.5f);
        float pxRight = converter.GetPixelX(_centerX + _boxWidth * 0.5f);

        float pyQ1 = converter.GetPixelY(_stats.Q1);
        float pyQ3 = converter.GetPixelY(_stats.Q3);
        float pyMedian = converter.GetPixelY(_stats.Median);
        float pyLowWhisker = converter.GetPixelY(_stats.LowerWhisker);
        float pyHighWhisker = converter.GetPixelY(_stats.UpperWhisker);

        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = Style.Color,
            StrokeWidth = 1.5f,
            IsAntialias = true
        };

        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = Style.Color.WithAlpha(Style.FillAlpha),
            IsAntialias = true
        };

        // 1. Whisker lines
        canvas.DrawLine(pxCenter, pyQ3, pxCenter, pyHighWhisker, strokePaint);
        canvas.DrawLine(pxCenter, pyQ1, pxCenter, pyLowWhisker, strokePaint);

        // Whisker crossbar caps
        float capHalfWidth = (pxRight - pxLeft) * 0.25f;
        canvas.DrawLine(pxCenter - capHalfWidth, pyHighWhisker, pxCenter + capHalfWidth, pyHighWhisker, strokePaint);
        canvas.DrawLine(pxCenter - capHalfWidth, pyLowWhisker, pxCenter + capHalfWidth, pyLowWhisker, strokePaint);

        // 2. IQR Box
        float top = Math.Min(pyQ1, pyQ3);
        float bottom = Math.Max(pyQ1, pyQ3);
        var boxRect = new SKRect(pxLeft, top, pxRight, bottom);
        canvas.DrawRect(boxRect, fillPaint);
        canvas.DrawRect(boxRect, strokePaint);

        // 3. Median Line
        using var medianPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = Colors.White,
            StrokeWidth = 2.5f,
            IsAntialias = true
        };
        canvas.DrawLine(pxLeft, pyMedian, pxRight, pyMedian, medianPaint);

        // 4. Outlier points
        if (ShowOutliers && _stats.Outliers.Length > 0)
        {
            using var outlierPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = Colors.Crimson,
                IsAntialias = true
            };

            foreach (var outVal in _stats.Outliers)
            {
                float pyOut = converter.GetPixelY(outVal);
                canvas.DrawCircle(pxCenter, pyOut, 3.5f, outlierPaint);
            }
        }
    }
}
