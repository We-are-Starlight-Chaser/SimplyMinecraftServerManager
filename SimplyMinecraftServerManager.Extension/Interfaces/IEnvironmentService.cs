// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 安全的系统环境信息服务。
/// 扩展可通过此接口获取系统信息，禁止直接访问 System.Environment / RuntimeInformation。
/// 返回的信息经过脱敏处理，不暴露完整路径等敏感数据。
/// </summary>
public interface IEnvironmentService
{
    /// <summary>操作系统平台名称（如 "Windows", "Linux"）</summary>
    string OSPlatform { get; }

    /// <summary>操作系统版本（如 "10.0.19045"）</summary>
    string OSVersion { get; }

    /// <summary>处理器架构（如 "X64", "Arm64"）</summary>
    string ProcessorArchitecture { get; }

    /// <summary>系统总物理内存（字节）</summary>
    long TotalPhysicalMemoryBytes { get; }

    /// <summary>当前可用物理内存（字节）</summary>
    long AvailablePhysicalMemoryBytes { get; }

    /// <summary>CPU 核心数</summary>
    int ProcessorCount { get; }

    /// <summary>应用运行时版本（如 "10.0.0"）</summary>
    Version RuntimeVersion { get; }

    /// <summary>应用基础目录路径（脱敏后）</summary>
    string ApplicationBasePath { get; }

    /// <summary>获取磁盘可用空间（字节）。path 不暴露给扩展。</summary>
    Task<long> GetDiskFreeSpaceAsync(string path, CancellationToken ct = default);
}
