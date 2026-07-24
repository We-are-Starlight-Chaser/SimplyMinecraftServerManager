// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 禁止 API 检测器。
/// 通过分析调用栈，检测扩展是否直接使用了项目已有替代方案的 .NET API。
///
/// 设计原则：
///   - 项目已提供 IFileService/IFolderService，禁止直接使用 System.IO.File/Directory
///   - 项目已提供 IServerService，禁止直接使用 System.Diagnostics.Process
///   - 项目已提供 IDownloadService，禁止直接使用 HttpClient 下载
///   - 项目已提供 ILogger，禁止直接使用 Console.WriteLine / Debug.WriteLine
///   - 检测到违规时记录日志，由调用方决定是否拦截
/// </summary>
internal static class ForbiddenApiDetector
{
    private static readonly (string Namespace, string? ClassName, string? MethodName, string Alternative, string Reason)[] ForbiddenApis =
    [
#pragma warning disable CS8619 // 数组中 null 用于表示"不限定方法名"，与元组 string? 语义一致
        // === 文件系统 === 项目已有 IFileService / IFolderService
        ("System.IO", "File", "ReadAllBytesAsync",  "IFileService.ReadBytesAsync()",     "文件读取必须通过 IFileService"),
        ("System.IO", "File", "ReadAllBytes",        "IFileService.ReadBytesAsync()",     "文件读取必须通过 IFileService"),
        ("System.IO", "File", "ReadAllTextAsync",    "IFileService.ReadTextAsync()",      "文件读取必须通过 IFileService"),
        ("System.IO", "File", "ReadAllText",         "IFileService.ReadTextAsync()",      "文件读取必须通过 IFileService"),
        ("System.IO", "File", "WriteAllBytesAsync",  "IFileService.WriteBytesAsync()",    "文件写入必须通过 IFileService"),
        ("System.IO", "File", "WriteAllBytes",       "IFileService.WriteBytesAsync()",    "文件写入必须通过 IFileService"),
        ("System.IO", "File", "WriteAllTextAsync",   "IFileService.WriteTextAsync()",     "文件写入必须通过 IFileService"),
        ("System.IO", "File", "WriteAllText",        "IFileService.WriteTextAsync()",     "文件写入必须通过 IFileService"),
        ("System.IO", "File", "AppendAllTextAsync",  "IFileService.AppendTextAsync()",    "文件写入必须通过 IFileService"),
        ("System.IO", "File", "AppendAllText",       "IFileService.AppendTextAsync()",    "文件写入必须通过 IFileService"),
        ("System.IO", "File", "Delete",              "IFileService.DeleteAsync()",        "文件删除必须通过 IFileService"),
        ("System.IO", "File", "Move",                "IFileService.MoveAsync()",          "文件移动必须通过 IFileService"),
        ("System.IO", "File", "Copy",                "IFileService.CopyAsync()",          "文件复制必须通过 IFileService"),
        ("System.IO", "File", "Exists",              "IFileService.ExistsAsync()",        "文件存在性检查必须通过 IFileService"),
        ("System.IO", "Directory", "GetFiles",            "IFolderService.ListAsync()",   "目录枚举必须通过 IFolderService"),
        ("System.IO", "Directory", "EnumerateFiles",      "IFolderService.ListAsync()",   "目录枚举必须通过 IFolderService"),
        ("System.IO", "Directory", "GetDirectories",      "IFolderService.ListAsync()",   "目录枚举必须通过 IFolderService"),
        ("System.IO", "Directory", "CreateDirectory",     "IFolderService.CreateAsync()", "目录创建必须通过 IFolderService"),
        ("System.IO", "Directory", "Delete",              "IFolderService.DeleteAsync()", "目录删除必须通过 IFolderService"),
        ("System.IO", "Directory", "Move",                "IFolderService.MoveAsync()",   "目录移动必须通过 IFolderService"),
        ("System.IO", "Directory", "Exists",              "IFolderService.ExistsAsync()", "目录存在性检查必须通过 IFolderService"),

        // === 进程管理 === 项目已有 IServerService
        ("System.Diagnostics", "Process", "Start",       "IServerService.StartAsync()",  "服务器进程管理必须通过 IServerService"),
        ("System.Diagnostics", "Process", "Kill",        "IServerService.KillAsync()",   "服务器进程管理必须通过 IServerService"),
        ("System.Diagnostics", "Process", "CloseMainWindow", "IServerService.StopAsync()", "服务器进程管理必须通过 IServerService"),

        // === 日志 === 项目已有 ILogger
        ("System", "Console",    "WriteLine",     "ILogger.Info()",    "日志输出必须通过 ILogger"),
        ("System", "Console",    "Write",         "ILogger.Info()",    "日志输出必须通过 ILogger"),
        ("System.Diagnostics", "Debug",  "WriteLine",     "ILogger.Debug()",   "日志输出必须通过 ILogger"),
        ("System.Diagnostics", "Debug",  "Write",         "ILogger.Debug()",   "日志输出必须通过 ILogger"),
        ("System.Diagnostics", "Trace",  "WriteLine",     "ILogger.Debug()",   "日志输出必须通过 ILogger"),

        // === 反射 === 禁止直接使用危险反射方法
        ("System.Reflection", "Type",          "GetTypeFromHandle",      "项目提供安全的类型访问", "反射访问受限制，请使用项目提供的接口"),
        ("System.Reflection", "Type",          "InvokeMember",           "项目提供安全的成员访问", "反射访问受限制，请使用项目提供的接口"),
        ("System.Reflection", "MethodInfo",    "Invoke",                 "项目提供安全的方法调用", "反射调用受限制，请使用项目提供的接口"),
        ("System.Reflection", "MethodInfo",    "CreateDelegate",         "项目提供安全的委托创建", "反射创建委托受限制"),
        ("System.Reflection", "FieldInfo",     "GetValue",               "项目提供安全的字段访问", "反射字段访问受限制"),
        ("System.Reflection", "FieldInfo",     "SetValue",               "项目提供安全的字段设置", "反射字段设置受限制"),
        ("System.Reflection", "PropertyInfo",  "GetValue",               "项目提供安全的属性访问", "反射属性访问受限制"),
        ("System.Reflection", "PropertyInfo",  "SetValue",               "项目提供安全的属性设置", "反射属性设置受限制"),
        ("System.Reflection.Emit", "DynamicMethod", ".ctor",             null, "禁止创建动态方法"),
        ("System.Reflection.Emit", "ILGenerator",   "Emit",             null, "禁止生成 IL 代码"),
        ("System.Reflection", "Assembly",      "Load",                   null, "禁止程序集加载"),
        ("System.Reflection", "Assembly",      "LoadFrom",               null, "禁止从任意路径加载程序集"),
        ("System.Reflection", "Assembly",      "LoadFile",               null, "禁止从文件加载程序集"),
        ("System.Reflection", "Assembly",      "ReflectionOnlyLoad",     null, "禁止反射加载程序集"),
        ("System.Reflection", "Assembly",      "ReflectionOnlyLoadFrom", null, "禁止反射加载程序集"),
        ("System.Reflection", "Assembly",      "UnsafeLoadFrom",         null, "禁止不安全加载程序集"),
        ("System",            "Activator",     "CreateInstance",         "项目提供安全的实例创建", "直接创建实例受限制"),
        ("System",            "Activator",     "CreateInstanceFrom",     null, "禁止从文件创建实例"),
        ("System",            "AppDomain",     "Load",                   null, "禁止 AppDomain 程序集加载"),
        ("System",            "AppDomain",     "CreateInstance",         null, "禁止 AppDomain 实例创建"),
        ("System",            "AppDomain",     "CreateInstanceAndUnwrap", null, "禁止 AppDomain 实例创建"),

        // === 序列化 === 禁止不安全的序列化器
        ("System.Runtime.Serialization.Formatters.Binary", "BinaryFormatter", "Deserialize", null, "禁止使用不安全的 BinaryFormatter"),
        ("System.Runtime.Serialization.Formatters.Binary", "BinaryFormatter", "Serialize",   null, "禁止使用不安全的 BinaryFormatter"),
        ("System.Runtime.Serialization", "NetDataContractSerializer",        "Deserialize", null, "禁止使用不安全的序列化器"),
        ("System.Runtime.Serialization", "ObjectStateFormatter",             "Deserialize", null, "禁止使用不安全的序列化器"),
        ("System.Web.Script.Serialization", "JavaScriptSerializer",          "Deserialize", null, "禁止使用不安全的序列化器"),
        ("Newtonsoft.Json", "JsonConvert",                                   "DeserializeObject", null, "请使用项目提供的序列化服务"),
        ("System.Text.Json", "JsonSerializer",                              "Deserialize", null, "请使用项目提供的序列化服务"),

        // === 网络上传 === 禁止扩展上传数据（仅允许通过 IDownloadService 下载）
        ("System.Net.Http", "HttpClient",      "PostAsync",        null, "禁止网络上传"),
        ("System.Net.Http", "HttpClient",      "PutAsync",         null, "禁止网络上传"),
        ("System.Net.Http", "HttpClient",      "SendAsync",        null, "禁止网络上传"),
        ("System.Net.Http", "HttpClient",      "PatchAsync",       null, "禁止网络上传"),
        ("System.Net.Http", "HttpClient",      "Post",             null, "禁止网络上传"),
        ("System.Net.Http", "HttpClient",      "Put",              null, "禁止网络上传"),
        ("System.Net.Http", "HttpClient",      "Send",             null, "禁止网络上传"),
        ("System.Net.Http", "HttpClient",      "GetStreamAsync",   null, "禁止网络上传"),
        ("System.Net.Http", "HttpClient",      "GetByteArrayAsync", null, "禁止网络上传"),
        ("System.Net.Http", "HttpClient",      "GetFromJsonAsync", null, "禁止网络上传"),
        ("System.Net", "WebClient",            "UploadString",     null, "禁止网络上传"),
        ("System.Net", "WebClient",            "UploadBytes",      null, "禁止网络上传"),
        ("System.Net", "WebClient",            "UploadFile",       null, "禁止网络上传"),
        ("System.Net", "WebClient",            "UploadValues",     null, "禁止网络上传"),
        ("System.Net", "WebClient",            "UploadData",       null, "禁止网络上传"),
        ("System.Net", "WebClient",            "UploadFileAsync",  null, "禁止网络上传"),
        ("System.Net", "WebClient",            "UploadDataAsync",  null, "禁止网络上传"),
        ("System.Net", "WebClient",            "UploadStringAsync", null, "禁止网络上传"),
        ("System.Net", "WebClient",            "UploadValuesAsync", null, "禁止网络上传"),
        ("System.Net.Sockets", "Socket",       "Send",             null, "禁止网络上传"),
        ("System.Net.Sockets", "Socket",       "SendAsync",        null, "禁止网络上传"),
        ("System.Net.Sockets", "Socket",       "SendTo",           null, "禁止网络上传"),
        ("System.Net.Sockets", "UdpClient",    "Send",             null, "禁止网络上传"),
        ("System.Net.Sockets", "UdpClient",    "SendAsync",        null, "禁止网络上传"),
        ("System.Net.Sockets", "UdpClient",    "SendBytes",        null, "禁止网络上传"),
        ("System.Net.Sockets", "UdpClient",    "SendPacketAsync",  null, "禁止网络上传"),
        ("System.Net.Sockets", "TcpClient",    "GetStream",        null, "禁止网络上传"),
        ("System.Net.Mail", "SmtpClient",      "Send",             null, "禁止网络上传"),
        ("System.Net.Mail", "SmtpClient",      "SendAsync",        null, "禁止网络上传"),
        ("System.Net.Mail", "SmtpClient",      "SendMailAsync",    null, "禁止网络上传"),
    ];
#pragma warning restore CS8619

    /// <summary>
    /// 按命名空间前缀索引的规则字典，O(1) 查找替代 O(N) 线性扫描。
    /// </summary>
    private static readonly Dictionary<string, List<int>> _namespaceIndex;

    /// <summary>
    /// 缓存已查询过的程序集名称，避免每帧重复反射。
    /// </summary>
    private static readonly ConcurrentDictionary<Assembly, string?> _assemblyNameCache = new();

    static ForbiddenApiDetector()
    {
        _namespaceIndex = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int i = 0; i < ForbiddenApis.Length; i++)
        {
            string ns = ForbiddenApis[i].Namespace;
            if (!_namespaceIndex.TryGetValue(ns, out var list))
            {
                list = [];
                _namespaceIndex[ns] = list;
            }
            list.Add(i);
        }
    }

    /// <summary>
    /// 检测结果。
    /// </summary>
    public sealed class DetectionResult
    {
        /// <summary>是否检测到违规</summary>
        public bool HasViolation { get; set; }

        /// <summary>违规详情列表</summary>
        public List<ViolationInfo> Violations { get; init; } = [];
    }

    /// <summary>
    /// 单条违规信息。
    /// </summary>
    public sealed class ViolationInfo
    {
        public string ForbiddenApi { get; init; } = "";
        public string Alternative { get; init; } = "";
        public string Reason { get; init; } = "";
        public string CallerAssembly { get; init; } = "";
        public string StackTrace { get; init; } = "";
    }

    /// <summary>
    /// 通过分析当前调用栈，检测是否有扩展直接使用了禁止的 API。
    /// 只检查来自扩展程序集的调用。
    /// </summary>
    /// <param name="extensionAssemblyName">要检查的扩展程序集名称（前缀匹配）</param>
    public static DetectionResult DetectFromCallStack(string extensionAssemblyName)
    {
        var result = new DetectionResult();
        var stackTrace = new StackTrace(skipFrames: 1);
        var frames = stackTrace.GetFrames();

        if (frames is null || frames.Length == 0)
            return result;

        // 预检查：扩展程序集中是否有帧
        bool hasExtensionFrame = false;
        foreach (var frame in frames)
        {
            var m = frame.GetMethod();
            var asm = m?.DeclaringType?.Assembly;
            if (asm is not null && GetCachedAssemblyName(asm)?.StartsWith(extensionAssemblyName, StringComparison.OrdinalIgnoreCase) == true)
            {
                hasExtensionFrame = true;
                break;
            }
        }
        if (!hasExtensionFrame) return result;

        foreach (var frame in frames)
        {
            var method = frame.GetMethod();
            if (method is null) continue;

            var declaringType = method.DeclaringType;
            if (declaringType is null) continue;

            string ns = declaringType.Namespace ?? "";
            string className = declaringType.Name;
            string methodName = method.Name;

            // 按命名空间前缀索引查找，O(1) 替代 O(N) 遍历
            if (!_namespaceIndex.TryGetValue(ns, out var candidates))
            {
                // 尝试父命名空间匹配（如 System.IO.Compression.File → System.IO）
                int lastDot = ns.LastIndexOf('.');
                if (lastDot > 0)
                {
                    string parentNs = ns[..lastDot];
                    if (!_namespaceIndex.TryGetValue(parentNs, out candidates))
                        continue;
                }
                else
                {
                    continue;
                }
            }

            foreach (int idx in candidates)
            {
                var (forbiddenNs, forbiddenClass, forbiddenMethod, alternative, reason) = ForbiddenApis[idx];

                if (!ns.StartsWith(forbiddenNs, StringComparison.Ordinal)) continue;
                if (forbiddenClass is not null && !className.Contains(forbiddenClass, StringComparison.Ordinal)) continue;
                if (forbiddenMethod is not null && !methodName.Contains(forbiddenMethod, StringComparison.Ordinal)) continue;

                result.HasViolation = true;
                result.Violations.Add(new ViolationInfo
                {
                    ForbiddenApi = $"{declaringType.FullName}.{methodName}()",
                    Alternative = alternative,
                    Reason = reason,
                    CallerAssembly = GetCachedAssemblyName(declaringType.Assembly) ?? extensionAssemblyName,
                    StackTrace = stackTrace.ToString(),
                });
                return result;
            }
        }

        return result;
    }

    /// <summary>
    /// 检查指定调用栈中是否存在禁止的 API 调用。
    /// 用于在 Guard 触发时进一步分析。
    /// </summary>
    public static DetectionResult AnalyzeStackTrace(StackFrame[] frames, string extensionAssemblyName)
    {
        var result = new DetectionResult();

        foreach (var frame in frames)
        {
            var method = frame.GetMethod();
            if (method is null) continue;

            var declaringType = method.DeclaringType;
            if (declaringType is null) continue;

            string ns = declaringType.Namespace ?? "";
            string className = declaringType.Name;
            string methodName = method.Name;

            if (!_namespaceIndex.TryGetValue(ns, out var candidates))
            {
                int lastDot = ns.LastIndexOf('.');
                if (lastDot > 0)
                {
                    string parentNs = ns[..lastDot];
                    if (!_namespaceIndex.TryGetValue(parentNs, out candidates))
                        continue;
                }
                else
                {
                    continue;
                }
            }

            foreach (int idx in candidates)
            {
                var (forbiddenNs, forbiddenClass, forbiddenMethod, alternative, reason) = ForbiddenApis[idx];

                if (!ns.StartsWith(forbiddenNs, StringComparison.Ordinal)) continue;
                if (forbiddenClass is not null && !className.Contains(forbiddenClass, StringComparison.Ordinal)) continue;
                if (forbiddenMethod is not null && !methodName.Contains(forbiddenMethod, StringComparison.Ordinal)) continue;

                result.HasViolation = true;
                result.Violations.Add(new ViolationInfo
                {
                    ForbiddenApi = $"{declaringType.FullName}.{methodName}()",
                    Alternative = alternative,
                    Reason = reason,
                    CallerAssembly = extensionAssemblyName,
                });
                return result;
            }
        }

        return result;
    }

    /// <summary>
    /// 获取所有禁止的 API 列表（用于文档生成或运行时查询）。
    /// </summary>
    public static IReadOnlyList<(string Api, string Alternative, string Reason)> GetAllForbiddenApis()
    {
        return ForbiddenApis.Select(x => ($"{x.Namespace}.{x.ClassName}.{x.MethodName}", x.Alternative, x.Reason)).ToList();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string? GetCachedAssemblyName(Assembly assembly)
    {
        return _assemblyNameCache.GetOrAdd(assembly, a =>
        {
            try { return a.GetName().Name; }
            catch { return null; }
        });
    }
}
