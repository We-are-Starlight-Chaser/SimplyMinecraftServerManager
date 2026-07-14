// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.Extension.Models;

/// <summary>
/// 扩展自定义导航栏项的配置信息。
/// </summary>
public sealed record NavigationItemInfo
{
    /// <summary>导航项唯一标识</summary>
    public required string Id { get; init; }

    /// <summary>导航项显示文本</summary>
    public required string Label { get; init; }

    /// <summary>导航项图标</summary>
    public SymbolRegular Icon { get; init; } = SymbolRegular.Empty;

    /// <summary>
    /// 导航项在侧边栏中的排序位置。
    /// 数值越小越靠前；相同值按注册顺序排列。
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// 点击导航项时触发的回调。
    /// </summary>
    public required Func<CancellationToken, Task> OnNavigate { get; init; }
}
