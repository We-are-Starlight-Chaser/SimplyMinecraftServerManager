namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 从插件 JAR 内的 plugin.yml 提取的元信息。
    /// </summary>
    public record class PluginInfo
    {
        /// <summary>插件名称 (plugin.yml → name)</summary>
        public string Name { get; set; } = "";

        /// <summary>版本号 (plugin.yml → version)</summary>
        public string Version { get; set; } = "";

        /// <summary>描述 (plugin.yml → description)</summary>
        public string Description { get; set; } = "";

        /// <summary>主类 (plugin.yml → main)</summary>
        public string MainClass { get; set; } = "";

        /// <summary>作者列表</summary>
        public List<string> Authors { get; set; } = [];

        /// <summary>API 版本 (plugin.yml → api-version)</summary>
        public string ApiVersion { get; set; } = "";

        /// <summary>依赖列表 (plugin.yml → depend)</summary>
        public List<string> Dependencies { get; set; } = [];

        /// <summary>软依赖列表 (plugin.yml → softdepend)</summary>
        public List<string> SoftDependencies { get; set; } = [];

        /// <summary>JAR 文件名</summary>
        public string FileName { get; set; } = "";

        /// <summary>JAR 完整路径</summary>
        public string FilePath { get; set; } = "";

        /// <summary>JAR 文件大小 (字节)</summary>
        public long FileSizeBytes { get; set; }

        public override string ToString() => $"{Name} v{Version}";
    }
}