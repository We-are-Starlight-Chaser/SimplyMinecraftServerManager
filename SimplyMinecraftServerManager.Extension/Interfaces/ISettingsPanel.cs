// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 自定义设置面板扩展接口。
/// 实现此接口可在设置页面中注册扩展专属的配置 UI。
/// </summary>
public interface ISettingsPanel
{
    /// <summary>设置面板的显示标题</summary>
    string Title { get; }

    /// <summary>设置面板在设置页面中的排序位置（越小越靠前）</summary>
    int Order { get; }

    /// <summary>
    /// 加载当前配置值。
    /// 宿主在显示设置面板时调用。
    /// </summary>
    void Load();

    /// <summary>
    /// 保存配置值。
    /// 宿主在用户确认保存时调用。
    /// </summary>
    void Save();
}
