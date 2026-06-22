namespace SimplyMinecraftServerManager.Extension.Models
{
    /// <summary>
    /// 表示一个版本范围约束（纯数据结构，不含字符串解析逻辑）。
    /// </summary>
    /// <param name="MinVersion">最小版本边界（null 表示无下限）</param>
    /// <param name="MaxVersion">最大版本边界（null 表示无上限）</param>
    /// <param name="IsMinInclusive">最小边界是否包含</param>
    /// <param name="IsMaxInclusive">最大边界是否包含</param>
    public sealed record VersionRange(
        Version? MinVersion,
        Version? MaxVersion,
        bool IsMinInclusive = true,
        bool IsMaxInclusive = false)
    {
        /// <summary>匹配任意版本</summary>
        public static VersionRange Any { get; } = new(null, null);

        /// <summary>精确匹配单个版本</summary>
        public static VersionRange Exact(Version version) =>
            new(version, version, true, true);

        /// <summary>大于等于指定版本</summary>
        public static VersionRange AtLeast(Version version) =>
            new(version, null, true, false);

        /// <summary>小于指定版本</summary>
        public static VersionRange Below(Version version) =>
            new(null, version, true, false);

        /// <summary>检查指定版本是否满足此范围约束</summary>
        public bool Satisfies(Version version)
        {
            ArgumentNullException.ThrowIfNull(version);

            if (MinVersion is not null)
            {
                int cmp = version.CompareTo(MinVersion);
                if (IsMinInclusive ? cmp < 0 : cmp <= 0) return false;
            }

            if (MaxVersion is not null)
            {
                int cmp = version.CompareTo(MaxVersion);
                if (IsMaxInclusive ? cmp > 0 : cmp >= 0) return false;
            }

            return true;
        }

        public override string ToString()
        {
            if (MinVersion is null && MaxVersion is null) return "*";
            if (MinVersion is not null && MaxVersion is not null && MinVersion.Equals(MaxVersion))
                return $"=={MinVersion}";

            char left = IsMinInclusive ? '[' : '(';
            char right = IsMaxInclusive ? ']' : ')';
            return $"{left}{MinVersion?.ToString() ?? ""},{MaxVersion?.ToString() ?? ""}{right}";
        }
    }


}
