namespace Glacier.StatsViz.Benchmarks;

using System;
using System.Diagnostics;
using BenchmarkDotNet.Running;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--bdn", StringComparison.OrdinalIgnoreCase))
        {
            BenchmarkRunner.Run<KdeBenchmarks>();
            return;
        }

        Console.WriteLine("================================================================================");
        Console.WriteLine("               GLACIER.STATSVIZ HIGH-SPEED STATS & KDE BENCHMARK                ");
        Console.WriteLine("================================================================================");

        var bench = new KdeBenchmarks();
        bench.Setup();

        // Warmup
        for (int i = 0; i < 3; i++) bench.Kde_100k();

        const int iters = 10;

        // 1. KDE 100k
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iters; i++) bench.Kde_100k();
        sw.Stop();
        double ms100k = sw.Elapsed.TotalMilliseconds / iters;
        Console.WriteLine($"  SIMD Gaussian KDE (100,000 samples, 200 grid):     {ms100k:F3} ms");

        // 2. KDE 1M
        sw.Restart();
        for (int i = 0; i < 5; i++) bench.Kde_1M();
        sw.Stop();
        double ms1M = sw.Elapsed.TotalMilliseconds / 5;
        Console.WriteLine($"  SIMD Gaussian KDE (1,000,000 samples, 200 grid):   {ms1M:F3} ms");

        // 3. Descriptive Stats 1M
        sw.Restart();
        for (int i = 0; i < 5; i++) bench.Stats_1M();
        sw.Stop();
        double msStats = sw.Elapsed.TotalMilliseconds / 5;
        Console.WriteLine($"  Descriptive Stats (1,000,000 samples, Quartiles):  {msStats:F3} ms");

        Console.WriteLine("================================================================================");
    }
}
