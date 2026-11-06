# PCL、HMCL 与 CMUmcl 登录恢复流程对照（2026-11-06）

用户反馈在 PCL/HMCL 中，新登录与旧会话自动恢复均可使用。该反馈不能直接由上游源码否定，因此本轮补充本机二进制及真实协议对照。

## 本轮新增证据

- 找到本机 HMCL-3.16.4.exe，读取其内嵌 manifest 确认 Implementation-Version=3.16.4。
- 检查这份实际二进制的 YggdrasilAccount.logIn：已认证且角色名存在时可直接复用当前会话；其他情况下 validate，失败再 refresh。刷新响应只检查角色及角色名，不再 validate 新令牌。其 logInWithPassword 单独通过 authenticate 取得会话。
- 直接加载这份实际二进制中的 AuthlibInjectorServer 和 YggdrasilService，调用真实皮肤站 refresh，然后由同一 HMCL 认证代码 validate 新令牌。结果：refresh 正常返回，但 validate=false，无客户端解析异常。仅使用本轮诊断中已失效但仍可刷新的测试会话，不读取或修改 HMCL 的加密账号凭据。
- 按 PCL 当前上游 McLoginRequestRefresh 的格式，发送 selectedProfile、accessToken、requestUser，不带 clientToken。真实站点返回 400 / IllegalArgumentException（角色不匹配）。PCL 的 McLoginServerStart 在刷新请求抛错后会转普通账号密码登录。该项是请求格式复现，本机未找到用于测试的 PCL 程序，不能记为完整 PCL 程序实测。
- 本机 HMCL 既有日志未记录足够的接口调用分支细节，不能判断用户此前成功的那次具体经历了哪条认证路径。

源码参考：

- https://github.com/HMCL-dev/HMCL/blob/main/HMCLCore/src/main/java/org/jackhuang/hmcl/auth/yggdrasil/YggdrasilService.java
- https://github.com/HMCL-dev/HMCL/blob/main/HMCLCore/src/main/java/org/jackhuang/hmcl/auth/yggdrasil/YggdrasilAccount.java
- https://github.com/Meloong-Git/PCL/blob/main/Plain%20Craft%20Launcher%202/Modules/Minecraft/ModLaunch.vb

## 修正后的结论

真实刷新结果被服务器拒绝这一事实已由独立 HTTP 和本机 HMCL 的真实认证代码交叉复现，不能归因为 CMUmcl 对令牌的解析。但此前将用户遇到的整条登录体验都归因于服务端不够完整：CMUmcl 缺少会话恢复入口，在需要凭据时只是报错退出，确实存在客户端处理缺口。

客户端本地修复：需要重新登录的认证错误传递明确标记；启动失败后自动打开已有登录窗口，登录成功继续原实例，取消则不启动。重试最多一次，并发启动共享登录窗口；网络/服务器暂时故障不触发反复登录。另修复较早启动结果晚返回时覆盖新会话的问题，以及错误响应为 JSON null 时的异常解析问题。不自动收集或保存密码。

本地构建通过：56 项基础/认证回归，以及 UI 测试中的 9 项新增登录恢复检查通过。覆盖成功恢复、取消、网络失败、重试上限、并发共享、旧会话晚返回和取消后不重试。新增恢复流程尚未发布、未在用户真实登录窗口进行端到端验收；当前正式客户端仍为 0.3.48.15。

服务器补丁部署仍独立待管理员处理。客户端恢复流程不意味着服务器签发的新令牌已经有效，也不能把缓存复用或 authenticate 成功算作 refresh 成功。

后续更新：登录恢复已随 0.3.48.16 发布，并完成真实会话吊销、重新登录、自动续启及进服验收，详见 [发布验收记录](AUTH-LOGIN-RECOVERY-RELEASE-2026-11-06.md)。
