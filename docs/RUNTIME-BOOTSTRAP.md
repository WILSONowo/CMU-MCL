# 首次运行依赖引导器

## 用户流程

完整解压后运行 **CMUmcl.Start.exe**：

1. 检测 x64 .NET 10 Core 与 Desktop Runtime。读取应用本地 `runtime/`、进程环境变量、官方 x64 注册表位置及标准安装目录，并使用微软 hostfxr 解析实际的 `CMUmcl.runtimeconfig.json`。
2. 可用时直接启动同目录的 `CMUmcl.exe`，引导器退出。选择的运行库路径只传给子进程，不修改系统环境变量。
3. 缺少依赖时显示说明、下载进度、取消、失败重试和微软下载页面入口。用户点击“下载并安装”后才下载和安装。
4. 从微软 HTTPS 固定地址下载 **.NET Desktop Runtime 10.0.14 x64**，当前安装包 60,035,296 字节，约 57.25 MiB；不下载 SDK。采用 Windows WinHTTP 自动代理，不读取客户端账号或代理密码。
5. 核对微软发布元数据中的 SHA-512，再使用 Windows Authenticode 验证信任链和签名证书组织 `Microsoft Corporation`。当前签名证书 CN 是 `.NET`，不能把 CN 错当作组织名。
6. 验证期间保持安装文件只读锁，防止验证后被改写；使用官方安装器 `/install /passive /norestart`，通过正常 Windows UAC 请求管理员确认。
7. 安装成功后重新检测，启动客户端。处理取消、安装错误及需要重启的返回值；不主动重启 Windows。安装开始后不强制终止安装器。

引导器为原生 Windows x64 程序，约 **169 KiB**，没有 CLR 入口或 .NET 导入；只使用 Windows 系统 DLL。现有 `CMUmcl.exe` 和 `updater/` 布局保持不变，正式快捷方式应指向 `CMUmcl.Start.exe`。

## 预览与验收边界

`--preview` 使用模拟进度演示流程；`--preview-failure` 演示失败和重试。预览需要为独立构建配置 `preview.ini`，指向隔离测试程序；正常运行不读取该配置。预览不是运行库安装验证。

当前实现面向 Windows x64、.NET 10。下载使用系统代理，与安装运行库之后客户端内配置的代理是两套作用范围。安装器版本与 SHA-512 固定在 `runtime-release.h`；发布时应检查微软补丁元数据，目标框架变更时同步检测与下载版本。

发布验收应覆盖版本/架构筛选、hostfxr 解析、哈希与签名拒绝、取消、失败重试、重复点击保护及子进程环境。干净 Windows 上的实际安装、UAC、重启返回值与启动流程需要单独验收，不能用模拟预览或组件测试代替。

## 构建与测试

构建使用 Zig 0.13.0；下载后应校验官方 SHA-256。编译器仅用于开发，不进入发行包，不需要在用户电脑安装。

```powershell
./CMUmcl.Bootstrap/build.ps1 -ZigPath <zig.exe> -OutputDirectory <输出目录>

./CMUmcl.Bootstrap/package.ps1 -ZigPath <zig.exe> -DotnetPath <dotnet.exe> -OutputDirectory <全新输出目录>

./CMUmcl.Bootstrap/test.ps1 -ZigPath <zig.exe> -RuntimeRoot <包含DesktopRuntime的dotnet目录> -InstallerPath <微软官方安装包> -RuntimeConfigPath <发布的CMUmcl.runtimeconfig.json> -OutputDirectory <全新测试目录>
```

不显示界面、不安装的诊断入口：

- `--self-test`：版本规则、x64、错误哈希、未签名文件拒绝。
- `--probe`：输出依赖就绪状态，返回 0 表示就绪、3 表示缺少或不兼容。
- `--verify-file <文件>`：验证固定官方安装包，不运行它。
- `--download-check <不存在的目标文件>`：实际下载并验证，不运行安装器。

参考：[微软 Windows 安装说明](https://learn.microsoft.com/en-us/dotnet/core/install/windows)、[.NET 10 发布元数据](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)。

## 文案与按钮行为

窗口标题为 `CMUmcl`，大标题为“准备运行环境”；正文为“启动器需要 Microsoft .NET Desktop Runtime 10 (x64) ，将从微软官方下载并安装。”，不再显示补充说明。正式初始状态为“未检测到兼容运行库。安装时 Windows 可能请求管理员确认。”；仅演示增加不实际安装的标识。

首屏按钮为“手动下载 / 退出 / 下载并安装”。空闲时“退出”直接关闭窗口，不开始下载。下载及检查安装包阶段，“退出”原位置变为“取消”；取消只终止本次准备，显示“已取消安装。安装运行组件后才能使用启动器。”，按钮恢复“退出 / 重试”。进入安装阶段后禁用取消，避免强制中断官方安装器。

状态用语采用“正在下载运行组件…”、“正在检查安装包…”、“请允许 Windows 安装提示，并等待安装完成。”、“安装完成，正在打开启动器…”。下载失败提示检查网络或手动下载；验证失败提示重新下载；需要重启时提示“安装完成，请重启电脑后重新打开启动器。”。正式流程与预览流程均在准备成功后自动打开对应客户端，无需第二次点击。
