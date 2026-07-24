// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 扩展配置存储服务，供扩展安全地读写自身配置。
/// 存储路径由宿主管理，扩展无需关心路径。
/// </summary>
public interface IConfigService
{
    /// <summary>
    /// 读取配置值。键不存在时返回 defaultValue。
    /// </summary>
    Task<string?> GetAsync(string key, string? defaultValue = null, CancellationToken ct = default);

    /// <summary>
    /// 写入配置值。键已存在时覆盖。
    /// </summary>
    Task SetAsync(string key, string value, CancellationToken ct = default);

    /// <summary>
    /// 删除指定配置项。
    /// </summary>
    Task<bool> RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// 列出所有已存储的配置键。
    /// </summary>
    Task<IReadOnlyList<string>> GetKeysAsync(CancellationToken ct = default);

    /// <summary>
    /// 检查指定配置键是否存在。
    /// </summary>
    Task<bool> HasKeyAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// 读取并反序列化 JSON 配置。
    /// </summary>
    Task<T?> GetJsonAsync<T>(string key, CancellationToken ct = default);

    /// <summary>
    /// 序列化并写入 JSON 配置。
    /// </summary>
    Task SetJsonAsync<T>(string key, T value, CancellationToken ct = default);
}
