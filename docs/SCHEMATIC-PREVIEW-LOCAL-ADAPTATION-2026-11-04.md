# LitematicaPreview 本地适配结果

> 历史记录：本文描述 0.1.0 离屏阶段。当前已改为 **0.2.0 / API 2 原生 GPU 实时呈现**，详见 [原生 GPU 适配结果](SCHEMATIC-PREVIEW-NATIVE-GPU-2026-11-04.md)。不要将本文的旧试用路径当作当前版本。

本地开发版本：扩展 0.1.0 / API 1。客户端版本号保持 0.3.48.11，未提交、推送或上线。

## 实现

- 固定上游 LitematicaPreview v0.5.2，commit `bdd61b6d5a048061c430a999032d9e2a3df01ded`。
- 直接复用上游 `src-tauri/src/render` 的相机、场景、着色器、GPU 上传与离屏读回模块。仅为 `context.rs` 增加延迟呈现的批次上传方法，避免读取到渐进加载中的帧；没有重写解析器或渲染器。
- 解析、网格化使用上游同一个 `litematica-preview-core`，固定 commit `87838a6dcca60010f2af63dd6c733a5010ec6a27`。依赖由 Cargo.lock 固定。
- 上游完整 Tauri 程序没有现成 WPF 控件接口，因此复用其原生渲染程序代码构建独立 worker，省去外部窗口和 WebView2 界面。WPF 通过 JSON 行协议控制相机，以 PNG 显示结果。
- 原理图页接入本地扩展安装、完整资源包选择、带材质预览、拖动旋转、Shift 拖动平移、滚轮缩放、按钮控制和重置视角。支持更换资源包、返回、切页取消、关闭回收；进程异常会显示失败状态。
- 扩展使用用户级 components/schematic-preview/versions 目录；本地清单验证类型、API、版本及入口 SHA-256，暂存成功后原子切换 current.json。校验失败保留激活版本，同版本不同字节不覆盖。
- 本地安装需要用户选取可信构建，尚无在线签名安装通道。SHA-256 提供本地完整性校验，不代表发布者认证。
- 输入复制到独立临时目录，原文件不改写；拒绝链接路径。子进程清除继承环境中的业务数据，仅保留系统运行变量，Windows Job 限制提交内存 2 GiB，启动器退出时回收进程。

## 当前限制

输入 64 MiB，解压 256 MiB，单边 4096，区域体积 16,777,216，区域 1024，网格/纹理累计 512 MiB；资源包压缩大小 256 MiB。初次加载 100 秒超时，单次交互 25 秒超时。当前画面固定 1200×800，按页面尺寸等比显示；PNG 编码传输的交互性能不等同于上游直接交换链呈现。

不包含 Minecraft 材质；必须选择本地完整 Java 资源包。测试材质从此前已下载的官方便携包中取出，仅供本机测试，未复制进扩展包。保留 AGPL LICENSE/NOTICE；公共分发仍需完成源码、依赖许可打包与审查。

## 本地验证

- Rust 上游渲染相关测试：30 通过，1 个原有硬件测试 ignored（不能将其计为通过）。
- .NET 端到端测试：23 项通过，真实运行 Windows Direct3D12 离屏渲染。
- Cottage.litematic：600 方块、2098 三角形；验证旋转/缩放确实改变像素，中文空格路径正常。
- 覆盖缺失组件、错误组件、失败安装保留旧激活、入口篡改、坏文件、64 MiB 文件限制、取消、超时、崩溃、切页终止、临时文件清理及源文件哈希不变。
- 上游 Concrete.litematic（67,108,864 方块）在当前限制下被拒绝。没有宣称百万方块预览性能已验收。
- 使用实际按钮模板生成深浅主题和 100%/150%/200% WPF 导出截图；不等同于跨显示器真实 DPI/鼠标拖拽人工验收。

测试与截图：`D:\新启动器\outputs\schematic-preview-local-test\evidence-final`。
测试日志：`D:\新启动器\outputs\schematic-preview-tests.log`、`schematic-rust-tests.log`。

## 本地试用

1. 启动 `D:\新启动器\outputs\schematic-preview-local-test\launcher\CMUmcl.exe`（整个目录保留）。
2. 在实例内容 → 原理图 → 安装本地预览扩展，选择 `D:\新启动器\outputs\schematic-preview-local-test\release-component\component.json`。
3. 点击 .litematic 的“快速预览”，选择本地完整 Java 资源包 ZIP。

构建脚本：`scripts/build-schematic-preview.ps1`（需要 Rust stable MSVC、Visual Studio C++ x64、Windows SDK）。
适配测试：`tests/SchematicPreview.Tests`。测试只使用隔离的组件根目录，没有改动用户已安装扩展。

未实现或执行：在线下载/独立签名 feed、服务器部署、自动更新、公共发布、依赖许可及对应源码的最终分发包。客户端常规发布工作流、net48 feed、版本号及 r18 私有测试目录未改动。
