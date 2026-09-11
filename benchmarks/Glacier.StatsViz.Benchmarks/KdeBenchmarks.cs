namespace Glacier.StatsViz.Benchmarks;

using System;
using BenchmarkDotNet.Attributes;
using Glacier.StatsViz.Kde;
using Glacier.StatsViz.Stats;

[MemoryDiagnoser]
public class KdeBenchmarks
{
    private float[] _samples100k = null!;
    private float[] _samples1M = null!;
    private float[] _grid = null!;
    private float[] _outDensity = null!;

    [GlobalSetup]
    public void Setup()
    {
        _samples100k = new float[100_000];
        _samples1M = new float[1_000_000];
        var rand = new Random(42);

        for (int i = 0; i < 100_000; i++) _samples100k[i] = (float)rand.NextDouble() * 100f;
        for (int i = 0; i < 1_000_000; i++) _samples1M[i] = (float)rand.NextDouble() * 100f;

        const int gridPoints = 200;
        _grid = new float[gridPoints];
        _outDensity = new float[gridPoints];
        for (int i = 0; i < gridPoints; i++) _grid[i] = i * 0.5f;
    }

    [Benchmark(Description = "KDE 100k Samples (200 Grid Points)")]
    public void Kde_100k() => KdeKernels.VectorizedKde(_samples100k, _grid, 1.0f, _outDensity);

    [Benchmark(Description = "KDE 1,000,000 Samples (200 Grid Points)")]
    public void Kde_1M() => KdeKernels.VectorizedKde(_samples1M, _grid, 1.0f, _outDensity);

    [Benchmark(Description = "Descriptive Stats 1M Points (Quartiles & IQR)")]
    public SummaryStats Stats_1M() => DescriptiveStats.Compute(_samples1M);
}
