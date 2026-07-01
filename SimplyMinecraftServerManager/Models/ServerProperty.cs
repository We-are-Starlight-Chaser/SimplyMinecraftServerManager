// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.


namespace SimplyMinecraftServerManager.Models
{
    /// <summary>
    /// 服务器属性记录类，表示一个键值对形式的服务器配置属性。
    /// </summary>
    public record class ServerProperty(string Key, string Value)
    {
        /// <summary>
        /// 获取或设置属性键。
        /// </summary>
        public string Key { get; set; } = Key;
        /// <summary>
        /// 获取或设置属性值。
        /// </summary>
        public string Value { get; set; } = Value;
    }
}
