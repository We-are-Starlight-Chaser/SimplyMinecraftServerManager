using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 封装 Minecraft 服务端进程的启动、停止、命令输入和输出监听。
    /// </summary>
    public class ServerProcess : IDisposable
    {
        private Process? _process;
        private bool _disposed;

        /// <summary>实例 UUID</summary>
        public string InstanceId { get; }

        /// <summary>服务器是否正在运行</summary>
        public bool IsRunning => _process is { HasExited: false };

        /// <summary>收到标准输出时触发</summary>
        public event EventHandler<string>? OutputReceived;

        /// <summary>收到标准错误时触发</summary>
        public event EventHandler<string>? ErrorReceived;

        /// <summary>服务器进程退出时触发</summary>
        public event EventHandler<int>? Exited; // 参数为 ExitCode

        public ServerProcess(string instanceId)
        {
            InstanceId = instanceId;
        }

        /// <summary>
        /// 启动服务器。
        /// </summary>
        /// <exception cref="InvalidOperationException">找不到实例或 JDK</exception>
        /// <exception cref="FileNotFoundException">找不到服务端 JAR</exception>
        public void Start()
        {
            if (IsRunning)
                throw new InvalidOperationException("Server is already running.");

            var info = InstanceManager.GetById(InstanceId)
                ?? throw new InvalidOperationException($"Instance '{InstanceId}' not found.");

            string javaPath = InstanceManager.ResolveJdkPath(InstanceId);
            if (string.IsNullOrWhiteSpace(javaPath))
                throw new InvalidOperationException("JDK path is not configured.");

            string workDir = PathHelper.GetInstanceDir(InstanceId);
            string jarPath = PathHelper.GetServerJarPath(InstanceId, info.ServerJar);

            if (!File.Exists(jarPath))
                throw new FileNotFoundException($"Server JAR not found: {jarPath}");

            // 构建参数
            var args = new StringBuilder();
            args.Append($"-Xms{info.MinMemoryMb}M ");
            args.Append($"-Xmx{info.MaxMemoryMb}M ");

            if (!string.IsNullOrWhiteSpace(info.ExtraJvmArgs))
                args.Append($"{info.ExtraJvmArgs.Trim()} ");

            args.Append($"-jar \"{info.ServerJar}\" nogui");

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = args.ToString(),
                    WorkingDirectory = workDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                },
                EnableRaisingEvents = true
            };

            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) OutputReceived?.Invoke(this, e.Data);
            };

            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) ErrorReceived?.Invoke(this, e.Data);
            };

            _process.Exited += (_, _) =>
            {
                int code = -1;
                try { code = _process.ExitCode; } catch { /* ignore */ }
                Exited?.Invoke(this, code);
            };

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        /// <summary>
        /// 向服务器控制台发送命令。
        /// </summary>
        public void SendCommand(string command)
        {
            if (!IsRunning)
                throw new InvalidOperationException("Server is not running.");

            _process!.StandardInput.WriteLine(command);
            _process.StandardInput.Flush();
        }

        /// <summary>发送 "stop" 命令，优雅关闭服务器。</summary>
        public void Stop() => SendCommand("stop");

        /// <summary>强制终止进程。</summary>
        public void Kill()
        {
            if (IsRunning)
                _process!.Kill(entireProcessTree: true);
        }

        /// <summary>等待服务器进程退出。</summary>
        public void WaitForExit() => _process?.WaitForExit();

        /// <summary>等待服务器进程退出（带超时）。</summary>
        public bool WaitForExit(int milliseconds)
            => _process?.WaitForExit(milliseconds) ?? true;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (IsRunning)
            {
                try { _process!.Kill(entireProcessTree: true); } catch { /* best effort */ }
            }

            _process?.Dispose();
            _process = null;

            GC.SuppressFinalize(this);
        }
    }
}