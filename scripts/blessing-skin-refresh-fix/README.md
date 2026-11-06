# Blessing Skin 刷新令牌身份不一致修复

适用问题：同一账号 authenticate 获得的令牌有效，refresh 返回 200 后的新令牌 validate/进服失败；账号邮箱含大写字母。

## 根因与证据

登录路径与 Token::isValid 使用原始邮箱生成 UUID v5，刷新路径却先 strtolower。邮箱含大写字母时，刷新生成不同的 JWT sub，随后被服务器自己的 RelatedTo 校验拒绝。

2026-11-06，独立 HTTP 客户端对真实站点复现：旧令牌 204 → refresh 200 → 新令牌 403。clientToken、角色和 issuer 不变，sub 改变；省略 clientToken 与延迟复验仍为 403。账号持有人确认邮箱含大写字母。未记录邮箱、令牌或密码。

上游问题：https://github.com/bs-community/blessing-skin-plugins/issues/212
参考控制器最后变更：ed9eefcee67473e2a3e0b6f3a0e5b0a155a1d32b。

## 最小修复

`preserve-refresh-subject.patch` 只把 refresh 中生成 userUuid 的表达式从 `strtolower($user->email)` 改成 `$user->email`。不修改 authenticate 或 Token::isValid，保持现有账号身份与现有有效令牌兼容。不应通过跳过 validate、修改 JWT 内容或把所有身份生成路径一并转小写来绕过。

本补丁针对核对过的上游布局。第三方站点是定制版本，管理员应先检查部署源码是否含相同表达式；路径或上下文不同则手工修改对应的一行。

## 管理员部署

1. 备份站点 `plugins/yggdrasil-api/src/Controllers/AuthController.php`。
2. 在含 `plugins` 目录的站点根目录执行 `git apply --check /path/preserve-refresh-subject.patch`，通过后执行 `git apply /path/preserve-refresh-subject.patch`；非 Git 部署也可核对后手工修改那一行。
3. 用站点实际 PHP 执行 `php -l plugins/yggdrasil-api/src/Controllers/AuthController.php`。
4. 按站点部署方式重新加载 PHP 进程/OPcache，使修改生效。无需修改数据库邮箱或清空所有认证缓存。
5. 用测试账号验证 authenticate → validate 204 → refresh 200 → 新令牌 validate 204 → 进入游戏服务器；同时覆盖全小写与混合大小写邮箱，重复刷新。不要把令牌输出到日志。
6. 如需回滚，恢复备份控制器并重新加载 PHP。

已签发的错误 sub 不能由客户端重写；补丁生效后，可尝试用仍在可刷新期限内的令牌再刷新，否则重新登录。客户端应继续复验，禁止把无效令牌交给游戏。

## 本地回归方法

`composer install --no-dev --no-scripts --no-plugins`，然后执行：

```text
php regression.php vendor/autoload.php /path/AuthController.original.php /path/AuthController.patched.php /path/Token.php
```

测试读取两个真实控制器中的身份生成表达式，通过实际 Ramsey UUID 与 Lcobucci JWT 库生成测试令牌，并调用未修改的上游 Token::isValid；仅使用虚构邮箱和测试密钥。覆盖全小写、邮箱用户名大写、域名大写、全大写及重复刷新，32 项通过。它验证服务端身份与令牌校验核心，不等价于已部署第三方站点或实际 Minecraft 进服验收。

本机验证使用 PHP 8.4.27-1、ramsey/uuid 4.9.4、lcobucci/jwt 5.6.1，composer.lock 锁定依赖。PHP 与 Composer 从官方来源下载并核对 SHA-256。管理员应结合站点现有 PHP/依赖版本再运行。

## 当前状态

补丁已在本地应用并测试，尚未部署到第三方皮肤站。CMUmcl 0.3.48.15 已能正确拦截无效刷新结果；本问题不需要客户端放松认证检查。
