namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// Modrinth 版本中的单个文件。
    /// </summary>
    public record class ModrinthFile
    {
        public string Url { get; set; } = "";
        public string FileName { get; set; } = "";
        public long Size { get; set; }
        public bool Primary { get; set; }

        /// <summary>哈希表 ("sha1" → "...", "sha512" → "...")</summary>
        public Dictionary<string, string> Hashes { get; set; } = [];
    }

    /// <summary>
    /// Modrinth 项目的一个版本。
    /// </summary>
    public record class ModrinthVersion
    {
        public string Id { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public string Name { get; set; } = "";
        public string VersionNumber { get; set; } = "";
        public string Changelog { get; set; } = "";

        /// <summary>release / beta / alpha</summary>
        public string VersionType { get; set; } = "";

        public List<string> GameVersions { get; set; } = [];
        public List<string> Loaders { get; set; } = [];
        public List<ModrinthFile> Files { get; set; } = [];
        public int Downloads { get; set; }

        /// <summary>发布日期 ISO-8601</summary>
        public string DatePublished { get; set; } = "";

        /// <summary>获取主文件（如果无 primary 标记则取第一个）</summary>
        public ModrinthFile? PrimaryFile =>
            Files.Find(f => f.Primary) ?? (Files.Count > 0 ? Files[0] : null);
    }
}