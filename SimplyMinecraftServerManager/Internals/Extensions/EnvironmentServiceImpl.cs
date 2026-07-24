// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SimplyMinecraftServerManager.Extension.Interfaces;
using OsPlatform = System.Runtime.InteropServices.OSPlatform;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// IEnvironmentService 实现，提供脱敏的系统环境信息。
/// </summary>
internal sealed class EnvironmentServiceImpl(string applicationBasePath) : IEnvironmentService
{
    private readonly string _applicationBasePath = applicationBasePath;

    public string OSPlatform => System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OsPlatform.Windows) ? "Windows"
        : System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OsPlatform.Linux) ? "Linux"
        : System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OsPlatform.OSX) ? "macOS"
        : System.Runtime.InteropServices.RuntimeInformation.OSDescription;

    public string OSVersion => System.Runtime.InteropServices.RuntimeInformation.OSDescription;

    public string ProcessorArchitecture => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();

    public long TotalPhysicalMemoryBytes
    {
        get
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                return process.WorkingSet64; // 近似值
            }
            catch { return 0; }
        }
    }

    public long AvailablePhysicalMemoryBytes
    {
        get
        {
            try
            {
                var gc = GC.GetTotalMemory(false);
                return gc;
            }
            catch { return 0; }
        }
    }

    public int ProcessorCount => Environment.ProcessorCount;

    public Version RuntimeVersion => Environment.Version;

    public string ApplicationBasePath => _applicationBasePath;

    public Task<long> GetDiskFreeSpaceAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var info = new DriveInfo(Path.GetPathRoot(path) ?? path);
            return Task.FromResult(info.AvailableFreeSpace);
        }
        catch
        {
            return Task.FromResult(0L);
        }
    }
}
