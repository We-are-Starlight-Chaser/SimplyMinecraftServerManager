// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.IO;
using System.Text;
using SimplyMinecraftServerManager.Helpers;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// Minecraft server.properties 文件的读写管理器，支持缓存和线程安全操作。
    /// </summary>
    public static class ServerPropertiesManager
    {
        private static readonly ReaderWriterLockSlim FileLock = new(LockRecursionPolicy.NoRecursion);
        private static readonly MemoryCache<Dictionary<string, string>> _propsCache = new(TimeSpan.FromSeconds(30), 50);

        /// <summary>
        /// 读取指定实例的 server.properties 文件并返回键值对字典。
        /// </summary>
        /// <param name="instanceId">实例 ID</param>
        /// <returns>配置键值对字典</returns>
        public static Dictionary<string, string> Read(string instanceId)
        {
            if (_propsCache.TryGet(instanceId, out var cached))
                return cached;

            string path = PathHelper.GetServerPropertiesPath(instanceId);
            if (!File.Exists(path))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            FileLock.EnterReadLock();
            try
            {
                foreach (string line in ReadAllLinesWithRetry(path))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == '!')
                        continue;

                    int eq = trimmed.IndexOf('=');
                    if (eq < 0) continue;

                    string key = trimmed[..eq].Trim();
                    string value = trimmed[(eq + 1)..];
                    result[key] = value;
                }
            }
            finally
            {
                FileLock.ExitReadLock();
            }

            _propsCache.Set(instanceId, result);
            return result;
        }

        /// <summary>清除指定实例的配置缓存</summary>
        /// <param name="instanceId">实例 ID</param>
        public static void InvalidateCache(string instanceId)
        {
            _propsCache.Remove(instanceId);
        }

        /// <summary>
        /// 获取指定键的值。
        /// </summary>
        /// <param name="instanceId">实例 ID</param>
        /// <param name="key">配置键名</param>
        /// <returns>键对应的值，不存在则返回 null</returns>
        public static string? GetValue(string instanceId, string key)
        {
            var props = Read(instanceId);
            return props.TryGetValue(key, out string? v) ? v : null;
        }

        /// <summary>
        /// 获取指定键的值，不存在时返回默认值。
        /// </summary>
        /// <param name="instanceId">实例 ID</param>
        /// <param name="key">配置键名</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>键对应的值或默认值</returns>
        public static string GetValue(string instanceId, string key, string defaultValue)
        {
            return GetValue(instanceId, key) ?? defaultValue;
        }

        /// <summary>
        /// 获取指定键的整数值。
        /// </summary>
        /// <param name="instanceId">实例 ID</param>
        /// <param name="key">配置键名</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>解析后的整数值</returns>
        public static int GetInt(string instanceId, string key, int defaultValue = 0)
        {
            string? v = GetValue(instanceId, key);
            return v != null && int.TryParse(v, out int result) ? result : defaultValue;
        }

        /// <summary>
        /// 获取指定键的布尔值。
        /// </summary>
        /// <param name="instanceId">实例 ID</param>
        /// <param name="key">配置键名</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>解析后的布尔值</returns>
        public static bool GetBool(string instanceId, string key, bool defaultValue = false)
        {
            string? v = GetValue(instanceId, key);
            return v != null && bool.TryParse(v, out bool result) ? result : defaultValue;
        }

        /// <summary>
        /// 设置单个配置项的值，若键已存在则更新，否则追加。
        /// </summary>
        /// <param name="instanceId">实例 ID</param>
        /// <param name="key">配置键名</param>
        /// <param name="value">要设置的值</param>
        public static void SetValue(string instanceId, string key, string value)
        {
            string path = PathHelper.GetServerPropertiesPath(instanceId);
            FileLock.EnterWriteLock();
            try
            {
                var lines = File.Exists(path)
                    ? ReadAllLinesWithRetry(path).ToList()
                    : [];

                bool found = false;

                for (int i = 0; i < lines.Count; i++)
                {
                    string trimmed = lines[i].TrimStart();
                    if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == '!')
                        continue;

                    int eq = trimmed.IndexOf('=');
                    if (eq < 0) continue;

                    string k = trimmed[..eq].Trim();
                    if (k.Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = $"{key}={value}";
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    lines.Add($"{key}={value}");
                }

                WriteAllLinesWithRetry(path, lines);
            }
            finally
            {
                FileLock.ExitWriteLock();
            }
            InvalidateCache(instanceId);
        }

        /// <summary>
        /// 批量设置多个配置项。
        /// </summary>
        /// <param name="instanceId">实例 ID</param>
        /// <param name="values">要设置的键值对字典</param>
        public static void SetValues(string instanceId, Dictionary<string, string> values)
        {
            string path = PathHelper.GetServerPropertiesPath(instanceId);
            FileLock.EnterWriteLock();
            try
            {
                var lines = File.Exists(path)
                    ? ReadAllLinesWithRetry(path).ToList()
                    : [];

                var remaining = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < lines.Count && remaining.Count > 0; i++)
                {
                    string trimmed = lines[i].TrimStart();
                    if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == '!')
                        continue;

                    int eq = trimmed.IndexOf('=');
                    if (eq < 0) continue;

                    string k = trimmed[..eq].Trim();

                    if (remaining.TryGetValue(k, out string? newVal))
                    {
                        lines[i] = $"{k}={newVal}";
                        remaining.Remove(k);
                    }
                }

                foreach (var kvp in remaining)
                    lines.Add($"{kvp.Key}={kvp.Value}");

                WriteAllLinesWithRetry(path, lines);
            }
            finally
            {
                FileLock.ExitWriteLock();
            }
            InvalidateCache(instanceId);
        }

        /// <summary>
        /// 覆盖写入整个 server.properties 文件。
        /// </summary>
        /// <param name="instanceId">实例 ID</param>
        /// <param name="properties">完整的配置键值对</param>
        public static void WriteAll(string instanceId, Dictionary<string, string> properties)
        {
            string path = PathHelper.GetServerPropertiesPath(instanceId);
            var lines = new List<string>
            {
                "#Minecraft server properties",
                $"#Generated by SMSM {DateTime.Now:ddd MMM dd HH:mm:ss zzz yyyy}"
            };

            foreach (var kvp in properties.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                lines.Add($"{kvp.Key}={kvp.Value}");

            FileLock.EnterWriteLock();
            try
            {
                WriteAllLinesWithRetry(path, lines);
            }
            finally
            {
                FileLock.ExitWriteLock();
            }
            InvalidateCache(instanceId);
        }

        /// <summary>
        /// 移除指定的配置项。
        /// </summary>
        /// <param name="instanceId">实例 ID</param>
        /// <param name="key">要移除的配置键名</param>
        /// <returns>是否成功移除</returns>
        public static bool RemoveValue(string instanceId, string key)
        {
            string path = PathHelper.GetServerPropertiesPath(instanceId);
            if (!File.Exists(path)) return false;

            FileLock.EnterWriteLock();
            try
            {
                var lines = ReadAllLinesWithRetry(path).ToList();
                int removed = lines.RemoveAll(line =>
                {
                    string t = line.TrimStart();
                    if (t.Length == 0 || t[0] == '#' || t[0] == '!') return false;
                    int eq = t.IndexOf('=');
                    if (eq < 0) return false;
                    return t[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase);
                });

                if (removed > 0)
                    WriteAllLinesWithRetry(path, lines);

                if (removed > 0) InvalidateCache(instanceId);
                return removed > 0;
            }
            finally
            {
                FileLock.ExitWriteLock();
            }
        }

        private static string[] ReadAllLinesWithRetry(string path, int retryCount = 6, int delayMs = 40)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    var lines = new List<string>();
                    while (!reader.EndOfStream)
                    {
                        lines.Add(reader.ReadLine() ?? string.Empty);
                    }

                    return [.. lines];
                }
                catch (IOException) when (attempt < retryCount)
                {
                    Thread.Sleep(delayMs * (attempt + 1));
                }
            }
        }

        private static void WriteAllLinesWithRetry(string path, IEnumerable<string> lines, int retryCount = 6, int delayMs = 40)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                    using var writer = new StreamWriter(stream, Encoding.UTF8);
                    foreach (var line in lines)
                    {
                        writer.WriteLine(line);
                    }

                    writer.Flush();
                    stream.Flush(true);
                    return;
                }
                catch (IOException) when (attempt < retryCount)
                {
                    Thread.Sleep(delayMs * (attempt + 1));
                }
            }
        }
    }
}
