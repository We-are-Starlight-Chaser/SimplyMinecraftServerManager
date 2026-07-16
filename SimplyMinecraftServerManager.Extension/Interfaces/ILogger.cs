// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 扩展日志抽象，供扩展输出诊断和运行时日志。
/// 宿主负责将日志路由到适当的输出（UI、文件等）。
/// 禁止直接使用 Console.WriteLine / System.Diagnostics.Debug.WriteLine，必须通过此接口。
/// </summary>
public interface ILogger
{
    /// <summary>输出调试级别日志（仅在调试模式下显示）</summary>
    void Debug(string message);

    /// <summary>输出信息级别日志</summary>
    void Info(string message);

    /// <summary>输出警告级别日志</summary>
    void Warn(string message);

    /// <summary>输出错误级别日志</summary>
    void Error(string message, Exception? exception = null);
}
