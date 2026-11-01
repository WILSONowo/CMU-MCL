# 启动器代理设置

入口为“设置 → 代理”。命名及字段参考 [HMCL DownloadSettingsPage](https://github.com/HMCL-dev/HMCL/blob/main/HMCL/src/main/java/org/jackhuang/hmcl/ui/main/DownloadSettingsPage.java)。

支持使用系统代理、不使用代理、HTTP、SOCKS5，以及主机、端口、身份验证、账户和密码。主机填写域名或 IPv4/IPv6，不含协议、路径或端口；端口范围 1～65535。有效配置自动保存并用于新请求，未填完或非法值保留最后一份有效配置并显示提示。保存失败沿用设置页重试机制。

默认使用系统代理，遵循 .NET 的环境变量/Windows 用户代理解析；“不使用代理”明确设置 `UseProxy=false`，不再探测本机 7890/7891/1080。旧 `CMUMCL_PROXY` 专用变量不再使用；标准 `HTTP_PROXY`、`HTTPS_PROXY`、`ALL_PROXY`、`NO_PROXY` 只在系统代理模式中按 .NET 规则生效。不会修改 Windows 代理设置，也不能绕过系统 VPN/TUN。

## 请求范围与生命周期

登录认证、资源清单、整合包及单文件下载、mrpack 导入、版本列表、Java/Forge/Fabric API 下载、客户端更新、背景图片统一使用 `Network/ProxyRoutingHandler`。CmlLib 的原版版本查询和下载通过 `MinecraftLauncherParameters.CreateDefault(path, httpClient)` 注入同一客户端；第三方内部默认 HttpClient 使用进程级 `IWebProxy` 后备。

代理设置改变后，下一个请求使用新的连接池，包括代理账户/密码变更。旧池等待现有响应或流释放后销毁，不取消已经开始的下载。设置不自动重试已经失败的请求，可重新点击下载/刷新。第三方库内部已有的连接可能继续复用，必要时重试或重启启动器。设置不会为启动后的 Java 游戏进程或其子进程设置网络代理。

代理密码用 DPAPI CurrentUser 加密后保存到 `proxyPasswordProtected`，不放入 URL 或源站 Authorization 头；界面使用 PasswordBox。配置复制到另一 Windows 用户/机器后若密码无法解密，需重新输入；错误时不静默直连。系统代理凭据由系统代理实现提供。

网络层仍执行原来的 HTTPS、重定向、签名、哈希和下载大小校验，没有新增 TLS 证书绕过。

## 验证

设置测试使用临时 HTTP 源站和回环 HTTP/SOCKS5 代理，覆盖切换、凭据隔离、失败处理和下载流生命周期。运行方法见 [测试方法](TESTING.md)。
