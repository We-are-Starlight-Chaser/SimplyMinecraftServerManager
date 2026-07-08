// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Internals;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace SimplyMinecraftServerManager.ViewModels.Dialogs
{
    public partial class ServerPingerDialogViewModel : ObservableObject
    {
        [ObservableProperty] private string _ip = "";
        [ObservableProperty] private string _port = "25565";
        [ObservableProperty] private ObservableCollection<MinecraftServerPing.Player> _players = [];
        [ObservableProperty] private string _statusText = "就绪";
        [ObservableProperty] private string _motd = "";
        [ObservableProperty] private string _versionName = "";
        [ObservableProperty] private int _protocolVersion;
        [ObservableProperty] private int _onlinePlayers;
        [ObservableProperty] private int _maxPlayers;
        [ObservableProperty] private long _latencyMs;
        [ObservableProperty] private BitmapImage? _serverIconSource;

        [RelayCommand(AllowConcurrentExecutions = false)]
        private async Task PingServerAsync()
        {
            try
            {
                if (!int.TryParse(Port, out int port) || port < 1 || port > 65535)
                {
                    StatusText = "端口号不合法（应为1-65535）";
                    return;
                }

                string trimmedIp = Ip.Trim();
                if (!DomainRegex().IsMatch(trimmedIp) && !IpRegex().IsMatch(trimmedIp))
                {
                    StatusText = "IP地址或域名不合法";
                    return;
                }

                StatusText = "正在Ping服务器...";

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var result = await MinecraftServerPing.PingAsync(trimmedIp, port, ct: cts.Token);

                if (result == null)
                {
                    StatusText = "Ping失败：服务器无响应或超时";
                    return;
                }
                Players.Clear();
                foreach (var p in result.Players)
                    Players.Add(p);

                Motd = result.Motd;
                VersionName = result.VersionName;
                ProtocolVersion = result.ProtocolVersion;
                OnlinePlayers = result.OnlinePlayers;
                MaxPlayers = result.MaxPlayers;
                LatencyMs = result.LatencyMs;
                ServerIconSource = ParseBase64Icon(result.Icon);

                StatusText = $"成功 - 在线玩家: {result.OnlinePlayers}/{result.MaxPlayers}";
            }
            catch (OperationCanceledException)
            {
                StatusText = "Ping超时";
            }
            catch (Exception ex)
            {
                StatusText = "Ping失败";
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _ = new Wpf.Ui.Controls.MessageBox
                    {
                        Title = "SMSM",
                        Content = $"发生未知错误: {ex.Message}"
                    }.ShowDialogAsync();
                });
            }
        }
        private static BitmapImage? ParseBase64Icon(string? icon)
        {
            if (string.IsNullOrEmpty(icon) || !icon.StartsWith("data:image/png;base64,"))
                return null;

            try
            {
                var base64 = icon["data:image/png;base64,".Length..];
                var bytes = Convert.FromBase64String(base64);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = new System.IO.MemoryStream(bytes);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        [GeneratedRegex(@"^((25[0-5]|2[0-4]\d|1\d{2}|[1-9]?\d)\.){3}(25[0-5]|2[0-4]\d|1\d{2}|[1-9]?\d)$")]
        private static partial Regex IpRegex();

        [GeneratedRegex(@"^([a-zA-Z0-9]+(-[a-zA-Z0-9]+)*\.)+[a-zA-Z]{2,}$")]
        private static partial Regex DomainRegex();
    }
}