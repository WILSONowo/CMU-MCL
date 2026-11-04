# 原生预览适配审查修复与本地对比

后续 **0.2.2** 已补做同视口、同设置的实际交互 FPS 与 GPU 帧时间测试，并加入 24 万方块模型；见 [交互性能对比](SCHEMATIC-PREVIEW-INTERACTION-BENCHMARK-2026-11-04.md)。下文保留首次显示耗时的历史结果。

扩展 0.2.1 / API 2。客户端版本仍为 0.3.48.11；仅本地构建，未发布。

## 三处修复

1. 移除加载循环末尾额外的无条件 present。加载批次仅通过上游 attach_batch 的首批立即呈现、后续约 50ms 节流进行绘制；输入、窗口刷新与完成事件仍立即呈现。
2. 后台加载线程在发送批次之前取得 UploadPermit；后台 drain 完成后才发送 Complete。窗口线程只接收已取得许可的批次，不执行 acquire/drain。保留单个上传信用和上游 64MiB 上传预算、有界通道，不改渲染核心。
3. Dispose 立即取消请求并关闭 Job，进程退出等待、资源释放与目录重试在后台执行。CleanupTask 跟踪最终清理结果；需要清理完成的异步失败路径会等待它。页面切换不再同步等待退出或文件重试。

上游 10 个渲染文件保持原样。扩展版本提升以避免已安装的同版本不同字节被覆盖。

## 测试

- 52 项端到端检查通过；增加关闭调用小于 100ms、锁定临时文件时后台清理继续重试的检查。
- 上游 Rust 测试 30 通过、1 个既有测试跳过。
- 保留原生输入、实际屏幕像素、自动调整大小、焦点、最小化恢复、取消、超时、崩溃、篡改、失败保留旧组件与无新增快照遗留等检查。
- 本轮没有模拟显卡驱动停止完成上传，也没有大建筑的 GPU 帧时间验收。将 acquire/drain 移出窗口线程的结论由调用路径审查确认；不能把小模型功能测试当作大模型性能保证。

## 与官方程序对比

官方 v0.5.2 便携 EXE，与适配版在本机交替运行。使用同一个 Cottage.litematic、同字节资源包；测试读取并核对资源包 SHA-256。硬件为此前本地测试的 RTX 5060，驱动 32.0.16.1088。

每轮独立测试进程，观察模型材质首次出现在桌面上的时间。外窗统一 960×700；保存成功检测时的截图供核验。官方窗口通过 PID、可见状态和标题枚举定位，排除其无标题辅助窗口；早期误采到桌面的试验无效，未纳入下表。

| 轮次 | 官方 v0.5.2 | 适配版 0.2.1 |
| --- | ---: | ---: |
| 1 | 1124.32ms | 1072.90ms |
| 2 | 1001.66ms | 1068.24ms |
| 3 | 993.66ms | 1065.83ms |
| 中位数 | 1001.66ms | 1068.24ms |

适配版中位数多约 66.58ms（6.6%）。这是三次进程新建到首次可见模型的端到端观测，包括启动和适配版输入快照复制；Windows 文件/运行时缓存没有清空。两者内部 UI 和视口面积不同，因此不是同尺寸纯 GPU 渲染基准，也不能证明帧率、重载缓存性能或百万方块性能与上游一致。

模型 SHA-256：`d1f18f462bb3b52fc87c71b78483a15d0a56a42dea9c85bee684b595231f38db`。
材质 SHA-256：`28489e81959ca494934557b2ad5e0fdbbfdf9feaea8bc6266d5f0e915da45c02`。

## 本地产物

- 启动器：`D:\新启动器\outputs\schematic-preview-review-fixes\launcher\CMUmcl.exe`。
- 组件清单：`D:\新启动器\outputs\schematic-preview-review-fixes\component\component.json`。
- 功能证据：`outputs/schematic-preview-review-fixes/evidence-final`。
- 有效比较数据及截图：`outputs/schematic-preview-review-fixes/verified-1`、`verified-2`、`verified-3`。
- 日志：`outputs/schematic-fixes-tests.log`、`outputs/schematic-fixes-rust-tests.log`。
- 比较工具：`tests/SchematicPreview.Tests/Benchmark.cs`，`--benchmark` 参数依次为输出目录、组件目录、模型、材质、官方 EXE。测试会启动并关闭所选官方程序。

未更改客户端 feed、服务器或常规发布工作流，材质仍只用于本机验证，不进入扩展包。
