# 原生预览：相同视口下的交互与 GPU 性能对比

2026-11-04，本地测试。官方 LitematicaPreview v0.5.2 便携 EXE 对比适配扩展 0.2.2 release 构建。没有修改官方 EXE，没有上传或发布。

## 对齐条件

- CPU：Intel i5-12400F（6 核、12 线程）；GPU：RTX 5060，驱动 610.88（Windows 版本 32.0.16.1088）；桌面 2560×1440，系统报告刷新率 119Hz。
- 两者实际 **3D 视口 800×480**，位置均为交换链内 `(0,121)`；**交换链 800×637**，不再只比较外窗大小。官方视口通过 UI Automation 元素边界读取，适配版通过原生 HWND 客户区与上游 set_stage 对齐；不匹配即中止。
- 相同上游渲染源码：Direct3D12、BGRA8 sRGB、Depth24Plus、FIFO、最大帧延迟 2，以及上游自动选择的 MSAA（支持时 4×）。10 个 Rust/WGSL 渲染文件保持上游原样。
- 分块 32、网格线程 4、speed_first 开启、上传信用 4。官方使用每轮全新的 WebView2 用户目录和默认设置；适配版通过可选的基准配置对齐。普通启动器请求仍保持原有默认值。
- 相同模型文件及相同字节的资源包。官方自定义资源包库不存在，使用 Assets/pack.zip；测试先校验它与适配版所选材质 SHA-256 相同。
- 每次确认加载完成后才采样；输入为同一条 120Hz 正弦轨迹的实际鼠标拖动，包含绕转和上下移动；每轮均从适配后的模型视角开始。两者均保留各自实际输入处理链（官方 WebView/rAF，适配版原生消息）。

## 模型与取样

| 模型 | 方块数 | 三角形数 | 说明 |
| --- | ---: | ---: | --- |
| Cottage | 600 | 2,098 | 上游原始样例 |
| Cottage 20×20 | 240,000 | 841,080 | 将相同样例平铺 400 份，357×11×317，400 个区域 |

较大模型由测试脚本生成，使用相同材质与方块种类；它比小样例更重，但不代表复杂模组建筑或高分辨率资源包。

每个模型、每个程序 **3 轮**；每轮约 1 秒交互预热，再采样约 **6 秒**。共 12 段、4,360 个有效呈现帧。统计使用每轮指标的算术平均，不把整个进程生命周期混入帧率。

使用官方 PresentMon 2.6.0，按目标渲染进程 PID 采集；`--v2_metrics --qpc_time_ms`，用输入程序记录的 QPC 起止时间截取有效区间。每段仅允许一个原生交换链，GPU 数据有效率须至少 95%，否则分析失败。实测均为 DXGI / SyncInterval 1 / Composed: Flip；显示帧率统计 DisplayedTime 为正值的帧。本批数据中提交帧数与显示帧数相同。

GPUTime 是一帧从 GPU 开始至完成的时间（含 GPUWait），GPUBusy 是 GPU 引擎的实际工作时间。没有使用 CPU 的 render() 调用耗时或输入事件数代替 GPU 时间。

GPU 指标针对目标进程的原生渲染交换链，不汇总官方 WebView2 辅助进程或桌面合成器的全部 GPU 工作；它不是整台电脑的 GPU 利用率。

## 多轮平均结果

括号内为三轮指标的样本标准差。

| 模型 / 程序 | 平均显示 FPS | 平均 GPUTime ms | 平均 GPUBusy ms | GPUTime P95 ms |
| --- | ---: | ---: | ---: | ---: |
| Cottage / 官方 | 58.03 (0.35) | 0.2537 (0.0096) | 0.2224 (0.0088) | 0.4014 |
| Cottage / 适配 | 63.68 (0.07) | 0.3312 (0.0019) | 0.2709 (0.0003) | 0.5455 |
| 24 万方块 / 官方 | 57.25 (0.12) | 0.4373 (0.0056) | 0.4370 (0.0059) | 0.4956 |
| 24 万方块 / 适配 | 63.72 (0.15) | 0.4334 (0.0088) | 0.4320 (0.0084) | 0.5455 |

P95 列是各轮 P95 的平均；其余数据和每轮统计均保存在 JSON。适配版小模型 GPUTime 多约 0.0775ms；较大模型约少 0.0040ms，在这组重复测量下接近一致。适配版此次实际交互帧率没有下降。

这些是同一输入轨迹下的真实交互结果。由于两者输入调度不同、保留 FIFO 和桌面合成，FPS 不是关闭同步后的渲染器最大吞吐量。GPU 自动频率未锁定，样本仅三轮、每轮六秒，每轮顺序为官方后适配版；不能把结果推广为所有模型与显卡上的绝对性能保证。初次显示时间没有纳入本次指标。

## 文件与复现

根目录：`D:\新启动器\outputs\schematic-interaction-benchmark`。

- 原始逐帧 CSV、每轮 QPC 区间、视口配置：`samples/`。
- 全部每轮统计及平均：`samples/interaction-summary.json`。
- 官方大模型数量核验：`large-metadata/Cottage-20x20-0-settings.json`（UI Automation 原始文本包含 240,000 方块、841,080 三角形）。
- 运行日志：`interaction.log`；PresentMon 每段日志：`samples/*-capture.log`。
- 组件：`component/component.json`；本地启动器：`launcher/CMUmcl.exe`。
- 性能工具：`tests/SchematicPreview.Tests/InteractionBenchmark.cs`；统计：`analyze_interaction.py`；生成大模型：`make_interaction_fixture.py`（nbtlib 2.0.4）。

`--interaction` 参数依次为输出目录、组件目录、小模型、大模型、资源包、官方 EXE、PresentMon EXE。测试会启动专用程序窗口、移动鼠标并采样，运行时不应同时操作电脑。运行完使用 `python tests/SchematicPreview.Tests/analyze_interaction.py <输出目录>` 生成汇总。

小模型 SHA-256：`d1f18f462bb3b52fc87c71b78483a15d0a56a42dea9c85bee684b595231f38db`。
大模型 SHA-256：`b03f4b7bd50ab85fcde7bbea7316f896295877eef31f983f1dc65b6b69340baa`。
材质 SHA-256：`28489e81959ca494934557b2ad5e0fdbbf9feaea8bc6266d5f0e915da45c02`。

适配扩展新增的可选网格线程参数与 stage 指令仅用于本次设置对齐；默认产品请求不发送这些基准设置。未修改上游渲染文件。材质与生成模型只保留在本机测试输出，不进入组件包。客户端 feed 和线上服务未修改。

构建后的回归检查：52 项适配检查通过，上游 Rust 测试 30 项通过、1 项既有测试跳过。日志分别为本地根目录下的 `regression.log`、`rust-tests.log`。
