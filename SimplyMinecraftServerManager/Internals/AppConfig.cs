// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 应用程序全局配置，以不可变记录类的形式存储，支持值相等比较
    /// </summary>
    public record class AppConfig
    {
        /// <summary>默认 JDK 安装路径，为空时自动下载</summary>
        public string DefaultJdkPath { get; set; } = "";

        /// <summary>界面语言标识，例如 "zh-CN"、"en-US"</summary>
        public string Language { get; set; } = "zh-CN";

        /// <summary>是否自动同意 Minecraft EULA 协议</summary>
        public bool AutoAcceptEula { get; set; } = true;

        /// <summary>控制台输出是否自动换行</summary>
        public bool ConsoleWrapLines { get; set; } = false;

        /// <summary>控制台使用的字体族名称</summary>
        public string ConsoleFontFamily { get; set; } = "Consolas";

        /// <summary>控制台字体大小（磅值）</summary>
        public int ConsoleFontSize { get; set; } = 12;

        /// <summary>服务器默认最小内存（MB）</summary>
        public int DefaultMinMemoryMb { get; set; } = 1024;

        /// <summary>服务器默认最大内存（MB）</summary>
        public int DefaultMaxMemoryMb { get; set; } = 2048;

        /// <summary>下载并发线程数 (1~32)</summary>
        public int DownloadThreads { get; set; } = 4;

        /// <summary>JDK 发行版偏好: Adoptium / Zulu</summary>
        public string PreferredJdkDistribution { get; set; } = "Adoptium";
    }
}
