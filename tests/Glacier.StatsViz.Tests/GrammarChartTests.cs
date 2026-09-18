namespace Glacier.StatsViz.Tests;

using System.IO;
using Glacier.Polaris;
using Glacier.Polaris.Data;
using Glacier.StatsViz.Grammar;
using Xunit;

public class GrammarChartTests
{
    private static DataFrame CreateSampleDataFrame()
    {
        var catSeries = CategoricalSeries.FromStrings(
            "Department",
            ["Engineering", "Engineering", "Engineering", "Marketing", "Marketing", "Marketing"]);

        var salarySeries = new Float32Series("Salary", 6);
        new float[] { 110f, 135f, 120f, 75f, 85f, 90f }.CopyTo(salarySeries.Memory.Span);

        var expSeries = new Float32Series("Experience", 6);
        new float[] { 3f, 7f, 5f, 2f, 4f, 6f }.CopyTo(expSeries.Memory.Span);

        return new DataFrame([catSeries, salarySeries, expSeries]);
    }

    [Fact]
    public void Chart_RendersViolinPlot_ToPngBytes()
    {
        var df = CreateSampleDataFrame();
        var chart = Chart.FromDataFrame(df)
            .Encode(x: "Department", y: "Salary")
            .WithTitle("Salary Distribution by Department")
            .GeomViolin();

        var fig = chart.ToGlacierPlotFigure();
        byte[] png = fig.RenderToBytes(600, 400);

        Assert.NotNull(png);
        Assert.True(png.Length > 200);
        Assert.Equal(0x89, png[0]);
    }

    [Fact]
    public void Chart_RendersBoxPlot()
    {
        var df = CreateSampleDataFrame();
        var chart = Chart.FromDataFrame(df)
            .Encode(x: "Department", y: "Salary")
            .GeomBox();

        byte[] png = chart.ToGlacierPlotFigure().RenderToBytes(500, 350);
        Assert.NotEmpty(png);
    }

    [Fact]
    public void Chart_RendersKdePlot()
    {
        var df = CreateSampleDataFrame();
        var chart = Chart.FromDataFrame(df)
            .Encode(x: "Salary")
            .GeomKde();

        byte[] png = chart.ToGlacierPlotFigure().RenderToBytes(500, 350);
        Assert.NotEmpty(png);
    }

    [Fact]
    public void Chart_RendersHistogram()
    {
        var df = CreateSampleDataFrame();
        var chart = Chart.FromDataFrame(df)
            .Encode(x: "Salary")
            .GeomHistogram(bins: 5, density: true);

        byte[] png = chart.ToGlacierPlotFigure().RenderToBytes(500, 350);
        Assert.NotEmpty(png);
    }

    [Fact]
    public void Chart_RendersScatterWithRegression()
    {
        var df = CreateSampleDataFrame();
        var chart = Chart.FromDataFrame(df)
            .Encode(x: "Experience", y: "Salary")
            .GeomScatter()
            .AddRegressionTrend();

        byte[] png = chart.ToGlacierPlotFigure().RenderToBytes(600, 400);
        Assert.NotEmpty(png);
    }

    [Fact]
    public void Chart_RendersHeatmap_ToSvg()
    {
        var df = CreateSampleDataFrame();
        var chart = Chart.FromDataFrame(df)
            .GeomHeatmap();

        string tempSvg = Path.Combine(Path.GetTempPath(), $"statsviz_test_{Path.GetRandomFileName()}.svg");
        try
        {
            chart.RenderToSvg(tempSvg, 500, 500);
            Assert.True(File.Exists(tempSvg));
            string content = File.ReadAllText(tempSvg);
            Assert.Contains("<svg", content);
        }
        finally
        {
            if (File.Exists(tempSvg)) File.Delete(tempSvg);
        }
    }

    [Fact]
    public void Chart_CategoricalGrouping_ZeroBoxingPartitioning()
    {
        var cat = CategoricalSeries.FromStrings("Group", ["A", "B", "A", "C", "B", "A", "C"]);
        var vals = new Float32Series("Value", 7);
        new float[] { 10f, 20f, 30f, 40f, 50f, 60f, 70f }.CopyTo(vals.Memory.Span);

        var df = new DataFrame([cat, vals]);
        var chart = Chart.FromDataFrame(df)
            .Encode(x: "Group", y: "Value")
            .GeomBox()
            .GeomViolin();

        var fig = chart.ToGlacierPlotFigure();
        Assert.NotNull(fig);
        byte[] png = fig.RenderToBytes(400, 300);
        Assert.NotEmpty(png);
        Assert.Equal(0x89, png[0]);
    }
}
