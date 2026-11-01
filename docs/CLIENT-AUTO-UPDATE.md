# 客户端自动更新

客户端检查签名更新清单，在下载完成并通过校验后请求用户确认；独立 updater 等待客户端退出，预检、备份和替换文件，失败时回滚。默认不强制终止客户端。隔离数据目录参数会在重启时保留。

下载地址必须符合 `UpdateProtocol` 的 HTTPS 域名、端口和路径限制，不允许通过重定向绕过校验。更新前检查可用空间；解压拒绝路径穿越、重复条目及不安全文件类型。旧版本和相同版本不安装。

## 更新清单 v2

固定入口：`https://csumcl.mcapcs.cn/update/windows-x86_64/latest.json`。

```json
{
  "payload": "<下面 release JSON 的原始 UTF-8 字节的 Base64>",
  "signature": {
    "algorithm": "ECDSA-P256-SHA256",
    "format": "ieee-p1363-base64",
    "value": "<对 payload 解码后原始字节的 ECDSA 签名>"
  }
}
```

release JSON：

```json
{
  "formatVersion": 2,
  "platform": "windows-x86_64",
  "version": "0.2.0",
  "url": "https://csumcl.mcapcs.cn/update/windows-x86_64/CMUmcl-0.2.0.zip",
  "size": 123456789,
  "sha256": "<ZIP 的 64 位十六进制 SHA-256>",
  "archiveSignature": {
    "algorithm": "ECDSA-P256-SHA256",
    "format": "ieee-p1363-base64",
    "value": "<ZIP 原始字节的 ECDSA 签名>"
  },
  "notes": "版本说明"
}
```

签名均为 NIST P-256 / SHA-256 / IEEE P1363 的 64 字节 `r || s`，再转 Base64。ZIP 可流式计算 SHA-256 后使用 `ECDsa.SignHash`，等同于对完整 ZIP 使用 `SignData`。payload 不做重新序列化：发布端先生成最终 UTF-8 字节，再签名和 Base64。这样版本号、平台、URL、大小和哈希也受签名保护。


客户端嵌入公开验签密钥。公钥与允许列表属于产品信任边界，不能从待验证的清单动态取代，也不能为了测试关闭签名或 TLS 检查。签名私钥由发布者在仓库外管理。

## 构建与发布

使用 README 中的发布命令。将发布目录的内容放到 ZIP 根目录，保持 `updater/` 目录结构，并确保程序集版本与签名清单版本一致。

1. 在隔离测试目录验证构建、更新和失败回滚。
2. 由受控发布流程签署 ZIP 和清单 payload；不要把私钥或签名操作日志放进源码。
3. 先上传不可变的版本 ZIP，再更新 `latest.json`。版本包可长期缓存，更新入口必须及时重验证。
4. 验证发布包的签名、哈希、大小、版本与恢复流程。真实部署的访问方式和发布回执由维护者私下保存。

## 验证与恢复

见 [测试方法](TESTING.md)。测试使用临时密钥和隔离数据，不改变正式域名、公钥或真实用户实例。测试结果不能替代实际平台兼容性验收。

更新器保留被覆盖文件的备份及事务结果；恢复时使用同一安装实例对应的备份，保留用户数据和额外文件。不要将另一个环境的安装状态或私人目录复制到公开问题报告中。
