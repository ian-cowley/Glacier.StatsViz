using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Glacier.Gpu.Drivers;
using Glacier.StatsViz.Core;
using Glacier.StatsViz.Kde;

namespace Glacier.StatsViz.Compute;

/// <summary>
/// Bare-metal GPU hardware accelerator for Glacier.StatsViz statistical kernels.
/// Accelerates Kernel Density Estimation (KDE) and high-dimensional distribution computations
/// via NVIDIA RTX 4060 dGPU, AMD Radeon 890M APU, or multi-threaded AVX-512 CPU.
/// </summary>
public static unsafe class GpuStatsAccelerator
{
    private static readonly Lock s_initLock = new();
    private static bool s_nvidiaInitialized;
    private static bool s_nvidiaAvailable;
    private static IntPtr s_cuContext;
    private static IntPtr s_cuModule;

    private static IntPtr s_fnKde1D;
    private static IntPtr s_fnKde2D;

    // Persistent pooled device buffers
    private static IntPtr s_dSamples;
    private static IntPtr s_dGrid;
    private static IntPtr s_dOut;
    private static nuint s_capSamples;
    private static nuint s_capGrid;
    private static nuint s_capOut;

    private static bool s_amdInitialized;
    private static bool s_amdAvailable;

    public static bool IsNvidiaAvailable => EnsureNvidiaInitialized();
    public static bool IsAmdAvailable => EnsureAmdInitialized();
    public static bool IsGpuAvailable => IsNvidiaAvailable || IsAmdAvailable;

    #region Driver Initialization

    private static bool EnsureNvidiaInitialized()
    {
        if (s_nvidiaInitialized) return s_nvidiaAvailable;
        lock (s_initLock)
        {
            if (s_nvidiaInitialized) return s_nvidiaAvailable;
            try
            {
                if (!CuDriver.IsAvailable())
                {
                    s_nvidiaAvailable = false;
                    s_nvidiaInitialized = true;
                    return false;
                }

                if (CuDriver.Init(0) != 0 || CuDriver.DeviceGet(out int dev, 0) != 0)
                {
                    s_nvidiaAvailable = false;
                    s_nvidiaInitialized = true;
                    return false;
                }

                CuDriver.DeviceGetAttribute(out int major, 75, dev);
                CuDriver.DeviceGetAttribute(out int minor, 76, dev);
                string targetArch = $"sm_{major}{minor}";

                if (CuDriver.CtxCreate(out s_cuContext, 0, dev) != 0)
                {
                    s_nvidiaAvailable = false;
                    s_nvidiaInitialized = true;
                    return false;
                }

                string ptx = GpuStatsKernels.PtxSource;
                if (!ptx.Contains($".target {targetArch}"))
                {
                    ptx = System.Text.RegularExpressions.Regex.Replace(ptx, @"\.target\s+sm_\d+", $".target {targetArch}");
                }

                byte[] ptxBytes = Encoding.UTF8.GetBytes(ptx + "\0");
                if (CuDriver.ModuleLoadData(out s_cuModule, ptxBytes) != 0)
                {
                    s_nvidiaAvailable = false;
                    s_nvidiaInitialized = true;
                    return false;
                }

                CuDriver.ModuleGetFunction(out s_fnKde1D, s_cuModule, "statsviz_kde_evaluate_fp32");
                CuDriver.ModuleGetFunction(out s_fnKde2D, s_cuModule, "statsviz_kde2d_evaluate_fp32");

                s_nvidiaAvailable = s_fnKde1D != IntPtr.Zero;
            }
            catch
            {
                s_nvidiaAvailable = false;
            }
            finally
            {
                s_nvidiaInitialized = true;
            }

            return s_nvidiaAvailable;
        }
    }

    private static bool EnsureAmdInitialized()
    {
        if (s_amdInitialized) return s_amdAvailable;
        lock (s_initLock)
        {
            if (s_amdInitialized) return s_amdAvailable;
            try
            {
                if (!HipDriver.IsAvailable() || HipDriver.Init(0) != 0 || HipDriver.GetDeviceCount(out int count) != 0 || count == 0)
                {
                    s_amdAvailable = false;
                    s_amdInitialized = true;
                    return false;
                }

                HipDriver.SetDevice(0);
                s_amdAvailable = true;
            }
            catch
            {
                s_amdAvailable = false;
            }
            finally
            {
                s_amdInitialized = true;
            }

            return s_amdAvailable;
        }
    }

    #endregion

    #region Kernel Density Estimation (1D)

    public static void EvaluateKde(
        ReadOnlySpan<float> samples,
        ReadOnlySpan<float> grid,
        float bandwidth,
        Span<float> outDensity,
        GpuTarget target = GpuTarget.Auto)
    {
        if (bandwidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(bandwidth), "Bandwidth must be positive.");

        int numSamples = samples.Length;
        int numGrid = grid.Length;
        if (numSamples == 0 || numGrid == 0) return;
        if (outDensity.Length < numGrid)
            throw new ArgumentException("Output buffer too small.");

        float invH2 = 1.0f / (2.0f * bandwidth * bandwidth);
        float normFactor = 1.0f / (numSamples * bandwidth * MathF.Sqrt(2.0f * MathF.PI));

        long workItems = (long)numSamples * numGrid;
        bool useGpu = target switch
        {
            GpuTarget.Cpu => false,
            GpuTarget.Nvidia => IsNvidiaAvailable,
            GpuTarget.Amd => IsAmdAvailable,
            _ => (IsNvidiaAvailable || IsAmdAvailable) && workItems >= 32768
        };

        if (useGpu && IsNvidiaAvailable && s_fnKde1D != IntPtr.Zero)
        {
            if (TryExecuteGpuKde1D(samples, grid, outDensity, numSamples, numGrid, invH2, normFactor))
                return;
        }

        // SIMD AVX-512 CPU Fallback
        KdeKernels.VectorizedKde(samples, grid, bandwidth, outDensity);
    }

    #endregion

    #region Buffer Pooling

    private static void EnsurePoolBuffers(nuint capSamples, nuint capGrid, nuint capOut)
    {
        if (capSamples > s_capSamples)
        {
            if (s_dSamples != IntPtr.Zero) CuDriver.MemFree(s_dSamples);
            CuDriver.MemAlloc(out s_dSamples, capSamples);
            s_capSamples = capSamples;
        }
        if (capGrid > s_capGrid)
        {
            if (s_dGrid != IntPtr.Zero) CuDriver.MemFree(s_dGrid);
            CuDriver.MemAlloc(out s_dGrid, capGrid);
            s_capGrid = capGrid;
        }
        if (capOut > s_capOut)
        {
            if (s_dOut != IntPtr.Zero) CuDriver.MemFree(s_dOut);
            CuDriver.MemAlloc(out s_dOut, capOut);
            s_capOut = capOut;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryExecuteGpuKde1D(
        ReadOnlySpan<float> samples,
        ReadOnlySpan<float> grid,
        Span<float> outDensity,
        int numSamples,
        int numGrid,
        float invH2,
        float normFactor)
    {
        try
        {
            nuint bytesSamples = (nuint)(numSamples * sizeof(float));
            nuint bytesGrid = (nuint)(numGrid * sizeof(float));
            nuint bytesOut = (nuint)(numGrid * sizeof(float));

            CuDriver.CtxSetCurrent(s_cuContext);
            lock (s_initLock)
            {
                EnsurePoolBuffers(bytesSamples, bytesGrid, bytesOut);

                fixed (float* pSamples = samples, pGrid = grid, pOut = outDensity)
                {
                    CuDriver.MemcpyHtoD(s_dSamples, (IntPtr)pSamples, bytesSamples);
                    CuDriver.MemcpyHtoD(s_dGrid, (IntPtr)pGrid, bytesGrid);

                    IntPtr[] kernelParams = new IntPtr[7];
                    GCHandle h0 = GCHandle.Alloc(s_dSamples, GCHandleType.Pinned);
                    GCHandle h1 = GCHandle.Alloc(s_dGrid, GCHandleType.Pinned);
                    GCHandle h2 = GCHandle.Alloc(s_dOut, GCHandleType.Pinned);
                    GCHandle h3 = GCHandle.Alloc(numSamples, GCHandleType.Pinned);
                    GCHandle h4 = GCHandle.Alloc(numGrid, GCHandleType.Pinned);
                    GCHandle h5 = GCHandle.Alloc(invH2, GCHandleType.Pinned);
                    GCHandle h6 = GCHandle.Alloc(normFactor, GCHandleType.Pinned);

                    kernelParams[0] = h0.AddrOfPinnedObject();
                    kernelParams[1] = h1.AddrOfPinnedObject();
                    kernelParams[2] = h2.AddrOfPinnedObject();
                    kernelParams[3] = h3.AddrOfPinnedObject();
                    kernelParams[4] = h4.AddrOfPinnedObject();
                    kernelParams[5] = h5.AddrOfPinnedObject();
                    kernelParams[6] = h6.AddrOfPinnedObject();

                    GCHandle hArray = GCHandle.Alloc(kernelParams, GCHandleType.Pinned);
                    try
                    {
                        uint blockSize = 256;
                        uint gridSize = (uint)((numGrid + blockSize - 1) / blockSize);

                        int launchRes = CuDriver.LaunchKernel(
                            s_fnKde1D,
                            gridSize, 1, 1,
                            blockSize, 1, 1,
                            0, IntPtr.Zero,
                            hArray.AddrOfPinnedObject(),
                            IntPtr.Zero);

                        if (launchRes == 0)
                        {
                            CuDriver.CtxSynchronize();
                            CuDriver.MemcpyDtoH((IntPtr)pOut, s_dOut, bytesOut);
                            return true;
                        }
                    }
                    finally
                    {
                        hArray.Free();
                        h0.Free(); h1.Free(); h2.Free();
                        h3.Free(); h4.Free(); h5.Free(); h6.Free();
                    }
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    #endregion
}
