# 原理图预览：原生 GPU 本地适配结果

后续审查的三处调度/回收问题已在 **0.2.1** 修复，当前本地产物及有效性能比较见 [审查修复记录](SCHEMATIC-PREVIEW-REVIEW-FIXES-2026-11-04.md)。下文保留 0.2.0 初始验收记录。

当前扩展版本 **0.2.0 / API 2**，替代 0.1.0 的 PNG 传图方案。客户端仍为 0.3.48.11。仅完成本地开发与验证，没有提交、推送、上传或上线。

## 实现与复用范围

- 固定上游 LitematicaPreview v0.5.2，commit `bdd61b6d5a048061c430a999032d9e2a3df01ded`；共享核心固定 `87838a6dcca60010f2af63dd6c733a5010ec6a27`，依赖由 Cargo.lock 固定。
- `components/schematic-preview/src/render` 中除模块入口 `mod.rs` 外，其余 10 个 Rust/WGSL 文件均与上游对应文件逐字节相同，包括 `context.rs`。已移除前一版延迟上传补丁。
- 直接调用上游 `NativeRenderer::new_from_hwnd`，复用相机、材质分层、着色器、原生 Direct3D12 交换链、渐进批次上传、GPU 上传限流及完成等待。不重写解析、网格化或渲染器。
- WPF `SchematicPreviewHost` 创建专用承载区；worker 从创建时就是这个区域的子窗口，具有独立消息循环和渲染器。没有启动并重设上游完整桌面窗口的父窗口，也不包含 Tauri/WebView2 界面。
- 鼠标拖动、Shift 平移、滚轮、方向键、Home/R 和 +/- 直接由 worker 原生窗口处理；Tab 可返回启动器控件，WPF 键盘导航也可进入视口。原生消息队列合并连续移动事件，按需呈现，空闲不轮询绘制。
- 解析和网格准备在后台线程运行；只在同一 worker 内通过有界通道传递批次，原生窗口线程负责上传和呈现。跨进程 JSON 只包含命令、进度和状态，不传几何或像素。
- 生产路径已删除 PNG 编码、临时图片、WPF BitmapImage 和图像读回调用；保留的上游 offscreen/frame 模块仅供上游模块编译及测试，原生会话不会创建离屏目标。验证截图由测试程序从桌面采集，不是渲染传输协议。
- 页面尺寸改变时，读取真实 HWND 客户区物理尺寸重建交换链；worker 线程匹配承载窗口 DPI awareness context，使用实际窗口 DPI 映射输入和相机。原生区域初始化前保持深色底，并在首帧清屏后显示。
- 保留原有组件目录、入口哈希校验、用户确认的本地安装、原子激活、独立进程、2 GiB Job 内存上限、退出回收及输入快照。API 1 扩展会被明确拒绝，提示安装 API 2，避免旧协议混用。

## 本地验证

本次原生版本端到端 **50 项通过**，上游渲染相关 Rust 测试 **30 项通过、1 项原有硬件测试跳过**。端到端测试实际使用 Windows 桌面与 Direct3D12，不能将跳过项算作通过。

- 验证原生视口真实属于启动器窗口，HWND 由隔离 worker 拥有，无独立顶层预览窗口。
- Cottage.litematic 解析为 600 方块、2098 三角形；桌面截图包含真实材质，旋转与缩放后像素变化。
- 连续拖动测试记录 180 次原生输入更新、180 次适配层呈现调用。此为本机小样例的功能验证，不是大建筑帧率承诺。
- 原生拖动、滚轮、方向键、Tab 进出、点击聚焦、实际 DPI 上下文匹配验证通过。
- 多次物理尺寸变化、生产页面自动调整交换链、最小化暂停、恢复重绘、独立弹层遮挡、深浅主题验证通过。
- 未安装、错误类型、失败安装保留激活、入口篡改、坏文件、64 MiB 限制、真实 67,108,864 方块样例限制、加载取消、超时、worker 崩溃、切页后回收、加载中切页、快照清理、源文件不变验证通过。
- 修复取消路径的竞争及进程退出后短暂文件占用导致的清理失败。最终运行无新增遗留快照目录。

测试机器可见 NVIDIA GeForce RTX 5060，驱动 32.0.16.1088。测试程序使用与启动器相同的 PerMonitorV2 清单。没有修改显示器设置；跨实体显示器的 100%–200% DPI 迁移、真实设备丢失和长期压力运行仍未验收。

## 试用与复现

1. 保留完整目录，打开 `D:\新启动器\outputs\schematic-preview-native-test\launcher\CMUmcl.exe`。
2. 实例内容 → 原理图 → 安装本地预览扩展，选择 `D:\新启动器\outputs\schematic-preview-native-test\component\component.json`。
3. 点击 .litematic 的快速预览，选择本地完整 Java 资源包 ZIP。

扩展 EXE 约 15.8 MiB，不附带 Minecraft 材质。旧离屏版本的产物保留用于对照，不要把旧 launcher 或旧清单与当前原生版本混用。

构建：`scripts/build-schematic-preview.ps1`，需要 Rust MSVC、Visual Studio C++ x64 和 Windows SDK。
测试：`tests/SchematicPreview.Tests`；输出参数依次为证据目录、组件目录、Cottage.litematic、完整资源包 ZIP、可选的 Concrete.litematic 大样例。

证据：`D:\新启动器\outputs\schematic-preview-native-test\evidence-final`。
日志：`D:\新启动器\outputs\schematic-native-tests.log`、`schematic-native-rust-tests.log`、`schematic-native-release-build.log`。

## 尚未发布的范围与限制

维持此前解析、输入、解压、体积、网格和资源包上限；没有宣称任意大小的上游文件都能打开。PNG 带来的固定画面尺寸和传图延迟已移除。

未改动 net48 feed、客户端版本号、常规发布工作流、服务器或 r18 私有测试目录。在线下载、独立签名 feed、组件自动更新、公共分发的对应源码/依赖许可证包仍待后续处理。AGPL LICENSE、NOTICE 与来源说明保留，测试材质不进入扩展包。
