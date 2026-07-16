// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Text.RegularExpressions;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// ILogger 实现，将扩展日志路由到主项目的日志系统。
/// 包含日志内容净化功能，防止日志注入攻击。
/// </summary>
internal sealed class ExtensionLogger(string extensionId) : ILogger
{
    // 日志注入检测模式
    private static readonly Regex[] InjectionPatterns = new[]
    {
        // ANSI 转义序列
        new Regex(@"\x1b\[[0-9;]*[a-zA-Z]", RegexOptions.Compiled),
        // 控制字符（除了换行和制表符）
        new Regex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled),
        // Unicode 方向覆盖字符
        new Regex(@"[\u202A-\u202E\u2066-\u2069]", RegexOptions.Compiled),
        // 零宽字符
        new Regex(@"[\u200B-\u200D\u200E-\u200F\uFEFF]", RegexOptions.Compiled),
        // 虚假行分隔符
        new Regex(@"[\u2028\u2029]", RegexOptions.Compiled),
    };
    
    // 危险内容模式
    private static readonly Regex[] DangerousContentPatterns = new[]
    {
        // SQL 注入尝试
        new Regex(@"(?:UNION\s+SELECT|DROP\s+TABLE|INSERT\s+INTO|DELETE\s+FROM)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // 命令注入尝试
        new Regex(@"(?:;\s*(?:rm|del|format|shutdown|reboot|kill))", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // 路径遍历尝试
        new Regex(@"(?:\.\.[\\/]){2,}", RegexOptions.Compiled),
        // Base64 编码的危险内容
        new Regex(@"[A-Za-z0-9+/]{50,}={0,2}", RegexOptions.Compiled),
    };

    public void Debug(string message) =>
        Log($"[EXT:{extensionId}] [DEBUG] {SanitizeLogContent(message)}");

    public void Info(string message) =>
        Log($"[EXT:{extensionId}] [INFO] {SanitizeLogContent(message)}");

    public void Warn(string message) =>
        Log($"[EXT:{extensionId}] [WARN] {SanitizeLogContent(message)}");

    public void Error(string message, Exception? exception = null)
    {
        Log($"[EXT:{extensionId}] [ERROR] {SanitizeLogContent(message)}");
        if (exception is not null)
        {
            Log($"[EXT:{extensionId}] [EXCEPTION] {SanitizeLogContent(exception.ToString())}");
        }
    }

    /// <summary>
    /// 净化日志内容，防止日志注入攻击。
    /// </summary>
    private static string SanitizeLogContent(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;
        
        string sanitized = content;
        
        // 检测并移除 ANSI 转义序列
        foreach (var pattern in InjectionPatterns)
        {
            sanitized = pattern.Replace(sanitized, "[SANITIZED]");
        }
        
        // 检测危险内容
        foreach (var pattern in DangerousContentPatterns)
        {
            if (pattern.IsMatch(sanitized))
            {
                sanitized = "[SUSPICIOUS_CONTENT]";
                break;
            }
        }
        
        // 限制日志长度（防止日志洪水）
        if (sanitized.Length > 10000)
        {
            sanitized = sanitized[..10000] + "... [TRUNCATED]";
        }
        
        // 移除多余的空白字符
        sanitized = Regex.Replace(sanitized, @"\s{100,}", " [EXCESS_WHITESPACE] ");
        
        return sanitized;
    }
    
    /// <summary>
    /// 检查日志内容是否包含可疑内容。
    /// </summary>
    public static bool ContainsSuspiciousContent(string content)
    {
        if (string.IsNullOrEmpty(content))
            return false;
        
        foreach (var pattern in InjectionPatterns)
        {
            if (pattern.IsMatch(content))
                return true;
        }
        
        foreach (var pattern in DangerousContentPatterns)
        {
            if (pattern.IsMatch(content))
                return true;
        }
        
        return false;
    }

    private static void Log(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
    }
}
