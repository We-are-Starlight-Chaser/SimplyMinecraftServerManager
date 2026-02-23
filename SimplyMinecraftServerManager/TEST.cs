using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads;

using System;
using System.Collections.Generic;
using System.Text;

namespace SimplyMinecraftServerManager
{
    public class TEST
    {
        static public async Task Main(string[] args)
        {
            DownloadManager.Default.ProgressChanged += (_, e) =>
            {
                double speedMb = e.SpeedBytesPerSecond / 1048576.0;
                double downloadedMb = e.BytesDownloaded / 1048576.0;

                if (e.IsCompleted)
                {
                    Console.WriteLine($"[{e.DisplayName}] ✅ 下载完成 ({downloadedMb:F1} MB)");
                }
                else if (e.IsFailed)
                {
                    Console.WriteLine($"[{e.DisplayName}] ❌ 下载失败: {e.ErrorMessage}");
                }
                else if (e.TotalBytes > 0)
                {
                    // 总大小已知 → 显示百分比
                    double totalMb = e.TotalBytes / 1048576.0;
                    Console.WriteLine(
                        $"[{e.DisplayName}] {downloadedMb:F1}/{totalMb:F1} MB " +
                        $"({e.ProgressPercent:F1}%) - {speedMb:F2} MB/s");
                }
                else
                {
                    // 总大小未知 → 只显示已下载量和速度（之前这个分支缺失导致无输出）
                    Console.WriteLine(
                        $"[{e.DisplayName}] {downloadedMb:F1} MB - {speedMb:F2} MB/s");
                }
            };

            DownloadManager.Default.TaskCompleted += (_, t) =>
                Console.WriteLine($"✅ {t.DisplayName} 下载完成!");

            DownloadManager.Default.TaskFailed += (_, t) =>
                Console.WriteLine($"❌ {t.DisplayName} 下载失败: {t.ErrorMessage}");

            // 下载 Paper 服务端

            var paper = ServerProviderFactory.Get(ServerPlatform.Paper);

            // 获取所有版本
            var versions = await paper.GetVersionsAsync();
            Console.WriteLine($"Paper 支持的版本: {string.Join(", ", versions)}...");

            string VersionWantToDownload = Console.ReadLine().Replace("\n", "");

            // 获取构建
            var latestBuild = await paper.GetLatestBuildAsync(VersionWantToDownload);

            string destPath = @"C:\Users\ttpa\Desktop\test\server.jar";

            var downloadResult = await paper.DownloadAsync(latestBuild, destPath);
            Console.WriteLine($"Paper 下载状态: {downloadResult.Status}");
        }
    }
}
