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
///   - 哈希值硬编码自 .NET 10.0.10 (win-x64) 的已知安全版本
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
        // === .NET Core Runtime (.NET 10.0.10 win-x64) ===
        ["System.Private.CoreLib.dll"]    = "76a49f17e83f613bc8a5b55fc98830b764e7838265aacbdbcc8f46707f80a3c9",
        ["System.Runtime.dll"]            = "60d0795848d15c0ee9c37d6b97dfef12eb48292ffda3e9054204b2c3a6a89b19",
        ["System.Threading.dll"]          = "96ef32e315217f24b6b701fc182cac464966cad1da94dd25a3609fe2d9f5a6ca",
        ["System.Threading.Tasks.dll"]    = "2830ac695731137a529b7620482499eae9dc88730aa78b2dbbbe35e9ea6655d9",
        ["System.Collections.dll"]        = "fc52be0abceac360478ffffce876c99997b4af56fc36fbf07cfc38ac5b7fdd7f",
        ["System.Linq.dll"]               = "4a5d41332ed37f7e195503ebd3f478da2ad0008cb8a9f85739b53d2df7d33d46",
        ["System.IO.dll"]                 = "7b40c8b70bf1b61924b4fade11d4325151debc2f5733f32fbe3409621914c802",
        ["System.Net.Http.dll"]           = "277ee31df794738d802c9dca109565ee1f549e733741af03b17f907208d13538",

        // === WPF 框架 (Microsoft.WindowsDesktop.App 10.0.10) ===
        ["WindowsBase.dll"]              = "40919153c0d41db63182208f07ceb2cdd7a5eaa1231bfae318f64fad929d6646",
        ["PresentationCore.dll"]         = "b8fbf245f2868dc992c329e67f1277119dc3a3914048ec73ee88fa45acf7e0ec",
        ["PresentationFramework.dll"]     = "3a22e5bd32d90b034a5ec19657b506cac2647b46cdaef5d33e134b09585de5d2",
        ["PresentationUI.dll"]           = "c4dddb3014180a096a5d4150e5dc70d70842959d295de846519c5f3351d46fcd",
        ["UIAutomationProvider.dll"]      = "55d87d14c32dcc2b768c1bf217019bf9780867ecc78ffeb70fbcdce0f9b7f9b7",
        ["UIAutomationTypes.dll"]         = "d4b9677ed735fe4f8db62ebc5d989cfc433f025dfbf3fd961f9367c140276e5d",
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

            using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var stream = new FileStream(modulePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                incrementalHash.AppendData(buffer, 0, bytesRead);
            }
            byte[] actualHashBytes = incrementalHash.GetHashAndReset();
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
                using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using var stream = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    incrementalHash.AppendData(buffer, 0, bytesRead);
                }
                byte[] actualHashBytes = incrementalHash.GetHashAndReset();
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
