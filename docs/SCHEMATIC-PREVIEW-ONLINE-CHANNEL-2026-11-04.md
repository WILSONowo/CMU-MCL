# 可选预览扩展在线通道：本地完成与客户端更新候选

> 后续发布已获授权并执行：扩展 0.2.2 及客户端 0.3.48.12 更新通道已上线，公网扩展 22 项与客户端包 6 项校验通过。以下为发布前的本地准备记录；线上状态和最终产物以 `SCHEMATIC-PREVIEW-PUBLICATION-2026-11-04.md` 为准。

客户端更新候选 **0.3.48.12**，扩展 **0.2.2 / API 2**。在线下载、独立签名、校验、安装和独立版本检查已接入客户端；本地测试通过，客户端 ZIP 和安装包以及签名组件通道均已准备。没有提交、推送、上传、修改服务器或更新现有 net48 feed。

## 用户行为

- 原理图页提供“预览扩展”入口；快速预览尚未安装时进入同一管理页。
- 管理页显示当前版本、在线版本、下载大小、说明与来源。检查版本只读取签名清单，不下载工作进程。
- 点击安装或更新后再次确认才下载；可取消。页面切换会取消未完成的操作。
- 安装在用户级 components/schematic-preview/versions，保留旧版本；下载、签名、解压或激活失败不切换 current.json。成功完成后才原子激活。
- 后续扩展更新只在这个页面检查和提示，不触发全局客户端新版本弹窗，不需要重新发布客户端。若未来 API 改变，则需另行安排客户端适配。
- 保留用户明确选择可信构建的本地安装入口；在线安装使用固定公钥认证，不能以本地安装的哈希检查替代签名。

## 协议与边界

独立清单：`https://csumcl.mcapcs.cn/components/schematic-preview/latest.json`。
ZIP 仅允许同一 HTTPS 主机、默认端口及 `/components/schematic-preview/<文件名>.zip`，拒绝凭据、查询参数、片段、编码路径和重定向。没有回退到客户端或整合包通道。

使用独立 ECDSA P-256/SHA-256 密钥，P1363 Base64 签名。先验证原始 payload 字节签名，再验证 component、API、schema、版本、最小客户端版本、大小、ZIP 哈希和入口哈希；ZIP 本身另有签名。重复 JSON 字段也会拒绝。

下载清单上限 128KiB、ZIP 上限 128MiB；ZIP 最多 2048 项、单项最多 128MiB、总展开最多 256MiB。解压前扫描路径并限制实际读取字节，拒绝目录越界、链接、Windows 重复名、设备名、ADS、异常属性和不允许的文件。包只包含工作进程、清单、许可、来源说明、许可证目录及 Source.zip。

在线安装保留签名清单回执；每次启动前复核回执与入口哈希。检查更新、安装与预览均不接收 CurseForge Key 或登录数据。安装互斥锁防止多个进程或入口同时提交；版本目录不覆盖不同字节的同版本工作进程。

## 签名密钥

客户端固定公钥 SPKI SHA-256：
`370FF49DD40B367BE0A745F3AAF11F237FF11586791E3E4E9420476334C1F20E`。

本轮生成了独立组件签名私钥，保存在本机
`%LOCALAPPDATA%\CMUmcl-build-secrets\schematic-preview-signing-key.pem`，文件 ACL 限制为当前用户。私钥不在仓库、构建输出、组件包或客户端包内。后续发布必须安全保留并使用同一把密钥；客户端只包含公钥。没有读取或复用现有客户端更新私钥。

如接入独立 CI，使用 `SCHEMATIC_PREVIEW_SIGNING_KEY_PEM`，不要误用 `UPDATE_SIGNING_KEY_PEM`。不要在输出、日志或 GitHub Release 中放私钥。

## 本地验证

- **98 项在线通道检查通过**：真实 ECDSA 签名与 ZIP，固定地址上的本地 HTTP handler 模拟服务器。覆盖清单/包篡改、错误签名、错误 component/API/schema、最小客户端版本、URL 白名单、重定向、404、截断、入口哈希与版本不符、ZIP 越界/设备名/重复名/链接/大小及数量限制、流式取消、并发安装、激活文件被占用、失败保留旧版和成功升级。
- **15 项正式公钥准备包检查通过**：实际签名包由客户端固定公钥校验并安装，复核源码归档布局，随后在签名回执保护下启动真实 worker 并显示预览；没有放宽默认公钥或地址策略。
- **52 项原生预览回归通过**；现有实例内容页面 **94 项检查通过**，更新了旧“预览尚不可用”占位断言。
- **4 项客户端 ZIP 检查通过**：版本为 0.3.48.12、无嵌入 CurseForge Key、无可选 worker、内嵌公钥与验签包一致。ZIP 使用现有客户端更新协议的安全解压器验证。
- Inno Setup 安装包编译成功；本轮未执行安装器以改变当前用户的安装记录和快捷方式。客户端常规发布流程及更新验签密钥未修改。

本地下载测试使用真实签名和文件，但网络响应由测试 transport 提供；公网 TLS、CDN 缓存与部署后的真实下载需上线后复验。本轮对公网清单只做只读探测，返回 **404**，所以现在尚不能在公网下载。

## 已准备的文件

根目录：`D:\新启动器\outputs\schematic-online-local-test`。

| 内容 | 路径 |
| --- | --- |
| 可运行客户端 | `launcher/CMUmcl.exe` |
| 客户端 ZIP | `client-release/CMUmcl-0.3.48.12-1e451f754c7d6ca6.zip` |
| 客户端安装包 | `client-release/CMUmcl-0.3.48.12-setup.exe` |
| 客户端包哈希/大小 | `client-release/release-info.json` |
| 独立组件 ZIP | `release-component/SchematicPreview-0.2.2-403b1c8e643b267f.zip` |
| 已签名组件清单 | `release-component/latest.json` |
| 组件包哈希/大小/入口哈希 | `release-component/release-info.json` |
| 在线安全与升级证据 | `final-online-evidence/` |
| 正式公钥安装与真实预览证据 | `final-pin-proof/` |
| 原生回归 | `native-regression/` |
| 实际客户端页面证据 | `final-existing-ui-evidence/preview-extension.png` |
| 公网只读探测 | `channel-public-probe.json` |

日志位于 `D:\新启动器\outputs`：`schematic-online-tests.log`、`schematic-production-pin-proof.log`、`schematic-native-online-regression.log`、`schematic-existing-ui-tests.log`、`schematic-client-package-proof.log`。

## 发布准备与顺序

1. 先部署版本化组件 ZIP，再原子发布对应的已签名 latest.json，仅写 `/components/schematic-preview/`。latest.json 应返回 no-cache，包文件名不可覆盖为不同字节。保留旧清单和旧包。
2. 从公网下载并用当前客户端固定公钥复验签名、哈希、大小和缓存头；通过未安装、升级、取消场景。这个步骤尚未执行，因为本次没有部署授权。
3. 再通过既有客户端流程发布 0.3.48.12。扩展包不能放进客户端 ZIP/安装包或 net48 feed。客户端更新签名仍使用原来的客户端更新密钥，不能用本轮组件私钥签客户端。

组件打包与签名脚本：`scripts/package-schematic-preview.ps1`；组件二进制构建仍为 `scripts/build-schematic-preview.ps1`。
本地客户端候选准备脚本：`scripts/prepare-preview-client-update.ps1`，保留暂存目录供检查，不写线上 feed。

组件 ZIP 包含 worker 对应 Rust 源码、锁文件、构建脚本及上游许可证文本，不包含 Minecraft 资源包或用户原理图。公共分发仍应复核最终许可清单和源码可获取方式，沿用已有 AGPL/资源分发约束。
