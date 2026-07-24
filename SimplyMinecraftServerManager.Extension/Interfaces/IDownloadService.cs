// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 文件下载服务，供扩展下载 JAR、插件/模组等文件。
/// 禁止直接使用 System.Net.Http.HttpClient 下载文件，必须通过此接口。
/// </summary>
public interface IDownloadService
{
    /// <summary>下载进度回调</summary>
    /// <param name="bytesReceived">已接收字节数</param>
    /// <param name="totalBytes">总字节数（未知时为 -1）</param>
    /// <param name="progressPercent">进度百分比（0-100，未知时为 -1）</param>
    delegate void ProgressCallback(long bytesReceived, long totalBytes, double progressPercent);

    /// <summary>
    /// 下载文件到指定路径。
    /// </summary>
    /// <param name="url">下载地址</param>
    /// <param name="destinationPath">目标文件完整路径</param>
    /// <param name="progress">进度回调（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>下载是否成功</returns>
    Task<bool> DownloadAsync(
        string url,
        string destinationPath,
        ProgressCallback? progress = null,
        CancellationToken cancellationToken = default);
}
