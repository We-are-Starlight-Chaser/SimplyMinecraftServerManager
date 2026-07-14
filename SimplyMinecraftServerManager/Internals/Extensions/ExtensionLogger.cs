// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Diagnostics;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// ILogger 实现，将扩展日志路由到主项目的日志系统。
/// </summary>
internal sealed class ExtensionLogger(string extensionId) : ILogger
{
    public void Debug(string message) =>
        Log($"[EXT:{extensionId}] [DEBUG] {message}");

    public void Info(string message) =>
        Log($"[EXT:{extensionId}] [INFO] {message}");

    public void Warn(string message) =>
        Log($"[EXT:{extensionId}] [WARN] {message}");

    public void Error(string message, Exception? exception = null)
    {
        Log($"[EXT:{extensionId}] [ERROR] {message}");
        if (exception is not null)
        {
            Log($"[EXT:{extensionId}] [EXCEPTION] {exception}");
        }
    }

    private static void Log(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
    }
}
