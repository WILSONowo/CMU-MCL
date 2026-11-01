# 测试方法

在独立工作目录运行测试，构建输出、截图和原始日志保存在忽略目录或仓库外。不要使用真实账号、实例数据或生产签名私钥。

## 更新协议

安装 .NET 9 SDK/运行时后执行：

```powershell
dotnet run --project ./tests/LauncherUpdate.Tests
```

覆盖签名、哈希、版本、URL 限制、压缩包安全检查以及更新回滚。测试使用临时生成的密钥。

## UI 更新验收

`tests/LauncherUpdate.UiAcceptance` 是独立的隔离验收程序，使用测试传输和测试签名材料。运行前检查该程序的参数及输出位置；不要把安装确认、窗口关闭或文件锁测试指向日常使用的客户端。
