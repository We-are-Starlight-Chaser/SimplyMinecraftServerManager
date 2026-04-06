namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 单个服务器实例的元信息 POCO，持久化到 instances.toml。
    /// </summary>
    public class InstanceInfo
    {
        /// <summary>实例唯一标识 (UUID)</summary>
        public string Id { get; set; } = "";

        /// <summary>显示名称</summary>
        public string Name { get; set; } = "";

        /// <summary>此实例使用的 java.exe 路径，留空则使用全局默认</summary>
        public string JdkPath { get; set; } = "";

        /// <summary>服务端 JAR 文件名（相对实例目录）</summary>
        public string ServerJar { get; set; } = "server.jar";

        /// <summary>最小内存 MB</summary>
        public int MinMemoryMb { get; set; } = 1024;

        /// <summary>最大内存 MB</summary>
        public int MaxMemoryMb { get; set; } = 2048;

        /// <summary>额外 JVM 参数</summary>
        public string ExtraJvmArgs { get; set; } = "";

        /// <summary>创建时间 ISO-8601</summary>
        public string CreatedAt { get; set; } = "";
    }

    /// <summary>
    /// instances.toml 的根模型。
    /// 序列化后形如 [[instances]] 数组表。
    /// </summary>
    internal class InstancesFileModel
    {
        public List<InstanceInfo> Instances { get; set; } = [];
    }
}
