namespace Glacier.StatsViz.Demo;

using System;
using System.Diagnostics;
using System.IO;
using Glacier.Plot.Core;
using Glacier.Polaris;
using Glacier.Polaris.Data;
using Glacier.StatsViz.Grammar;
using Glacier.StatsViz.Kde;

public static class Program
{
    public static void Main()
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("       GLACIER.STATSVIZ: STATISTICAL GRAPHICS & SIMD KDE ENGINE (.NET 10)       ");
        Console.WriteLine("                    Native C# Alternative to Python Seaborn                    ");
        Console.WriteLine("================================================================================\n");

        string outDir = Path.Combine(AppContext.BaseDirectory, "output");
        Directory.CreateDirectory(outDir);

        // -----------------------------------------------------------------------------------------
        // Demo 1: Multi-Category Violin Plot with Embedded Inner Quartiles
        // -----------------------------------------------------------------------------------------
        Console.WriteLine("[Demo 1] Multi-Category Statistical Violin Plot (with embedded IQR box & medians)");
        int samplesPerCat = 5000;
        int total = samplesPerCat * 3;

        var cats = new string[total];
        var scores = new float[total];
        var rand = new Random(42);

        for (int i = 0; i < samplesPerCat; i++)
        {
            cats[i] = "Research & Dev";
            scores[i] = 75f + NextGaussian(rand) * 8f;
        }
        for (int i = 0; i < samplesPerCat; i++)
        {
            int idx = samplesPerCat + i;
            cats[idx] = "Product Design";
            scores[idx] = 68f + NextGaussian(rand) * 14f; // higher variance
        }
        for (int i = 0; i < samplesPerCat; i++)
        {
            int idx = samplesPerCat * 2 + i;
            cats[idx] = "Sales & Operations";
            // Bimodal distribution!
            float baseScore = rand.NextDouble() > 0.5 ? 55f : 85f;
            scores[idx] = baseScore + NextGaussian(rand) * 6f;
        }

        var catSeries = CategoricalSeries.FromStrings("Department", cats);
        var scoreSeries = new Float32Series("PerformanceScore", total);
        scores.CopyTo(scoreSeries.Memory.Span);

        var dfViolin = new DataFrame([catSeries, scoreSeries]);

        var violinChart = Chart.FromDataFrame(dfViolin)
            .Encode(x: "Department", y: "PerformanceScore")
            .WithTitle("Employee Performance Distribution by Department")
            .WithTheme(PlotTheme.Dark)
            .GeomViolin();

        string path1 = Path.Combine(outDir, "demo_violin_plot.png");
        violinChart.RenderToPng(path1, 1280, 720);
        Console.WriteLine($"  ✓ Rendered multi-category violin plot -> {path1}\n");

        // -----------------------------------------------------------------------------------------
        // Demo 2: 1,000,000-Point SIMD Gaussian Kernel Density Estimation (KDE)
        // -----------------------------------------------------------------------------------------
        Console.WriteLine("[Demo 2] SIMD Gaussian KDE: 1,000,000 Samples");
        int kdeN = 1_000_000;
        float[] massiveSamples = new float[kdeN];
        for (int i = 0; i < kdeN; i++) massiveSamples[i] = NextGaussian(rand) * 5f + 10f;

        int gridPoints = 200;
        float[] grid = new float[gridPoints];
        for (int i = 0; i < gridPoints; i++) grid[i] = -10f + i * (40f / (gridPoints - 1));
        float[] density = new float[gridPoints];

        // Warmup
        KdeKernels.VectorizedKde(massiveSamples.AsSpan(0, 1000), grid, 1.0f, density);

        var sw = Stopwatch.StartNew();
        KdeKernels.VectorizedKde(massiveSamples, grid, bandwidth: 0.8f, density);
        sw.Stop();

        double kdeMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"  ✓ Vectorized Gaussian KDE: {kdeN:N0} points evaluated in {kdeMs:F2} ms (Target: <25 ms)!");

        var kdeSeries = new Float32Series("Value", 1000);
        massiveSamples.AsSpan(0, 1000).CopyTo(kdeSeries.Memory.Span);
        var dfKde = new DataFrame([kdeSeries]);

        var kdeChart = Chart.FromDataFrame(dfKde)
            .Encode(x: "Value")
            .WithTitle($"SIMD Gaussian KDE (1,000,000 Points in {kdeMs:F1}ms)")
            .WithTheme(PlotTheme.Cyber)
            .GeomKde(bandwidth: 0.8f);

        string path2 = Path.Combine(outDir, "demo_kde_plot.png");
        kdeChart.RenderToPng(path2, 1280, 720);
        Console.WriteLine($"  ✓ Saved high-resolution KDE curve -> {path2}\n");

        // -----------------------------------------------------------------------------------------
        // Demo 3: Correlation Matrix Heatmap
        // -----------------------------------------------------------------------------------------
        Console.WriteLine("[Demo 3] Glacier.Polaris Multi-Feature Correlation Heatmap");
        int rows = 1000;
        var colRev = new Float32Series("Revenue", rows);
        var colCost = new Float32Series("Cost", rows);
        var colProfit = new Float32Series("Profit", rows);
        var colGrowth = new Float32Series("Growth", rows);

        for (int i = 0; i < rows; i++)
        {
            float rev = 100f + (float)rand.NextDouble() * 50f;
            float cost = rev * 0.6f + (float)(rand.NextDouble() - 0.5) * 10f;
            float profit = rev - cost;
            float growth = profit * 0.15f + (float)(rand.NextDouble() - 0.5) * 5f;

            colRev.Memory.Span[i] = rev;
            colCost.Memory.Span[i] = cost;
            colProfit.Memory.Span[i] = profit;
            colGrowth.Memory.Span[i] = growth;
        }

        var dfCorr = new DataFrame([colRev, colCost, colProfit, colGrowth]);
        var heatChart = Chart.FromDataFrame(dfCorr)
            .GeomHeatmap()
            .WithTheme(PlotTheme.Dark);

        string path3 = Path.Combine(outDir, "demo_correlation_matrix.png");
        heatChart.RenderToPng(path3, 700, 700);
        Console.WriteLine($"  ✓ Rendered correlation matrix heatmap -> {path3}\n");

        // -----------------------------------------------------------------------------------------
        // Demo 4: Linear Regression Trend with 95% Confidence Band
        // -----------------------------------------------------------------------------------------
        Console.WriteLine("[Demo 4] Linear Regression Trendline with 95% Confidence Envelope");
        int regN = 250;
        var expX = new Float32Series("ExperienceYears", regN);
        var salY = new Float32Series("SalaryK", regN);

        for (int i = 0; i < regN; i++)
        {
            float exp = (float)rand.NextDouble() * 15f;
            float sal = 45f + exp * 6.5f + NextGaussian(rand) * 8f;
            expX.Memory.Span[i] = exp;
            salY.Memory.Span[i] = sal;
        }

        var dfReg = new DataFrame([expX, salY]);
        var regChart = Chart.FromDataFrame(dfReg)
            .Encode(x: "ExperienceYears", y: "SalaryK")
            .WithTitle("Salary vs Experience (OLS Linear Trend & 95% CI)")
            .WithTheme(PlotTheme.Cyber)
            .GeomScatter()
            .AddRegressionTrend();

        string path4 = Path.Combine(outDir, "demo_regression_plot.png");
        regChart.RenderToPng(path4, 1280, 720);
        Console.WriteLine($"  ✓ Rendered regression plot with confidence band -> {path4}\n");

        Console.WriteLine("================================================================================");
        Console.WriteLine("         ALL DEMOS COMPLETED SUCCESSFULLY: GLACIER.STATSVIZ IS READY!           ");
        Console.WriteLine("================================================================================");
    }

    private static float NextGaussian(Random rand)
    {
        double u1 = 1.0 - rand.NextDouble();
        double u2 = 1.0 - rand.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }
}
