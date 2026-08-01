# 选型结论（先给答案）

**推荐：C#（.NET 10 LTS）+ WPF + WebView2 + Markdig + AvalonEdit。**

一句话理由：这是 Windows 单平台 Markdown 编辑器的"黄金组合"——WPF 冷启动约 0.4 秒（远快于 Electron 的 1–3 秒），Markdig 是业界最强的 CommonMark 解析库之一，WebView2 复用系统 Edge 内核（零打包成本、渲染质量等同 Chrome），其 `PrintToPdfAsync` 一行代码即可完成高保真 PDF 导出，剪贴板图片粘贴由 WPF 原生支持。备选方案是 Rust + Tauri 2（启动更快、体积更小，但团队需要 Rust + 前端双技能）。

以下是完整需求文档，可直接保存为 `PRD.md`。

---

# MarkDesk（代号）产品需求文档

| 文档属性 | 内容 |
|---|---|
| 文档版本 | v1.0（草案） |
| 日期 | 2026-07-31 |
| 平台 | Windows 10 (1809+) / Windows 11 |
| 界面语言 | English only（v1） |
| 文档形态 | 单文档（Single Document），不支持多标签页 |

---

## 1. 产品概述

### 1.1 产品定位

A fast, lightweight Markdown viewer & editor for Windows —— 一个窗口、一份文档，在**源码编辑**与**渲染查看**之间无缝切换。

### 1.2 目标用户与场景

- 技术写作者：撰写 README、技术笔记，需要粘贴截图、导出 PDF 交付
- 文档维护者：快速打开 `.md` 查看渲染效果，偶尔切回源码微调
- 偏好轻量工具、反感 Electron 臃肿启动的用户

### 1.3 产品边界（v1 明确不做）

| 不做 | 说明 |
|---|---|
| 多文档 / 标签页 | 按需求明确排除，一次只打开一个文件 |
| 插件系统、协同编辑、云同步 | 不在 v1 范围 |
| 所见即所得（WYSIWYG）编辑 | 只做"源码编辑 + 渲染预览"双模 |
| Linux / macOS | 仅 Windows |

---

## 2. 功能需求

优先级：`P0` 必须交付 / `P1` 应该交付 / `P2` 可以延后

### 2.1 核心功能（P0）

| 编号 | 需求 | 描述 |
|---|---|---|
| FR-01 | 打开文件 | `Ctrl+O` / 拖拽 / 命令行参数 `markdesk.exe file.md` 打开 `.md` / `.markdown` 文件；大文件（>5 MB）给出警告 |
| FR-02 | 源码编辑 | 基于 AvalonEdit：Markdown 语法高亮、行号、查找替换（`Ctrl+F/H`）、自动换行开关、撤销/重做 |
| FR-03 | 渲染查看 | WebView2 渲染 HTML，支持 GFM 表格、任务列表、代码高亮、数学公式（KaTeX）、脚注 |
| FR-04 | 三种视图模式 | 工具栏切换 **Edit / Split / Preview**（快捷键 `F6` 循环切换） |
| FR-05 | 切换即渲染 | **从 Edit 切换到 Split/Preview 时，强制消费脏标记并重新渲染**，保证预览永不过期；Split 模式下输入防抖（150 ms）实时渲染 |
| FR-06 | 图片粘贴 | `Ctrl+V` 粘贴剪贴板图片 → 自动保存到文档同目录的 `assets/` 文件夹 → 在光标处插入 `![](assets/img-20260731-153012.png)` → 预览立即可见；文件未保存时先提示 Save As |
| FR-07 | 导出 PDF | `Ctrl+P` / 菜单 `File → Export as PDF` → 将渲染结果通过 `PrintToPdfAsync` 导出；相对路径图片在 PDF 中正确显示 |
| FR-08 | 保存 | `Ctrl+S` 保存（标题栏显示 `●` 未保存标记）、`Ctrl+Shift+S` 另存为；关闭未保存文件时弹出确认 |
| FR-09 | 布局阈值自适应 | 窗口宽度 **< 阈值** 时，Split 布局自动折叠为 Edit/Preview 标签式切换；**≥ 阈值** 时恢复左右分栏 |
| FR-10 | 配置中心 | `Settings` 对话框中**显示并可修改布局阈值**，同时实时显示当前窗口宽度（如 `Current window width: 1024 px`）帮助用户理解阈值含义；所有配置持久化到 `%AppData%/MarkDesk/settings.json` |

### 2.2 增强功能（P1）

| 编号 | 需求 | 描述 |
|---|---|---|
| FR-11 | 滚动同步 | Split 模式下编辑区与预览区按段落比例同步滚动（可在设置中关闭） |
| FR-12 | 最近文件 | `File → Recent` 记录最近 10 个文件 |
| FR-13 | 外部修改检测 | FileSystemWatcher 监测文件被外部修改，提示 `Reload / Keep mine` |
| FR-14 | 拖拽行为区分 | 拖入 `.md`/`.markdown` → 打开文件（归并到 FR-01）；拖入图片文件 → 复制进 `assets/` 并插入链接（明确与 FR-01 的边界） |
| FR-15 | 编码探测 | 打开文件自动探测编码（UTF-8 BOM → UTF-8 → GBK 回退）；状态栏显示当前编码并支持手动切换/重载 |

### 2.3 锦上添花（P2）

FR-16 状态栏字数/词数统计 · FR-17 预览缩放（`Ctrl+滚轮`）· FR-18 深色主题（跟随系统，预览区注入 dark CSS） · FR-19 便携模式（配置文件与 exe 同目录） · FR-20 崩溃草稿恢复（定时自动保存未提交草稿，异常退出后恢复）

---

## 3. 非功能需求

| 编号 | 类别 | 指标 |
|---|---|---|
| NFR-01 | 启动速度 | 冷启动到可编辑 ≤ **1.5 s**；热启动 ≤ **400 ms**；预览首次就绪 ≤ 2.5 s（含 WebView2 后台初始化） |
| NFR-02 | 渲染性能 | ≤ 5 万字符的文档，输入到 HTML 生成 ≤ 200 ms；含 KaTeX/highlight.js 的完整 DOM 渲染首屏可放宽至 ≤ 600 ms |
| NFR-03 | 资源占用 | 典型使用下，主进程 + WebView2 派生进程（渲染+GPU）合计常驻内存 ≤ 250 MB；单文件 exe ≤ 30 MB（不含系统 WebView2 运行时；WPF 关闭裁剪以兼容单文件发布） |
| NFR-04 | 分发形态 | 绿色单文件 exe（portable）为首选，可选 MSIX 安装包 |
| NFR-05 | 界面语言 | 全英文 UI（见 §6 关键文案） |
| NFR-06 | 数据安全 | 保存采用"写临时文件→原子替换"，崩溃不损坏原文件 |

---

## 4. 技术选型

### 4.1 候选方案对比

| 方案 | 启动速度 | Markdown 生态 | PDF 导出 | 图片粘贴 | 包体积 | 开发效率 | 结论 |
|---|---|---|---|---|---|---|---|
| **C# + WPF + WebView2** | 快（~0.4 s） | 强（Markdig） | 一行 API（`PrintToPdfAsync`） | 原生剪贴板 | ~20 MB | 高 | ✅ **推荐** |
| Rust + Tauri 2 | 极快（<0.2 s） | 良（markdown-it，JS 侧） | 经 WebView2 打印能力 | 支持 | ~8 MB | 中（Rust 门槛） | 🔄 备选 |
| C++ + Qt 6 | 快 | 弱（需自研/第三方） | 需捆 QWebEngine（+100 MB） | 支持 | 大 | 低 | ❌ |
| Electron + markdown-it | 慢（1–3 s） | 强 | 成熟 | 支持 | 150 MB+ | 高 | ❌ 违背 NFR-01 |
| Python + PySide6 | 中偏慢 | 中 | 繁琐 | 支持 | 打包臃肿 | 中 | ❌ |

### 4.2 推荐技术栈

| 层 | 技术 | 许可 |
|---|---|---|
| 语言 / 运行时 | C# 14 / **.NET 10 LTS**（支持至 2028-11） | MIT |
| UI 框架 | WPF + CommunityToolkit.Mvvm（MVVM） | MIT |
| 源码编辑器 | AvalonEdit（Markdown xshd 高亮） | MIT |
| Markdown 引擎 | Markdig（CommonMark + GFM 扩展，预编译静态 Pipeline；**禁用 raw HTML 透传** 以防预览/PDF 中的 XSS） | BSD-2 |
| 渲染 / PDF | Microsoft.Web.WebView2（系统自带 Edge 内核） | Microsoft |
| 代码高亮（预览内） | highlight.js · 公式 KaTeX（随 HTML 模板内嵌） | MIT |
| 配置 | `settings.json` @ `%AppData%`（System.Text.Json） | — |

### 4.3 架构示意

```
┌──────────────────── WPF Shell (MVVM) ────────────────────┐
│  MainWindow ── MainViewModel ── SettingsService          │
│       │              │                │                  │
│       │        DocumentModel (dirty flag, file path)     │
├───────┴────────┬─────┴─────────────┬──┴──────────────────┤
│  AvalonEdit    │  MarkdigRenderer  │  WebView2Control    │
│  源码编辑       │  MD → HTML        │  渲染预览            │
│  剪贴板图片 ────┼──→ ImagePaster    │  PrintToPdfAsync ──→ PDF
│                │  (debounce 150ms) │                     │
└────────────────┴───────────────────┴─────────────────────┘
        FileSystemWatcher（外部修改）   LayoutAdaptor（窗口宽度 vs 阈值）
```

### 4.4 关键实现要点（影响需求能否达成）

1. **启动速度**：WebView2 采用**懒初始化**——窗口先展示 AvalonEdit 编辑器（达成 NFR-01 "可编辑 ≤1.5 s"），`EnsureCoreWebView2Async` 在后台进行；发布采用 ReadyToRun + 单文件。
2. **相对路径图片**（FR-06 / FR-07 的共同前提）：用 `SetVirtualHostNameToFolderMapping` 把文档所在目录映射为 `https://mdlocal/`，预览与 PDF 中的 `assets/xx.png` 均解析为 `https://mdlocal/assets/xx.png`，无需转 base64。
3. **切换即渲染**（FR-05）：ViewModel 维护 `isDirty` 标记；任何模式切换事件先执行 `RenderNow()` 再切换视图。
4. **布局阈值**（FR-09/10）：`Window.Width` 与设置值通过 `IMultiValueConverter` 比较，驱动 Split/Tabbed 两种视觉状态；设置面板双向绑定该值，改完即时生效。
5. **PDF 导出**：使用独立打印模板（`@media print` CSS：A4、页边距、代码块防截断），`PrintToPdfAsync(printSettings)` 关闭页眉页脚。

---

## 5. 界面与交互

### 5.1 主窗口线框（Split 模式）

```
┌─ MarkDesk ─ [readme.md] ● ────────────────────────────□✕┐
│ File  Edit  View  Export  Help                          │
│ (Edit) (Split) (Preview)          [Wrap] [Zoom −/+]     │
├────────────────────────┬────────────────────────────────┤
│ 1  # Release Notes     │  Release Notes                 │
│ 2  Paste a **shot**:   │  Paste a shot:                 │
│ 3  ![](assets/a.png)   │  [image]                       │
├────────────────────────┴────────────────────────────────┤
│ Ln 3, Col 22 │ UTF-8 │ 128 words │ Preview: synced ✓    │
└─────────────────────────────────────────────────────────┘
```

窗口宽度 < 阈值时，分栏消失，顶部出现 `[Edit] | [Preview]` 标签页切换。

### 5.2 关键英文 UI 文案（NFR-05）

`File / Open… / Save / Save As… / Export as PDF / Recent / Exit` · `Edit / Split / Preview` · `Settings → Layout → Split view width threshold (px): [960]  Current window width: 1024 px` · 未保存提示 `You have unsaved changes. Save before closing?`

### 5.3 配置项清单（FR-10）

| Key | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `layoutThresholdPx` | int | **960** | 布局切换阈值，Settings 面板可见可改 |
| `defaultViewMode` | enum | `Split` | 打开文件时的初始模式 |
| `assetsFolderName` | string | `assets` | 粘贴图片的保存目录名 |
| `imageNamePattern` | string | `img-{yyyyMMdd-HHmmss}-{n}` | 粘贴图片命名模板；`{n}` 为同名去重序号（从 1 起，避免同秒粘贴覆盖） |
| `scrollSync` | bool | `true` | 滚动同步开关 |
| `renderDebounceMs` | int | `150` | 实时渲染防抖 |
| `pdfPageSize` | enum | `A4` | PDF 纸张 |

---

## 6. 里程碑（按 1 名开发者估算）

| 里程碑 | 周期 | 交付物 |
|---|---|---|
| M1 骨架 | 第 1 周 | MVVM 框架、打开/保存、AvalonEdit 高亮、状态栏 |
| M2 渲染 | 第 2 周 | Markdig 管线、WebView2 预览、三模式切换、阈值自适应 |
| M3 特性 | 第 3 周 | 图片粘贴、PDF 导出、Settings 面板 |
| M4 收尾 | 第 4 周 | 滚动同步、最近文件、外部修改检测、打包（portable + MSIX）、验收 |

> 注：以上为"无重大返工"的理想路径。含打包双形态与回归测试，**生产级交付的现实周期建议预留 6–8 周**；若进度紧张，FR-11 滚动同步、FR-13 外部修改检测可视情况后置至 M5。

---

## 7. 验收标准（可度量）

1. 双击 exe，秒表计时冷启动 ≤ 1.5 s 即可输入文字；
2. 打开含表格/代码块/公式的样例文档，Preview 渲染正确率 100%（CommonMark 规范用例）；
3. 截图后 `Ctrl+V`，`assets/` 目录生成 PNG，光标处出现链接，预览与导出的 PDF 中图片均可见；
4. 导出的 PDF 与预览渲染一致，中文无乱码，代码块不被生硬截断；
5. 设置中将阈值改为 1200，窗口缩至 1100 px 宽时布局立即折叠为标签式；
6. Edit→Preview 切换后，预览内容与最后一次输入完全一致（验证 FR-05）；
7. 任务管理器确认主进程 + WebView2 派生进程合计常驻内存 ≤ 250 MB；
8. 打开 GBK 编码的中文文档正确显示，状态栏可切换编码重载（验证 FR-15）；
9. 同一秒内连续粘贴两张图片，生成不同文件名、互不覆盖（验证 `imageNamePattern` 的 `{n}` 去重）。

---

## 8. 风险与对策

| 风险 | 对策 |
|---|---|
| 少数旧机器无 WebView2 运行时 | 程序内置 Evergreen Bootstrapper，首启自动检测并引导安装 |
| Markdig 对冷门语法支持差异 | 启用全部官方扩展 + 规范用例回归测试 |
| PDF 中相对路径图片丢失 | 虚拟主机映射方案（§4.4-2），并纳入验收项 3 |
| AvalonEdit 原生无 Markdown 高亮 | 采用社区 xshd 定义或自绘 highlighting 规则，M1 内完成 |
| 文件编码识别错误导致乱码 | FR-15 三级回退（BOM→UTF-8→GBK）+ 状态栏手动切换重载 |
| 预览中 Markdown 内嵌 HTML 的 XSS | 禁用 Markdig raw HTML 扩展（§4.2），PDF 导出同样受保护 |
| 渲染回归（Markdig/扩展升级后输出漂移） | 对 Markdig→HTML 建立快照测试，锁定 CommonMark 规范用例输出 |
| 单文件 exe 超 30 MB（WPF 不支持完全裁剪） | 关闭裁剪 + ReadyToRun；若超限则改 MSIX 为首选、portable 退为可选 |


