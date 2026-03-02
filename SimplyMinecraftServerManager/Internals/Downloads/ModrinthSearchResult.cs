namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// Modrinth 搜索结果中的单个项目。
    /// </summary>
    public class ModrinthProject
    {
        /// <summary>项目 ID (slug 或 hash)</summary>
        public string ProjectId { get; set; } = "";

        /// <summary>Slug（URL 友好名）</summary>
        public string Slug { get; set; } = "";

        /// <summary>项目名称</summary>
        public string Title { get; set; } = "";

        /// <summary>简介</summary>
        public string Description { get; set; } = "";

        /// <summary>作者</summary>
        public string Author { get; set; } = "";

        /// <summary>图标 URL</summary>
        public string IconUrl { get; set; } = "";

        /// <summary>项目类型 (mod / plugin / datapack / modpack …)</summary>
        public string ProjectType { get; set; } = "";

        /// <summary>下载总数</summary>
        public long Downloads { get; set; }

        /// <summary>关注数</summary>
        public int Follows { get; set; }

        /// <summary>支持的 Minecraft 版本</summary>
        public List<string> GameVersions { get; set; } = [];

        /// <summary>支持的加载器 (bukkit / spigot / paper / purpur …)</summary>
        public List<string> Loaders { get; set; } = [];

        /// <summary>服务端/客户端支持</summary>
        public string ServerSide { get; set; } = ""; // required / optional / unsupported
        public string ClientSide { get; set; } = "";

        /// <summary>最新版本 ID</summary>
        public string LatestVersionId { get; set; } = "";

        /// <summary>Modrinth 页面 URL</summary>
        public string Url => $"https://modrinth.com/{ProjectType}/{Slug}";
    }

    /// <summary>
    /// Modrinth 搜索返回的分页结果。
    /// </summary>
    public class ModrinthSearchResponse
    {
        public List<ModrinthProject> Hits { get; set; } = [];
        public int Offset { get; set; }
        public int Limit { get; set; }
        public int TotalHits { get; set; }
    }
}