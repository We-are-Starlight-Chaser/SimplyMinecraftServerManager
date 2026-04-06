using SimplyMinecraftServerManager.Internals;
using System.Windows.Controls;

namespace SimplyMinecraftServerManager.Controls
{
    public partial class ServerCard : UserControl
    {
        // Dependency Properties
        public static readonly DependencyProperty InstanceIdProperty =
            DependencyProperty.Register(nameof(InstanceId), typeof(string), typeof(ServerCard), new PropertyMetadata(""));

        public static readonly DependencyProperty InstanceNameProperty =
            DependencyProperty.Register(nameof(InstanceName), typeof(string), typeof(ServerCard), new PropertyMetadata("未命名服务器"));

        public static readonly DependencyProperty ServerTypeProperty =
            DependencyProperty.Register(nameof(ServerType), typeof(string), typeof(ServerCard), new PropertyMetadata(""));

        public static readonly DependencyProperty MinecraftVersionProperty =
            DependencyProperty.Register(nameof(MinecraftVersion), typeof(string), typeof(ServerCard), new PropertyMetadata(""));

        public static readonly DependencyProperty IsRunningProperty =
            DependencyProperty.Register(nameof(IsRunning), typeof(bool), typeof(ServerCard), new PropertyMetadata(false, OnIsRunningChanged));

        public string InstanceId
        {
            get => (string)GetValue(InstanceIdProperty);
            set => SetValue(InstanceIdProperty, value);
        }

        public string InstanceName
        {
            get => (string)GetValue(InstanceNameProperty);
            set => SetValue(InstanceNameProperty, value);
        }

        public string ServerType
        {
            get => (string)GetValue(ServerTypeProperty);
            set => SetValue(ServerTypeProperty, value);
        }

        public string MinecraftVersion
        {
            get => (string)GetValue(MinecraftVersionProperty);
            set => SetValue(MinecraftVersionProperty, value);
        }

        public bool IsRunning
        {
            get => (bool)GetValue(IsRunningProperty);
            set => SetValue(IsRunningProperty, value);
        }

        // Events
        public event EventHandler<string>? StartRequested;
        public event EventHandler<string>? StopRequested;

        public ServerCard()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 从 InstanceInfo 创建 ServerCard
        /// </summary>
        public ServerCard(InstanceInfo info) : this()
        {
            if (info != null)
            {
                var metadata = ServerJarMetadataReader.Read(info);
                InstanceId = info.Id;
                InstanceName = info.Name;
                ServerType = metadata.ServerType;
                MinecraftVersion = metadata.MinecraftVersion;
            }
        }

        /// <summary>
        /// 兼容旧版本的构造函数
        /// </summary>
        [Obsolete("Use ServerCard(InstanceInfo) instead")]
        public ServerCard(string sn) : this()
        {
            InstanceName = sn;
        }

        private static void OnIsRunningChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // 可用于更新 UI 状态指示器
        }

        private void OnStartClick(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(InstanceId))
            {
                StartRequested?.Invoke(this, InstanceId);
            }
        }

        private void OnStopClick(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(InstanceId))
            {
                StopRequested?.Invoke(this, InstanceId);
            }
        }

        /// <summary>
        /// 从 InstanceInfo 更新显示数据
        /// </summary>
        public void UpdateFromInfo(InstanceInfo info)
        {
            if (info == null) return;

            var metadata = ServerJarMetadataReader.Read(info);
            InstanceId = info.Id;
            InstanceName = info.Name;
            ServerType = metadata.ServerType;
            MinecraftVersion = metadata.MinecraftVersion;
        }
    }
}
