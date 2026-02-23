### Internals 调用方式

```
using SimplyMinecraftServerManager.Internals;

// ═══════════════ 1. 全局配置 ═══════════════
var config = ConfigManager.Load();
config.DefaultJdkPath = @"C:\Program Files\Java\jdk-21\bin\java.exe";
config.AutoAcceptEula = true;
ConfigManager.Save(config);

// ═══════════════ 2. 创建服务器实例 ═══════════════
var instance = InstanceManager.CreateInstance(
    name:              "生存服",
    serverType:        "paper",
    minecraftVersion:  "1.21.4",
    serverJar:         "paper-1.21.4-123.jar",
    serverJarSourcePath: @"D:\Downloads\paper-1.21.4-123.jar",   // 自动复制到实例目录
    minMemoryMb:       2048,
    maxMemoryMb:       4096,
    extraJvmArgs:      "-XX:+UseG1GC -XX:+ParallelRefProcEnabled"
);
Console.WriteLine($"实例已创建: {instance.Id}");

// ═══════════════ 3. 修改 JDK 路径 ═══════════════
InstanceManager.SetJdkPath(instance.Id,
    @"C:\Program Files\Eclipse Adoptium\jdk-21\bin\java.exe");

// ═══════════════ 4. 获取所有实例 ═══════════════
foreach (var inst in InstanceManager.GetAll())
{
    Console.WriteLine($"[{inst.ServerType}] {inst.Name} ({inst.MinecraftVersion}) - {inst.Id}");
}

// ═══════════════ 5. 获取插件列表 ═══════════════
var plugins = PluginManager.GetPlugins(instance.Id);
foreach (var p in plugins)
{
    string authors = string.Join(", ", p.Authors);
    Console.WriteLine($"  📦 {p.Name} v{p.Version} by {authors} ({p.FileName})");
}

// ═══════════════ 6. 安装/删除插件 ═══════════════
PluginManager.InstallPlugin(instance.Id, @"D:\Plugins\EssentialsX-2.21.0.jar");
PluginManager.DeletePlugin(instance.Id, "OldPlugin-1.0.jar");

// ═══════════════ 7. 读写 server.properties ═══════════════
// 读取全部
var props = ServerPropertiesManager.Read(instance.Id);
foreach (var kvp in props)
    Console.WriteLine($"  {kvp.Key} = {kvp.Value}");

// 读取单个值
int port = ServerPropertiesManager.GetInt(instance.Id, "server-port", 25565);
bool onlineMode = ServerPropertiesManager.GetBool(instance.Id, "online-mode", true);

// 修改单个值
ServerPropertiesManager.SetValue(instance.Id, "server-port", "25566");
ServerPropertiesManager.SetValue(instance.Id, "motd", "\\u00A7bWelcome to my server!");

// 批量修改
ServerPropertiesManager.SetValues(instance.Id, new Dictionary<string, string>
{
    ["max-players"]   = "50",
    ["view-distance"] = "12",
    ["difficulty"]    = "hard",
    ["pvp"]           = "true"
});

// ═══════════════ 8. 启动服务器 ═══════════════
using var server = new ServerProcess(instance.Id);

server.OutputReceived += (_, line) => Console.WriteLine($"[OUT] {line}");
server.ErrorReceived  += (_, line) => Console.WriteLine($"[ERR] {line}");
server.Exited         += (_, code) => Console.WriteLine($"服务器已退出, code={code}");

server.Start();

// 发送命令
server.SendCommand("say Hello from SMSM!");
server.SendCommand("whitelist add Steve");

// 优雅关闭
server.Stop();
server.WaitForExit(30_000); // 等待 30 秒

// ═══════════════ 9. 删除服务器实例 ═══════════════
InstanceManager.DeleteInstance(instance.Id, deleteFiles: true);
```

### 下载插件或服务端 JAR

```
using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads;

// ═══════════════════════════════════════════════
//  1. 全局进度监听
// ═══════════════════════════════════════════════

DownloadManager.Default.ProgressChanged += (_, e) =>
{
    double speedMb = e.SpeedBytesPerSecond / 1048576.0;
    double downloadedMb = e.BytesDownloaded / 1048576.0;

    if (e.IsCompleted)
    {
        Console.WriteLine($"[{e.DisplayName}] ✅ 下载完成 ({downloadedMb:F1} MB)");
    }
    else if (e.IsFailed)
    {
        Console.WriteLine($"[{e.DisplayName}] ❌ 下载失败: {e.ErrorMessage}");
    }
    else if (e.TotalBytes > 0)
    {
        // 总大小已知 → 显示百分比
        double totalMb = e.TotalBytes / 1048576.0;
        Console.WriteLine(
            $"[{e.DisplayName}] {downloadedMb:F1}/{totalMb:F1} MB " +
            $"({e.ProgressPercent:F1}%) - {speedMb:F2} MB/s");
    }
    else
    {
        // 总大小未知 → 只显示已下载量和速度（之前这个分支缺失导致无输出）
        Console.WriteLine(
            $"[{e.DisplayName}] {downloadedMb:F1} MB - {speedMb:F2} MB/s");
    }
};

DownloadManager.Default.TaskCompleted += (_, t) =>
    Console.WriteLine($"✅ {t.DisplayName} 下载完成!");

DownloadManager.Default.TaskFailed += (_, t) =>
    Console.WriteLine($"❌ {t.DisplayName} 下载失败: {t.ErrorMessage}");

// ═══════════════════════════════════════════════
//  2. 下载 Paper 服务端
// ═══════════════════════════════════════════════

var paper = ServerProviderFactory.Get(ServerPlatform.Paper);

// 获取所有版本
var versions = await paper.GetVersionsAsync();
Console.WriteLine($"Paper 支持的版本: {string.Join(", ", versions.Take(10))}...");

// 获取 1.21.4 最新构建
var latestBuild = await paper.GetLatestBuildAsync("1.21.4");
Console.WriteLine($"最新构建: {latestBuild}");

// 创建实例并下载服务端到实例目录
var instance = InstanceManager.CreateInstance(
    name: "Paper 生存服",
    serverType: "paper",
    minecraftVersion: "1.21.4",
    serverJar: latestBuild!.FileName
);

string destPath = PathHelper.GetServerJarPath(instance.Id, latestBuild.FileName);
var downloadResult = await paper.DownloadAsync(latestBuild, destPath);
Console.WriteLine($"Paper 下载状态: {downloadResult.Status}");

// ═══════════════════════════════════════════════
//  3. 下载 Purpur 服务端
// ═══════════════════════════════════════════════

var purpur = ServerProviderFactory.Get(ServerPlatform.Purpur);
var purpurVersions = await purpur.GetVersionsAsync();
var purpurLatest = await purpur.GetLatestBuildAsync(purpurVersions[0]);
if (purpurLatest != null)
{
    Console.WriteLine($"Purpur 最新: {purpurLatest}");
}

// ═══════════════════════════════════════════════
//  4. 下载 Leaves 服务端
// ═══════════════════════════════════════════════

var leaves = ServerProviderFactory.Get(ServerPlatform.Leaves);
var leavesVersions = await leaves.GetVersionsAsync();
Console.WriteLine($"Leaves 版本: {string.Join(", ", leavesVersions)}");

// ═══════════════════════════════════════════════
//  5. 下载 Leaf 服务端
// ═══════════════════════════════════════════════

var leaf = ServerProviderFactory.Get(ServerPlatform.Leaf);
var leafVersions = await leaf.GetVersionsAsync();
Console.WriteLine($"Leaf 版本: {string.Join(", ", leafVersions)}");

// ═══════════════════════════════════════════════
//  6. 搜索 Modrinth 插件
// ═══════════════════════════════════════════════

var modrinth = new ModrinthProvider();

// 搜索 EssentialsX
var searchResult = await modrinth.SearchAsync(
    query: "EssentialsX",
    loaders: new[] { "bukkit", "spigot", "paper" },
    gameVersions: new[] { "1.21.4" },
    projectType: "plugin",
    limit: 10
);

Console.WriteLine($"\n搜索到 {searchResult.TotalHits} 个结果:");
foreach (var hit in searchResult.Hits)
{
    Console.WriteLine($"  📦 {hit.Title} - {hit.Description}");
    Console.WriteLine($"     下载数: {hit.Downloads:N0} | {hit.Url}");
}

// ═══════════════════════════════════════════════
//  7. 获取插件版本并下载
// ═══════════════════════════════════════════════

if (searchResult.Hits.Count > 0)
{
    var project = searchResult.Hits[0];

    // 获取该插件的所有版本
    var pluginVersions = await modrinth.GetVersionsAsync(
        project.ProjectId,
        loaders: new[] { "bukkit", "spigot", "paper" },
        gameVersions: new[] { "1.21.4" }
    );

    Console.WriteLine($"\n{project.Title} 有 {pluginVersions.Count} 个适配版本:");
    foreach (var v in pluginVersions.Take(5))
    {
        Console.WriteLine($"  v{v.VersionNumber} ({v.VersionType}) - {v.PrimaryFile?.FileName}");
    }

    // 下载最新版本到实例 plugins 目录
    if (pluginVersions.Count > 0)
    {
        var latestPlugin = pluginVersions[0];
        string pluginDest = System.IO.Path.Combine(
            PathHelper.GetPluginsDir(instance.Id),
            latestPlugin.PrimaryFile!.FileName);

        var pluginDownload = await modrinth.DownloadVersionAsync(
            latestPlugin, pluginDest);

        Console.WriteLine($"插件下载状态: {pluginDownload.Status}");
    }
}

// ═══════════════════════════════════════════════
//  8. 一键搜索+下载插件
// ═══════════════════════════════════════════════

var quickDownload = await modrinth.SearchAndDownloadAsync(
    query: "WorldEdit",
    instanceId: instance.Id,
    mcVersion: "1.21.4",
    loaders: new[] { "bukkit", "spigot", "paper" }
);

if (quickDownload != null)
    Console.WriteLine($"WorldEdit 下载: {quickDownload.Status}");

// ═══════════════════════════════════════════════
//  9. 批量并行下载多个插件
// ═══════════════════════════════════════════════

var pluginNames = new[] { "LuckPerms", "Vault", "PlaceholderAPI" };
var downloadTasks = new List<Task<DownloadTask?>>();

foreach (var name in pluginNames)
{
    downloadTasks.Add(modrinth.SearchAndDownloadAsync(
        query: name,
        instanceId: instance.Id,
        mcVersion: "1.21.4"
    ));
}

var results = await Task.WhenAll(downloadTasks);
foreach (var r in results)
{
    if (r != null)
        Console.WriteLine($"  {r.DisplayName}: {r.Status}");
}

// ═══════════════════════════════════════════════
// 10. 同时下载多个平台的服务端（演示并行能力）
// ═══════════════════════════════════════════════

var platforms = ServerProviderFactory.GetAll();
var versionTasks = platforms.Select(async p =>
{
    var v = await p.GetVersionsAsync();
    return (p.Platform, Versions: v);
});

foreach (var (platform, vers) in await Task.WhenAll(versionTasks))
{
    Console.WriteLine($"{platform}: {string.Join(", ", vers.Take(5))}...");
}
```

### 下载并自动管理 JDK

```
using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads;

// ═══════════════════════════════════════════════
//  1. 查询可用 JDK 版本
// ═══════════════════════════════════════════════

var adoptium = JdkProviderFactory.Get(JdkDistribution.Adoptium);
var zulu     = JdkProviderFactory.Get(JdkDistribution.Zulu);

var adoptiumVersions = await adoptium.GetAvailableMajorVersionsAsync();
var zuluVersions     = await zulu.GetAvailableMajorVersionsAsync();

Console.WriteLine($"Adoptium: {string.Join(", ", adoptiumVersions)}");
Console.WriteLine($"Zulu:     {string.Join(", ", zuluVersions)}");

// ═══════════════════════════════════════════════
//  2. 获取 JDK 21 的所有构建
// ═══════════════════════════════════════════════

var jdk21Builds = await adoptium.GetBuildsAsync(21);
Console.WriteLine($"\nAdoptium JDK 21 构建 ({jdk21Builds.Count} 个):");
foreach (var b in jdk21Builds.Take(3))
{
    Console.WriteLine($"  {b.FullVersion} - {b.FileName} ({b.FileSize / 1048576} MB)");
}

// ═══════════════════════════════════════════════
//  3. 下载并安装 JDK（手动指定版本）
// ═══════════════════════════════════════════════

// 进度监听
DownloadManager.Default.ProgressChanged += (_, e) =>
{
    if (e.IsCompleted)
        Console.WriteLine($"✅ [{e.DisplayName}] 下载完成");
    else if (e.TotalBytes > 0)
        Console.WriteLine($"[{e.DisplayName}] {e.ProgressPercent:F1}%");
};

var latestJdk21 = await adoptium.GetLatestAsync(21);
Console.WriteLine($"\n准备安装: {latestJdk21}");

var installed = await JdkManager.DownloadAndInstallAsync(
    latestJdk21!,
    progress: new Progress<int>(pct => Console.WriteLine($"  解压进度: {pct}%"))
);

Console.WriteLine($"安装完成: {installed}");
Console.WriteLine($"java.exe: {installed.JavaExecutable}");
Console.WriteLine($"有效: {installed.IsValid}");

// ═══════════════════════════════════════════════
//  4. 一键安装（AutoInstall）
// ═══════════════════════════════════════════════

// 已安装则直接返回，未安装则自动下载
var jdk17 = await JdkManager.AutoInstallAsync(17);
Console.WriteLine($"\nJDK 17: {jdk17.JavaExecutable}");

// 更便捷：只拿 java.exe 路径
string java21 = await JdkManager.EnsureJdkAsync(21);
Console.WriteLine($"JDK 21 java.exe: {java21}");

// ═══════════════════════════════════════════════
//  5. 根据 MC 版本自动推荐并安装 JDK
// ═══════════════════════════════════════════════

string mcVersion = "1.21.4";
int recommended = JdkManager.RecommendJdkVersion(mcVersion);
Console.WriteLine($"\nMC {mcVersion} 推荐 JDK {recommended}");

string javaPath = await JdkManager.EnsureJdkAsync(recommended);
Console.WriteLine($"java.exe: {javaPath}");

// ═══════════════════════════════════════════════
//  6. 查看所有已安装 JDK
// ═══════════════════════════════════════════════

Console.WriteLine("\n已安装的 JDK:");
foreach (var jdk in JdkManager.GetInstalledJdks())
{
    string status = jdk.IsValid ? "✅" : "❌";
    Console.WriteLine($"  {status} {jdk}");
}

// ═══════════════════════════════════════════════
//  7. 创建服务器实例时自动匹配 JDK
// ═══════════════════════════════════════════════

string serverMcVersion = "1.21.4";
int jdkMajor = JdkManager.RecommendJdkVersion(serverMcVersion);
string autoJava = await JdkManager.EnsureJdkAsync(jdkMajor);

var instance = InstanceManager.CreateInstance(
    name: "自动配置服",
    serverType: "paper",
    minecraftVersion: serverMcVersion,
    jdkPath: autoJava    // ← 自动匹配的 java.exe
);

Console.WriteLine($"\n实例 JDK: {instance.JdkPath}");

// ═══════════════════════════════════════════════
//  8. Zulu JDK（切换发行版）
// ═══════════════════════════════════════════════

var zuluJdk21 = await JdkManager.AutoInstallAsync(
    21, JdkDistribution.Zulu);
Console.WriteLine($"\nZulu JDK 21: {zuluJdk21.JavaExecutable}");

// ═══════════════════════════════════════════════
//  9. 卸载 JDK
// ═══════════════════════════════════════════════

bool removed = JdkManager.Uninstall(17, JdkDistribution.Adoptium);
Console.WriteLine($"\n卸载 JDK 17: {(removed ? "成功" : "未找到")}");

// ═══════════════════════════════════════════════
// 10. 完整工作流：创建服务器 + 自动 JDK + 下载服务端
// ═══════════════════════════════════════════════

string targetMcVer = "1.21.4";

// 自动安装匹配的 JDK
int recJdk = JdkManager.RecommendJdkVersion(targetMcVer);
string targetJava = await JdkManager.EnsureJdkAsync(recJdk);

// 获取 Paper 最新构建
var paperProvider = ServerProviderFactory.Get(ServerPlatform.Paper);
var paperBuild = await paperProvider.GetLatestBuildAsync(targetMcVer);

// 创建实例
var newInstance = InstanceManager.CreateInstance(
    name: "全自动生存服",
    serverType: "paper",
    minecraftVersion: targetMcVer,
    serverJar: paperBuild!.FileName,
    jdkPath: targetJava
);

// 下载服务端 JAR
string jarDest = PathHelper.GetServerJarPath(newInstance.Id, paperBuild.FileName);
await paperProvider.DownloadAsync(paperBuild, jarDest);

Console.WriteLine($"\n🎉 服务器创建完成!");
Console.WriteLine($"   名称: {newInstance.Name}");
Console.WriteLine($"   版本: {newInstance.MinecraftVersion}");
Console.WriteLine($"   JDK:  {newInstance.JdkPath}");
Console.WriteLine($"   目录: {PathHelper.GetInstanceDir(newInstance.Id)}");
```