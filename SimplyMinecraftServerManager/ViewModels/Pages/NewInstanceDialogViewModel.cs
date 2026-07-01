// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Internals;
using System.Collections.ObjectModel;
using Wpf.Ui.Abstractions.Controls;
using Microsoft.Win32;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    /// <summary>
    /// 新建服务器实例对话框的视图模型
    /// </summary>
    public partial class NewInstanceDialogViewModel : ObservableObject
    {
        /// <summary>
        /// 可用的服务器版本列表
        /// </summary>
        private readonly ObservableCollection<string> _availableVersions;

        /// <summary>
        /// 可用的 JDK 运行时列表
        /// </summary>
        private readonly ObservableCollection<JdkDisplayItem> _availableJdks;

        /// <summary>
        /// 创建实例的回调函数
        /// </summary>
        private readonly Func<Task> _onCreate;

        /// <summary>
        /// 取消操作的回调函数
        /// </summary>
        private readonly Action _onCancel;

        /// <summary>
        /// 初始化新建实例对话框视图模型
        /// </summary>
        /// <param name="availableVersions">可用的服务器版本集合</param>
        /// <param name="availableJdks">可用的 JDK 运行时集合</param>
        /// <param name="onCreate">创建实例时的回调函数</param>
        /// <param name="onCancel">取消操作时的回调函数</param>
        public NewInstanceDialogViewModel(
            ObservableCollection<string> availableVersions,
            ObservableCollection<JdkDisplayItem> availableJdks,
            Func<Task> onCreate,
            Action onCancel)
        {
            _availableVersions = availableVersions;
            _availableJdks = availableJdks;
            _onCreate = onCreate;
            _onCancel = onCancel;

            if (_availableVersions.Count > 0)
            {
                SelectedVersion = _availableVersions[0];
            }

            if (_availableJdks.Count > 0)
            {
                SelectedJdk = _availableJdks[0];
            }
        }

        /// <summary>
        /// 实例名称
        /// </summary>
        [ObservableProperty]
        private string _instanceName = "";

        /// <summary>
        /// 服务器类型（如 Paper、Purpur 等）
        /// </summary>
        [ObservableProperty]
        private string _serverType = "Paper";

        /// <summary>
        /// 当前选中的服务器版本
        /// </summary>
        [ObservableProperty]
        private string? _selectedVersion;

        /// <summary>
        /// 自定义 JAR 文件路径
        /// </summary>
        [ObservableProperty]
        private string? _customJarPath = "";

        /// <summary>
        /// 是否使用自定义 JAR 文件
        /// </summary>
        [ObservableProperty]
        private bool _useCustomJar = false;

        /// <summary>
        /// 当前选中的 JDK 运行时
        /// </summary>
        [ObservableProperty]
        private JdkDisplayItem? _selectedJdk;

        /// <summary>
        /// 最小内存分配（MB）
        /// </summary>
        [ObservableProperty]
        private int _minMemory = 1024;

        /// <summary>
        /// 最大内存分配（MB）
        /// </summary>
        [ObservableProperty]
        private int _maxMemory = 2048;

        /// <summary>
        /// 状态消息
        /// </summary>
        [ObservableProperty]
        private string _statusMessage = "";

        /// <summary>
        /// 是否正在创建实例
        /// </summary>
        [ObservableProperty]
        private bool _isCreating = false;

        /// <summary>
        /// 获取可用的服务器版本列表
        /// </summary>
        public ObservableCollection<string> AvailableVersions => _availableVersions;

        /// <summary>
        /// 获取可用的 JDK 运行时列表
        /// </summary>
        public ObservableCollection<JdkDisplayItem> AvailableJdks => _availableJdks;

        /// <summary>
        /// 支持的服务器类型列表
        /// </summary>
        public static string[] ServerTypes => ["Paper", "Purpur", "Leaves", "Leaf", "Folia"];

        /// <summary>
        /// 浏览并选择自定义 JAR 文件
        /// </summary>
        [RelayCommand]
        private void BrowseCustomJar()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "JAR 文件 (*.jar)|*.jar|所有文件 (*.*)|*.*",
                Title = "选择自定义服务端 JAR 文件",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                CustomJarPath = openFileDialog.FileName;
            }
        }

        /// <summary>
        /// 异步创建服务器实例
        /// </summary>
        /// <returns>表示异步操作的任务</returns>
        public async Task CreateAsync()
        {
            await _onCreate();
        }

        /// <summary>
        /// 取消创建操作
        /// </summary>
        public void Cancel()
        {
            _onCancel();
        }
    }
}
