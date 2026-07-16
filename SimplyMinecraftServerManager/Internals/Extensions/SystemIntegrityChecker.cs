// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.IO;
using System.Security.Cryptography;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 系统完整性校验器。
/// 硬编码已知安全的系统 DLL SHA256 哈希值，供模块监控守护在校验时比对。
///
/// 设计原则：
///   - 仅收录框架没有替代 API 的系统 DLL（文件操作、进程创建等已有 Guard 的不在此列）
///   - 哈希值硬编码自 .NET 10.0.9 (win-x64) 的已知安全版本
///   - ModuleMonitor / AntiTamper 在检测到新模块时调用 IsKnownSystemModule() 进行放行
/// </summary>
internal static class SystemIntegrityChecker
{
    /// <summary>
    /// 已知安全的系统 DLL 及其预期 SHA256 哈希值（小写十六进制）。
    /// 包含 .NET Core Runtime 和 WPF 框架中扩展必须使用且框架无替代 API 的模块。
    /// </summary>
    public static readonly Dictionary<string, string> ExpectedHashes = new(StringComparer.OrdinalIgnoreCase)
    {
        // === .NET Core Runtime ===
        // CRITICAL: 所有扩展必需的基础类型 (Version, DateTime, IDisposable, String, etc.)
        ["System.Private.CoreLib.dll"]    = "dc1945de746f94987ec705a1f27d512abf72a41a1da37e414d1951e4e823037a",
        // CRITICAL: 核心运行时类型系统 (Type, Object, Assembly, Activator, etc.)
        ["System.Runtime.dll"]            = "87f6a9a4133e1fb95b0430071ae73d232eb9cb24beb04aca4d7ae3a3d074f9ad",
        // CRITICAL: CancellationToken 在所有 SDK 方法签名中
        ["System.Threading.dll"]          = "89a81656b39f93950f4ab2882b7ea350892edd7b6074ad67179ecc73ca01a190",
        // CRITICAL: Task/Task<T> 是所有异步方法的返回类型
        ["System.Threading.Tasks.dll"]    = "3d58c971ab9bb4e3b5de43f4706ad9f63b4aea04d1ca7a3762ce93168875a60d",
        // HIGH: SDK 接口暴露的集合类型 (IReadOnlyList, Dictionary, etc.)
        ["System.Collections.dll"]        = "e8a4b43005fff65121433b298659322410a69bc02bcf44b223f1ed282a3a0335",
        // HIGH: LINQ 查询方法
        ["System.Linq.dll"]               = "2142f1e852b8edbf8dceddb575177295dde02c8a368e716f0ca0c902d42bfe03",
        // MEDIUM: Path, MemoryStream, Stream 等类型使用
        ["System.IO.dll"]                 = "f5f0aaa66f552c7c0867c3ca00c3df1b4108a8539817b24a33f002da19e59ac1",
        // MEDIUM: HttpClient 用于 HTTP 请求
        ["System.Net.Http.dll"]           = "9b6b1e1f5e8b6ac535241cd514e1b1c1e15b2fac9df30fc0f1369c1b8a9d1433",

        // === WPF 框架 ===
        // WPF 渲染核心
        ["PresentationCore.dll"]         = "2264156caa886da031791f6459d5bc78cec0c52a117b2333393546c82c3642de",
        // WPF 框架层 (控件、布局、数据绑定)
        ["PresentationFramework.dll"]     = "f13135c4cdf7d1b40469c1c0b6396ea59782f267e8cdd7c37b480c229c9175ff",
        // WPF 基础 (DependencyObject, Dispatcher, etc.)
        ["WindowsBase.dll"]              = "eb051d513031cb6d80b219a1de82e8889284aedcd770f399bf3ea726cff43e14",
        // WPF UI 控件
        ["PresentationUI.dll"]           = "ad0e1a34dc5ee8f8d0cc95cb14e957a25df4e1132491a415770f82de442baadd",
        // UI 自动化
        ["UIAutomationProvider.dll"]      = "691c03d4bd5ad62aca075154454f9112fd83063c351f05ac000dc8a378cf96e0",
        ["UIAutomationTypes.dll"]         = "f067607b8580974165b05e41a7c28fda823ddd8c3211c4d63f894f3b3006470d",
    };

    /// <summary>
    /// 检查指定模块是否为已知安全的系统模块（通过文件哈希比对）。
    /// 如果模块不在哈希表中，则计算实际哈希并缓存比对结果。
    /// </summary>
    /// <param name="moduleName">模块文件名（如 "PresentationUI.dll"）</param>
    /// <param name="modulePath">模块完整路径</param>
    /// <returns>true 表示是已知安全的系统模块，应放行</returns>
    public static bool IsKnownSystemModule(string moduleName, string? modulePath)
    {
        // 不在哈希表中的模块，不做哈希校验（交给后续黑名单/路径检查）
        if (!ExpectedHashes.TryGetValue(moduleName, out string? expectedHash))
            return false;

        // 没有路径无法计算哈希
        if (string.IsNullOrEmpty(modulePath))
            return false;

        try
        {
            if (!File.Exists(modulePath))
                return false;

            byte[] fileBytes = File.ReadAllBytes(modulePath);
            byte[] actualHashBytes = SHA256.HashData(fileBytes);
            string actualHash = Convert.ToHexStringLower(actualHashBytes);

            return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // 文件读取失败，视为不安全
            return false;
        }
    }

    /// <summary>
    /// 执行全量系统完整性校验并输出日志。
    /// 返回 true 表示所有关键系统 DLL 均未被篡改。
    /// </summary>
    public static bool VerifyWithLogging(ILogger logger)
    {
        logger.Info("[系统完整性] 开始校验关键系统 DLL...");
        string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        int passed = 0;
        int failed = 0;

        foreach (var (dllName, expectedHash) in ExpectedHashes)
        {
            // 只校验 .NET Runtime 目录下的 DLL（WPF DLL 在 WindowsDesktop 目录）
            string dllPath = Path.Combine(runtimeDir, dllName);
            if (!File.Exists(dllPath))
            {
                // 尝试 WindowsDesktop 目录
                string? wpfDir = Path.GetDirectoryName(runtimeDir);
                if (!string.IsNullOrEmpty(wpfDir))
                {
                    // 向上找到 shared/ 再进入 WindowsDesktop.App/{version}/
                    string? sharedRoot = Path.GetDirectoryName(wpfDir);
                    if (!string.IsNullOrEmpty(sharedRoot))
                    {
                        string wpfFramework = Path.Combine(sharedRoot, "Microsoft.WindowsDesktop.App");
                        if (Directory.Exists(wpfFramework))
                        {
                            var versions = Directory.GetDirectories(wpfFramework);
                            foreach (string verDir in versions)
                            {
                                string wpfPath = Path.Combine(verDir, dllName);
                                if (File.Exists(wpfPath))
                                {
                                    dllPath = wpfPath;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            if (!File.Exists(dllPath))
                continue;

            try
            {
                byte[] fileBytes = File.ReadAllBytes(dllPath);
                byte[] actualHashBytes = SHA256.HashData(fileBytes);
                string actualHash = Convert.ToHexStringLower(actualHashBytes);

                if (string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    passed++;
                }
                else
                {
                    failed++;
                    logger.Error($"[系统完整性] ⚠ {dllName} 哈希不匹配:");
                    logger.Error($"  预期: {expectedHash}");
                    logger.Error($"  实际: {actualHash}");
                }
            }
            catch
            {
                failed++;
                logger.Error($"[系统完整性] ⚠ {dllName} 读取失败");
            }
        }

        if (failed == 0)
        {
            logger.Info($"[系统完整性] 校验通过 ✓ ({passed}/{ExpectedHashes.Count} 个 DLL 匹配)");
            return true;
        }

        logger.Error($"[系统完整性] 校验失败！通过: {passed}/{ExpectedHashes.Count}, 失败: {failed}");
        logger.Error("[系统完整性] 系统环境可能已被篡改，拒绝加载扩展");
        return false;
    }
}
