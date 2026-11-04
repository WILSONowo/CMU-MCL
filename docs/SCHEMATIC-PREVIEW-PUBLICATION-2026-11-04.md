# 预览扩展与客户端发布记录

2026-11-04（Asia/Shanghai）。用户授权先上线扩展通道、公网验证通过后再发布客户端。

## 扩展 0.2.2 / API 2

- 清单：<https://csumcl.mcapcs.cn/components/schematic-preview/latest.json>。
- 组件包：`SchematicPreview-0.2.2-403b1c8e643b267f.zip`，5,831,928 字节，SHA-256 `403b1c8e643b267f84eb5b509eec86eab68ef85b29c133bed16d9fefbf694ac6`。
- 对应源码、上游许可与第三方许可随签名包分发；资源包和测试模型不分发。
- GitHub 镜像：<https://github.com/yuhui258036/CMU-MCL/releases/tag/schematic-preview-0.2.2>，没有将组件 Release 标为客户端最新版本。
- 部署成功：<https://github.com/yuhui258036/CMU-MCL/actions/runs/41806397756>。包先写入，清单随后原子激活，保留旧清单和不可变包。
- 公网清单 HTTPS 200，`Cache-Control: no-cache`。默认客户端固定公钥与默认真实网络传输完成 22 项检查：只检查不安装、取消首次下载、实际签名安装、取消升级保留旧版本、0.2.1 → 0.2.2、真实模型和本地资源包预览、输入焦点、缩放、页面切换和进程释放。
- 本地日志：`D:\新启动器\outputs\schematic-public-channel-proof.log`；证据：`outputs/schematic-online-local-test/public-channel-proof/`。

## 客户端 0.3.48.12

- 源码：`net48` 分支，发行标签 `v0.3.48.12` 指向 `a3983558f667d9308a72f9a0058b6c38dbb57e05`。
- GitHub：<https://github.com/yuhui258036/CMU-MCL/releases/tag/v0.3.48.12>。公开 ZIP `CMUmcl-0.3.48.12-a96d3d2402b52878.zip`，3,476,933 字节；无项目 Key。
- 客户端更新清单：<https://csumcl.mcapcs.cn/update/windows-x86_64/net48/latest.json>。
- 既有服务端发行策略保持：`CMUmcl-0.3.48.12-90f7f2dbcb091995.zip`，3,477,035 字节，SHA-256 `90f7f2dbcb091995b4ad699a092d64ed3c0bc0a44d0e0ede0ab4248bd6cc3966`。该包只在既有服务器通道分发，不上传 GitHub。
- 公网下载后用原有客户端固定公钥复核清单及 ZIP 签名、大小和哈希；确认包内版本、项目 Key 资源存在、预览 worker 未捆绑、独立组件公钥一致，共 6 项通过。日志：`D:\新启动器\outputs\schematic-published-client-proof.log`。
- 首次工作流 <https://github.com/yuhui258036/CMU-MCL/actions/runs/41806620847> 完成公开 ZIP、服务端签名清单和 ZIP，但安装器编译日志混入 stdout 路径，导致安装器上传失败。修复打包脚本输出约定、添加路径断言及缺失产物失败检查；本机通过子进程调用复验仅返回一个实际安装器路径。
- 安装器从上述已发布原始 ZIP 补建，不重新生成或覆盖同版本客户端 ZIP、更新签名和发行标签。补建流程：<https://github.com/yuhui258036/CMU-MCL/actions/runs/41807357210>。
- 本机候选目录 `client-release/` 中的旧 ZIP 和安装器是发布前构建。正式下载以线上 Release/更新清单为准，避免混用哈希。

## SSH 与发布权限

用户从服务器控制台核对了 ED25519 指纹 `SHA256:6lhuvN4cYASWafbpwm/II9f7DzLL+guzLFCQrLn4HJc`。本机旧记录先备份，再以实际公开密钥计算指纹，匹配后替换该主机记录；GitHub 部署凭据中的已知主机也同步为此公钥。未关闭主机校验。

本机没有可用的 root 默认登录密钥；发布采用此前已配置的 GitHub 部署密钥。独立只读登录检查成功：<https://github.com/yuhui258036/CMU-MCL/actions/runs/41805841949>。未打印、导出或新增信任绕过；组件签名私钥仍仅保留在原来的本机受限位置。

后续扩展可独立发布，无需再更新客户端（仍需满足 API 和最低客户端版本）。本轮扩展继续保持用户主动选择安装。
