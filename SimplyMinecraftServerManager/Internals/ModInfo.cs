// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 从 Mod JAR 内的 fabric.mod.json / mods.toml 提取的元信息。
    /// </summary>
    public record class ModInfo
    {
        /// <summary>Mod ID (fabric.mod.json → id)</summary>
        public string Id { get; set; } = "";

        /// <summary>Mod 名称 (fabric.mod.json → name)</summary>
        public string Name { get; set; } = "";

        /// <summary>版本号 (fabric.mod.json → version)</summary>
        public string Version { get; set; } = "";

        /// <summary>描述 (fabric.mod.json → description)</summary>
        public string Description { get; set; } = "";

        /// <summary>作者列表 (fabric.mod.json → authors)</summary>
        public List<string> Authors { get; set; } = [];

        /// <summary>贡献者列表 (fabric.mod.json → contributors)</summary>
        public List<string> Contributors { get; set; } = [];

        /// <summary>许可证 (fabric.mod.json → license)</summary>
        public string License { get; set; } = "";

        /// <summary>环境要求 (fabric.mod.json → environment: "client"/"server"/"*")</summary>
        public string Environment { get; set; } = "*";

        /// <summary>图标文件路径 (fabric.mod.json → icon)</summary>
        public string Icon { get; set; } = "";

        /// <summary>入口点 (fabric.mod.json → entrypoints)</summary>
        public Dictionary<string, List<string>> Entrypoints { get; set; } = [];

        /// <summary>Mixin 配置文件列表 (fabric.mod.json → mixins)</summary>
        public List<string> Mixins { get; set; } = [];

        /// <summary>依赖 (fabric.mod.json → depends)</summary>
        public Dictionary<string, object> Depends { get; set; } = [];

        /// <summary>冲突 Mod 列表 (fabric.mod.json → breaks)</summary>
        public Dictionary<string, object> Breaks { get; set; } = [];

        /// <summary>提供的虚拟 Mod ID (fabric.mod.json → provides)</summary>
        public List<string> Provides { get; set; } = [];

        /// <summary>AccessWidener 文件路径 (fabric.mod.json → accessWidener)</summary>
        public string AccessWidener { get; set; } = "";

        /// <summary>内嵌 JAR 文件列表 (fabric.mod.json → jars)</summary>
        public List<string> Jars { get; set; } = [];

        /// <summary>Schema 版本号 (fabric.mod.json → schemaVersion)</summary>
        public int SchemaVersion { get; set; }

        /// <summary>联系信息 (fabric.mod.json → contact)</summary>
        public ModContactInfo Contact { get; set; } = new();

        /// <summary>自定义数据 (fabric.mod.json → custom)</summary>
        public Dictionary<string, object> Custom { get; set; } = [];

        /// <summary>JAR 文件名</summary>
        public string FileName { get; set; } = "";

        /// <summary>JAR 完整路径</summary>
        public string FilePath { get; set; } = "";

        /// <summary>JAR 文件大小 (字节)</summary>
        public long FileSizeBytes { get; set; }

        /// <summary>是否已禁用 (.jar 后缀被改为其他)</summary>
        public bool IsDisabled { get; set; } = false;

        public override string ToString() => $"{Name} v{Version}";
    }

    /// <summary>
    /// Mod 联系信息 (fabric.mod.json → contact)。
    /// </summary>
    public record class ModContactInfo
    {
        public string Homepage { get; set; } = "";
        public string Sources { get; set; } = "";
        public string Issues { get; set; } = "";
    }
}
