using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 读写 Minecraft server.properties（Java Properties 格式）。
    /// 写入时保留原文件的注释与顺序。
    /// </summary>
    public static class ServerPropertiesManager
    {
        /// <summary>
        /// 读取全部属性为字典。
        /// </summary>
        public static Dictionary<string, string> Read(string instanceId)
        {
            string path = PathHelper.GetServerPropertiesPath(instanceId);
            if (!File.Exists(path))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == '!')
                    continue;

                int eq = trimmed.IndexOf('=');
                if (eq < 0) continue;

                string key = trimmed[..eq].Trim();
                string value = trimmed[(eq + 1)..]; // 不 Trim value，保留前后空格（虽然极少见）
                result[key] = value;
            }

            return result;
        }

        /// <summary>
        /// 获取单个属性值。
        /// </summary>
        public static string? GetValue(string instanceId, string key)
        {
            var props = Read(instanceId);
            return props.TryGetValue(key, out string? v) ? v : null;
        }

        /// <summary>
        /// 获取单个属性值，带默认值。
        /// </summary>
        public static string GetValue(string instanceId, string key, string defaultValue)
        {
            return GetValue(instanceId, key) ?? defaultValue;
        }

        /// <summary>
        /// 获取整数属性。
        /// </summary>
        public static int GetInt(string instanceId, string key, int defaultValue = 0)
        {
            string? v = GetValue(instanceId, key);
            return v != null && int.TryParse(v, out int result) ? result : defaultValue;
        }

        /// <summary>
        /// 获取布尔属性。
        /// </summary>
        public static bool GetBool(string instanceId, string key, bool defaultValue = false)
        {
            string? v = GetValue(instanceId, key);
            return v != null && bool.TryParse(v, out bool result) ? result : defaultValue;
        }

        /// <summary>
        /// 设置单个属性值（就地修改文件，保留注释和顺序）。
        /// </summary>
        public static void SetValue(string instanceId, string key, string value)
        {
            string path = PathHelper.GetServerPropertiesPath(instanceId);
            var lines = File.Exists(path)
                ? File.ReadAllLines(path, Encoding.UTF8).ToList()
                : new List<string>();

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
                lines.Add($"{key}={value}");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, lines, Encoding.UTF8);
        }

        /// <summary>
        /// 批量设置属性。
        /// </summary>
        public static void SetValues(string instanceId, Dictionary<string, string> values)
        {
            // 为了效率，一次性读写而不是逐个调用 SetValue
            string path = PathHelper.GetServerPropertiesPath(instanceId);
            var lines = File.Exists(path)
                ? File.ReadAllLines(path, Encoding.UTF8).ToList()
                : new List<string>();

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

            // 追加文件中不存在的新键
            foreach (var kvp in remaining)
                lines.Add($"{kvp.Key}={kvp.Value}");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, lines, Encoding.UTF8);
        }

        /// <summary>
        /// 用完整字典覆盖写入（会丢失原有注释）。
        /// </summary>
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

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, lines, Encoding.UTF8);
        }

        /// <summary>
        /// 删除指定属性。
        /// </summary>
        public static bool RemoveValue(string instanceId, string key)
        {
            string path = PathHelper.GetServerPropertiesPath(instanceId);
            if (!File.Exists(path)) return false;

            var lines = File.ReadAllLines(path, Encoding.UTF8).ToList();
            int removed = lines.RemoveAll(line =>
            {
                string t = line.TrimStart();
                if (t.Length == 0 || t[0] == '#' || t[0] == '!') return false;
                int eq = t.IndexOf('=');
                if (eq < 0) return false;
                return t[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase);
            });

            if (removed > 0)
                File.WriteAllLines(path, lines, Encoding.UTF8);

            return removed > 0;
        }
    }
}