// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Net.Http;

namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// 提供共享的 HttpClient 工厂方法，避免各 Provider 重复创建相同的 HttpClient。
    /// </summary>
    internal static class HttpHelper
    {
        private const string UserAgent = "SimplyMinecraftServerManager/1.0 (https://github.com/We-are-Starlight-Chaser/SimplyMinecraftServerManager)";

        /// <summary>
        /// 创建默认的 HttpClient 实例，超时时间 30 分钟。
        /// </summary>
        public static HttpClient CreateDefaultClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return client;
        }
    }
}
