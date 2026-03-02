namespace SimplyMinecraftServerManager.Internals
{
    public class AppConfig
    {
        public string DefaultJdkPath { get; set; } = "";
        public string Language { get; set; } = "zh-CN";
        public bool AutoAcceptEula { get; set; } = false;
        public int DefaultMinMemoryMb { get; set; } = 1024;
        public int DefaultMaxMemoryMb { get; set; } = 2048;

        /// <summary>下载并发线程数 (1~32)</summary>
        public int DownloadThreads { get; set; } = 4;

        /// <summary>JDK 发行版偏好: Adoptium / Zulu</summary>
        public string PreferredJdkDistribution { get; set; } = "Adoptium";
    }
}