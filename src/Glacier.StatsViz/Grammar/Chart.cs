namespace Glacier.StatsViz.Grammar;

using System;
using System.Collections.Generic;
using System.IO;
using Glacier.Plot.Core;
using Glacier.Plot.Figures;
using Glacier.Plot.Interop;
using Glacier.Plot.Plottables;
using Glacier.Polaris;
using Glacier.Polaris.Data;
using Glacier.StatsViz.Plottables;
using Glacier.StatsViz.Stats;
using SkiaSharp;

/// <summary>
/// Declarative Grammar of Graphics chart builder for statistical visualization.
/// Directly consumes Glacier.Polaris DataFrames and compiles into Glacier.Plot figures.
/// </summary>
public sealed class Chart
{
    private readonly DataFrame _df;
    private string? _xCol;
    private string? _yCol;
    private string? _colorCol;
    private string? _sizeCol;

    private readonly List<IPlottable> _layers = new();
    private readonly Figure _figure = new();

    public Figure UnderlyingFigure => _figure;

    private Chart(DataFrame df)
    {
        _df = df;
    }

    public static Chart FromDataFrame(DataFrame df) => new(df);

    public Chart Encode(string x, string? y = null, string? color = null, string? size = null)
    {
        _xCol = x;
        _yCol = y;
        _colorCol = color;
        _sizeCol = size;

        _figure.XAxis.Label = x;
        if (y != null) _figure.YAxis.Label = y;
        return this;
    }

    public Chart WithTitle(string title)
    {
        _figure.Title = title;
        return this;
    }

    public Chart WithTheme(PlotTheme theme)
    {
        _figure.Theme = theme;
        return this;
    }

    public Chart GeomViolin(float? bandwidth = null, bool showBox = true)
    {
        if (_xCol == null || _yCol == null)
            throw new InvalidOperationException("GeomViolin requires both X and Y encoding.");

        var xSeries = _df.GetColumn(_xCol);
        var ySeries = _df.GetColumn(_yCol);
        float[] yVals = ySeries.ToFloatArray();

        // Group by X values
        var groups = GroupByCategories(xSeries, yVals);
        int categoryIndex = 0;

        foreach (var (catName, samples) in groups)
        {
            var color = _figure.Theme.Palette[categoryIndex % _figure.Theme.Palette.Length];
            var style = new PlotStyle { Color = color, FillAlpha = 150 };
            var violin = new ViolinPlot(categoryIndex, samples.ToArray(), 0.8f, bandwidth, style)
            {
                Label = catName,
                ShowBox = showBox
            };
            _figure.AddPlottable(violin);
            categoryIndex++;
        }

        return this;
    }

    public Chart GeomBox(float whiskerMultiplier = 1.5f)
    {
        if (_xCol == null || _yCol == null)
            throw new InvalidOperationException("GeomBox requires both X and Y encoding.");

        var xSeries = _df.GetColumn(_xCol);
        var ySeries = _df.GetColumn(_yCol);
        float[] yVals = ySeries.ToFloatArray();

        var groups = GroupByCategories(xSeries, yVals);
        int categoryIndex = 0;

        foreach (var (catName, samples) in groups)
        {
            var color = _figure.Theme.Palette[categoryIndex % _figure.Theme.Palette.Length];
            var style = new PlotStyle { Color = color, FillAlpha = 180 };
            var box = new BoxPlot(categoryIndex, samples.ToArray(), 0.6f, whiskerMultiplier, style)
            {
                Label = catName
            };
            _figure.AddPlottable(box);
            categoryIndex++;
        }

        return this;
    }

    public Chart GeomKde(float? bandwidth = null, bool fill = true)
    {
        if (_xCol == null)
            throw new InvalidOperationException("GeomKde requires X encoding.");

        float[] vals = _df.GetColumn(_xCol).ToFloatArray();
        var style = new PlotStyle { Color = Colors.Cyan, IsFilled = fill, FillAlpha = 90 };
        var kde = new KdePlot(vals, 200, bandwidth, style) { Label = _xCol };
        _figure.AddPlottable(kde);
        _figure.YAxis.Label = "Density";
        return this;
    }

    public Chart GeomScatter(float alpha = 0.7f)
    {
        if (_xCol == null || _yCol == null)
            throw new InvalidOperationException("GeomScatter requires both X and Y encoding.");

        float[] x = _df.GetColumn(_xCol).ToFloatArray();
        float[] y = _df.GetColumn(_yCol).ToFloatArray();

        var style = new PlotStyle { Color = Colors.Amber, Marker = MarkerShape.Circle, MarkerSize = 6.0f };
        var scatter = new ScatterPlot(x, y, style) { Label = $"{_yCol} vs {_xCol}" };
        _figure.AddPlottable(scatter);
        return this;
    }

    public Chart GeomHistogram(int bins = 30, bool density = false)
    {
        if (_xCol == null)
            throw new InvalidOperationException("GeomHistogram requires X encoding.");

        float[] vals = _df.GetColumn(_xCol).ToFloatArray();
        var style = new PlotStyle { Color = Colors.Emerald, IsFilled = true, FillAlpha = 180 };
        var hist = new HistogramPlot(vals, bins, density ? HistogramType.Density : HistogramType.Count, style)
        {
            Label = _xCol
        };
        _figure.AddPlottable(hist);
        _figure.YAxis.Label = density ? "Density" : "Count";
        return this;
    }

    public Chart GeomHeatmap()
    {
        var (matrix, names) = CorrelationKernels.ComputeCorrelationMatrix(_df);
        int n = names.Length;

        var heatmap = new HeatmapPlot(matrix, n, n, ColorMap.Coolwarm);
        _figure.AddPlottable(heatmap);
        _figure.Title = "Correlation Matrix";
        _figure.ShowLegend = false;
        return this;
    }

    public Chart AddRegressionTrend(bool showConfidenceBand = true)
    {
        if (_xCol == null || _yCol == null)
            throw new InvalidOperationException("AddRegressionTrend requires both X and Y encoding.");

        float[] x = _df.GetColumn(_xCol).ToFloatArray();
        float[] y = _df.GetColumn(_yCol).ToFloatArray();

        var style = new PlotStyle { Color = Colors.Cyan, StrokeWidth = 2.5f };
        var reg = new RegressionPlot(x, y, style)
        {
            ShowConfidenceBand = showConfidenceBand,
            ShowScatter = false,
            Label = "OLS Linear Fit"
        };

        _figure.AddPlottable(reg);
        return this;
    }

    public Figure ToGlacierPlotFigure() => _figure;

    public void RenderToPng(string filePath, int width = 1280, int height = 720)
    {
        _figure.SavePng(filePath, width, height);
    }

    public void RenderToSvg(string filePath, int width = 1280, int height = 720)
    {
        _figure.SaveSvg(filePath, width, height);
    }

    private static Dictionary<string, List<float>> GroupByCategories(ISeries catCol, float[] yVals)
    {
        var dict = new Dictionary<string, List<float>>();
        for (int i = 0; i < catCol.Length; i++)
        {
            string cat = catCol.Get(i)?.ToString() ?? "Unknown";
            if (!dict.TryGetValue(cat, out var list))
            {
                list = new List<float>();
                dict[cat] = list;
            }
            list.Add(yVals[i]);
        }
        return dict;
    }
}
