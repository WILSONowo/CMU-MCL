# CMUmcl

面向 Minecraft 社区的 Windows 启动器，提供账号认证、签名资源清单、整合包安装、Java 运行时选择和独立客户端更新。

## 构建

安装 .NET 10 SDK，在仓库根目录执行：

```powershell
dotnet build ./CMUmcl/CMUmcl.csproj -c Debug
dotnet publish ./CMUmcl/CMUmcl.csproj -c Release -r win-x64 --self-contained true -o ./dist/client
```

发布目录包含独立 updater。请保留 LICENSE、NOTICE 和第三方许可，按各自要求分发源代码及相关材料。

## 代码与文档

- `CMUmcl/Auth`：认证与本地凭据存储。
- `CMUmcl/Content`：签名清单、资源下载与整合包安装。
- `CMUmcl/Launch`：游戏启动、Java 与加载器处理。
- `CMUmcl/Update` 和 `CMUmcl.Updater`：更新协议、验签与替换事务。
- [更新协议与发布流程](docs/CLIENT-AUTO-UPDATE.md)
- [测试方法](docs/TESTING.md)
- [安全说明](docs/security.md)
- [资源协议](docs/server-contract.md)
- [公开内容约定](docs/PUBLIC-CONTENT.md)

正式认证与更新入口、下载允许列表和验签公钥属于产品配置。修改这些配置需要相应的兼容性和安全验证。生产运维资料不随源码发布。
