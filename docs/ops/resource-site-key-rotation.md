# 资源签名与凭据轮换

此文档说明维护原则，不记录某个部署的服务器地址、用户名、管理端口、密钥位置或访问方式。

管理员登录凭据与资源签名密钥有不同用途，应分别管理。管理员凭据变化后，应验证旧会话的失效行为；签名密钥变化还必须与客户端信任的公钥协调，避免让已有客户端无法验证资源。

## 准备

- 在私有运维系统确认目标环境、授权范围、备份和恢复方式。
- 将私钥、管理员密码及哈希保存在专用秘密存储或仓库外的受限文件中。
- 先在隔离环境验证新签名、客户端兼容性和回退流程。

## 公开验签

验签只需要公开密钥、清单和签名文件。可使用本目录的 `verify-manifest-signature.ps1`，显式传入本地文件和 OpenSSL 路径：

```powershell
./docs/ops/verify-manifest-signature.ps1 -PublicKey ./public.pem -Manifest ./manifest.json -Signature ./manifest.json.sig -OpenSsl (Get-Command openssl).Source
```

该工具将 ECDSA P-256 的 IEEE P1363 签名转换为 OpenSSL 使用的 DER 格式；签名验证失败时不应继续发布。公开公钥不是私钥，可以随客户端发布。实际轮换和部署命令应保留在私有运维手册中。
