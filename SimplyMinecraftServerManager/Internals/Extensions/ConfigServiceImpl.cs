// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// IConfigService 实现，基于扩展数据目录下的 JSON 文件存储配置。
/// 每个扩展一个 config.json 文件，线程安全。
/// </summary>
internal sealed class ConfigServiceImpl(string extensionDataPath) : IConfigService
{
    private readonly string _configFilePath = Path.Combine(extensionDataPath, "config.json");
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, JsonElement> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;
    private static readonly JsonSerializerOptions _cacheOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions _saveOptions = new() { WriteIndented = true };

    public async Task<string?> GetAsync(string key, string? defaultValue = null, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        if (_cache.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String)
            return element.GetString();
        return defaultValue;
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _cache[key] = JsonSerializer.SerializeToElement(value);
            await SaveAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache.Remove(key))
            {
                await SaveAsync(ct).ConfigureAwait(false);
                return true;
            }
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetKeysAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        return [.. _cache.Keys];
    }

    public async Task<bool> HasKeyAsync(string key, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        return _cache.ContainsKey(key);
    }

    public async Task<T?> GetJsonAsync<T>(string key, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        if (!_cache.TryGetValue(key, out var element) || element.ValueKind == JsonValueKind.Null)
            return default;
        return element.Deserialize<T>();
    }

    public async Task SetJsonAsync<T>(string key, T value, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _cache[key] = JsonSerializer.SerializeToElement(value);
            await SaveAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded) return;
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_loaded) return;
            if (File.Exists(_configFilePath))
            {
                var json = await File.ReadAllTextAsync(_configFilePath, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(json))
                    _cache = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, _cacheOptions) ?? new(StringComparer.OrdinalIgnoreCase);
            }
            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_configFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(_cache, _saveOptions);
        await File.WriteAllTextAsync(_configFilePath, json, ct).ConfigureAwait(false);
    }
}
