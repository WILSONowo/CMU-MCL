# 0.3.48.13 发布与扩展在线安装验收

2026-11-04（Asia/Shanghai）。修复 `%LOCALAPPDATA%/CMUmcl` 已迁移到其他磁盘时，预览扩展安装按钮变灰的问题。

- 发行源码：`46610f5c042ac5e3e16e61e40cd40ff8182b24b7`，标签 `v0.3.48.13`。
- GitHub Actions：<https://github.com/yuhui258036/CMU-MCL/actions/runs/41815303506>，公开发行与服务器发行两个任务均成功，全部安装包上传步骤通过。
- 发行页：<https://github.com/yuhui258036/CMU-MCL/releases/tag/v0.3.48.13>。
- 客户端签名清单：<https://csumcl.mcapcs.cn/update/windows-x86_64/net48/latest.json>，版本 0.3.48.13。
- 服务器安装包：<https://csumcl.mcapcs.cn/downloads/CMUmcl-0.3.48.13-setup-full.exe>。

发布前使用 0.3.48.13 构建通过 24 项迁移目录测试和 98 项在线通道回归。发布后重新下载 GitHub 正式 ZIP 和安装包，复核发布元数据中的大小与 SHA-256；公开 ZIP 4 项验收通过。服务器签名更新清单及 ZIP 用原有客户端固定公钥验证，6 项验收通过。

在线安装验收加载下载到的正式公开 ZIP 内的 CMUmcl.exe，记录其路径、版本和哈希，并与 ZIP 内入口字节比对。默认真实网络、默认组件公钥、默认组件安装目录，均未使用测试替代值。真实扩展管理页面成功读取在线清单，原先灰色的安装按钮启用；执行安装按钮事件后，下载、验签和原子激活成功。

实际启用 `0.2.2 / online-signed`，位于 `D:/新启动器/CMUmclData/components/schematic-preview`，入口 SHA-256 为 `fa7f82658bb87d304c1b15723e63e14d71baa4e988a476bc617a980c49ca6a2d`，保留签名回执。随后通过真实模型加载、原生 GPU 画面、焦点、缩放、主题切换、页面切换释放进程等检查，共 **19 项正式包实际在线安装验收通过**。

安装扩展是本轮用户要求的发布后验收操作。客户端仍保持可选安装行为；后续扩展继续使用 0.2.2 独立通道，本轮未重签或重发扩展。用户原有实例索引哈希不变；正在运行的 `D:/CMUmcl/CMUmcl.exe` 未被替换或关闭，用户可通过客户端“检查更新”升到 0.3.48.13，新版将识别本轮已安装的扩展。

最终正式包集中在 `outputs/releases/client-0.3.48.13/`；签名清单、哈希、发布日志、安装日志、GPU 截图和验收回执位于 `outputs/release-evidence/2026-11-04/release-0.3.48.13/`。临时测试程序与编译输出集中到工作区 `.tmp`，不保留为新的 outputs 测试安装目录。
