# .NET48 分支

本地 r18 为功能基线，面向 Windows x64 / .NET Framework 4.8。原 main 和本地私有测试程序不在此源码整理中修改。

保留 PCL clientVersion / HMCL patches 导入、主窗口内置实例设置、版本维护与修复、设置共享、CurseForge/Modrinth 直连、模组图标、资源包封面、schematics 折叠搜索、加载器自动图标及原有登录和更新器。

构建 LauncherSettings.Tests 后，调用测试 EXE，传入源码绝对路径、全新临时产物目录，再选择一个专项参数：

| 参数 | 内容 |
| --- | --- |
| `--name-icon` | 加载器图标、手动覆盖与列表刷新 |
| `--folder-import` | 原版、PCL、HMCL 导入与取消 |
| `--instance-content` | 管理页、封面与原理图 |
| `--local-mod-icons` | 模组图标匹配与默认回退（模拟网络） |
| `--instance-settings` | Java、内存、窗口与参数 |
| `--install-sync` | 维护、修复与同步 |
| `--feedback-r15` | 主窗口内置交互 |
| `--resource-redesign` | 社区导航与详情布局 |
| `--dpi-matrix` | 明暗主题与布局矩阵 |

不带专项参数执行完整界面回归。公开测试不含个人桌面路径或本机实例验收脚本，也不携带真实 Key 或认证。大型项目图标样本改为程序生成的 GIF/WebP 回归样本，产品解码能力保持。

此公开源码工作区已完成构建：完整界面 160、内置交互 59、内容管理 85、文件夹导入 44、名称图标 58、社区 271、底层 20、资源下载 39 项通过。各专项含公共检查，不应直接相加。普通构建已核对不包含 CurseForge 私有 Key 资源；日志与产物保存在仓库外，不提交。

不要提交 bin、obj、sandbox-data、test-data 或含私有资源的 EXE。本次只上传源码分支，不发布二进制、更新 feed，也不改服务器和生产代理开关。
