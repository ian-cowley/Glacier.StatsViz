namespace Glacier.StatsViz.Plottables;

using System;
using Glacier.Plot.Core;
using Glacier.Plot.Plottables;
using Glacier.StatsViz.Stats;
using SkiaSharp;

/// <summary>
/// Scatter plot overlay with an Ordinary Least Squares (OLS) regression line and confidence envelope.
/// </summary>
public sealed class RegressionPlot : IPlottable
{
    private readonly float[] _x;
    private readonly float[] _y;
    private readonly LinearRegressionResult _reg;
    private readonly float _minX;
    private readonly float _maxX;

    public string? Label { get; set; }
    public PlotStyle Style { get; set; } = new() { Color = Colors.Cyan };
    public bool ShowConfidenceBand { get; set; } = true;
    public bool ShowScatter { get; set; } = true;

    public LinearRegressionResult Regression => _reg;

    public RegressionPlot(ReadOnlySpan<float> x, ReadOnlySpan<float> y, PlotStyle? style = null)
    {
        if (x.Length != y.Length)
            throw new ArgumentException("X and Y spans must have identical length.");

        _x = x.ToArray();
        _y = y.ToArray();
        _reg = RegressionKernels.FitLinear(x, y);

        if (style != null) Style = style;

        float minX = _x[0], maxX = _x[0];
        for (int i = 1; i < _x.Length; i++)
        {
            if (_x[i] < minX) minX = _x[i];
            if (_x[i] > maxX) maxX = _x[i];
        }

        if (maxX == minX) { minX -= 1f; maxX += 1f; }
        _minX = minX;
        _maxX = maxX;
    }

    public AxisLimits GetLimits()
    {
        return AxisLimits.FromData(_x, _y).WithPadding(0.05, 0.05);
    }

    public void Render(SKCanvas canvas, CoordinateConverter converter, PlotTheme theme)
    {
        if (_x.Length == 0) return;

        // 1. Draw Scatter Points
        if (ShowScatter)
        {
            using var scatterPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = Style.Color.WithAlpha(160),
                IsAntialias = true
            };

            for (int i = 0; i < _x.Length; i++)
            {
                float px = converter.GetPixelX(_x[i]);
                float py = converter.GetPixelY(_y[i]);
                canvas.DrawCircle(px, py, 3.5f, scatterPaint);
            }
        }

        // 2. Draw Confidence Interval Envelope
        const int steps = 40;
        float stepSize = (_maxX - _minX) / (steps - 1);
        float[] evalX = new float[steps];
        float[] upperY = new float[steps];
        float[] lowerY = new float[steps];

        for (int i = 0; i < steps; i++)
        {
            float curX = _minX + i * stepSize;
            evalX[i] = curX;
            var (lower, upper) = _reg.ConfidenceInterval(curX);
            lowerY[i] = lower;
            upperY[i] = upper;
        }

        if (ShowConfidenceBand)
        {
            using var bandPath = new SKPath();
            bandPath.MoveTo(converter.GetPixelX(evalX[0]), converter.GetPixelY(upperY[0]));

            for (int i = 1; i < steps; i++)
            {
                bandPath.LineTo(converter.GetPixelX(evalX[i]), converter.GetPixelY(upperY[i]));
            }

            for (int i = steps - 1; i >= 0; i--)
            {
                bandPath.LineTo(converter.GetPixelX(evalX[i]), converter.GetPixelY(lowerY[i]));
            }

            bandPath.Close();

            using var bandPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = Style.Color.WithAlpha(45),
                IsAntialias = true
            };
            canvas.DrawPath(bandPath, bandPaint);
        }

        // 3. Draw Trend Line
        using var linePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = Style.Color,
            StrokeWidth = Math.Max(2.0f, Style.StrokeWidth),
            IsAntialias = true
        };

        float px1 = converter.GetPixelX(_minX);
        float py1 = converter.GetPixelY(_reg.Predict(_minX));
        float px2 = converter.GetPixelX(_maxX);
        float py2 = converter.GetPixelY(_reg.Predict(_maxX));

        canvas.DrawLine(px1, py1, px2, py2, linePaint);
    }
}
