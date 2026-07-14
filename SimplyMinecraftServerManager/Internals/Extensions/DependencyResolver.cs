// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Diagnostics;
using System.Reflection;
using SimplyMinecraftServerManager.Extension.Interfaces;
using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 扩展依赖解析器。
/// 负责拓扑排序、版本兼容性检查、循环依赖检测。
/// </summary>
internal sealed class DependencyResolver
{
    private readonly Dictionary<string, IExtensionMetadata> _extensions;
    private readonly ILogger _logger;

    public DependencyResolver(Dictionary<string, IExtensionMetadata> extensions, ILogger logger)
    {
        _extensions = extensions;
        _logger = logger;
    }

    /// <summary>
    /// 拓扑排序扩展加载顺序（Kahn 算法）。
    /// 返回按依赖顺序排列的扩展 ID 列表。
    /// 如果存在循环依赖或缺失依赖，抛出异常。
    /// </summary>
    public List<string> Resolve()
    {
        var inDegree = new Dictionary<string, int>();
        var dependents = new Dictionary<string, List<string>>();

        foreach (var (id, _) in _extensions)
        {
            inDegree.TryAdd(id, 0);
            dependents.TryAdd(id, []);
        }

        // 构建依赖图
        foreach (var (id, meta) in _extensions)
        {
            if (meta.Dependencies is not { Length: > 0 }) continue;

            foreach (var dep in meta.Dependencies)
            {
                if (!_extensions.ContainsKey(dep.Id))
                {
                    throw new InvalidOperationException(
                        $"扩展 '{id}' 依赖 '{dep.Id}'，但该依赖未找到。");
                }

                dependents[dep.Id].Add(id);
                inDegree[id] = inDegree.GetValueOrDefault(id) + 1;
            }
        }

        // Kahn 拓扑排序
        var queue = new Queue<string>();
        foreach (var (id, degree) in inDegree)
        {
            if (degree == 0) queue.Enqueue(id);
        }

        var sorted = new List<string>();
        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            sorted.Add(current);

            foreach (string dependent in dependents[current])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        if (sorted.Count != _extensions.Count)
        {
            var circular = string.Join(", ", _extensions.Keys.Except(sorted));
            throw new InvalidOperationException(
                $"检测到循环依赖，涉及扩展: {circular}");
        }

        return sorted;
    }

    /// <summary>
    /// 验证所有扩展的 HostApiVersion 兼容性。
    /// 返回不兼容的扩展 ID 列表。
    /// </summary>
    public List<string> CheckHostCompatibility(Version hostVersion)
    {
        var incompatible = new List<string>();

        foreach (var (id, meta) in _extensions)
        {
            if (meta.HostApiVersion is not null && meta.Version < meta.HostApiVersion)
            {
                _logger.Warn($"扩展 '{id}' 需要宿主 API >= {meta.HostApiVersion}，当前宿主版本 {hostVersion}");
                incompatible.Add(id);
            }
        }

        return incompatible;
    }

    /// <summary>
    /// 验证所有依赖的版本范围是否满足。
    /// </summary>
    public List<string> ValidateDependencyVersions()
    {
        var invalid = new List<string>();

        foreach (var (id, meta) in _extensions)
        {
            if (meta.Dependencies is not { Length: > 0 }) continue;

            foreach (var dep in meta.Dependencies)
            {
                if (_extensions.TryGetValue(dep.Id, out var depMeta) && dep.Range is not null)
                {
                    if (!dep.Range.Satisfies(depMeta.Version))
                    {
                        _logger.Warn(
                            $"扩展 '{id}' 依赖 '{dep.Id}' 版本 {depMeta.Version}，" +
                            $"但要求版本范围 {dep.Range}");
                        invalid.Add(id);
                    }
                }
            }
        }

        return invalid;
    }
}
