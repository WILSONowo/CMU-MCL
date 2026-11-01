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

## 设置、背景与代理

安装 .NET 10 SDK 和 Windows Desktop 运行时，在仓库根目录执行：

```powershell
$source = (Get-Location).Path
$output = Join-Path $env:TEMP ("cmumcl-tests-" + [guid]::NewGuid())
dotnet run --project ./tests/LauncherSettings.Tests -- $source $output
dotnet run --project ./tests/ResourceDownload.Tests
```

默认设置测试使用合成身份、临时图片和回环代理，不访问指定的真实图片服务。可选网络背景验收需要同时传入 `--live-background`，并通过 `CMUMCL_TEST_BACKGROUND_URL` 指定由测试者控制的 HTTP(S) 图片地址；未配置时应报错，不回退到维护者的服务。不要在测试参数中放凭据或签名 URL。

构建检查只能证明代码编译；正常 UI 测试和可选网络验收应分别记录结果。
