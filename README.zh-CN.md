# MarkDesk

一个面向 Windows 的轻量级 Markdown 查看器与编辑器。打开 `.md`、阅读、编辑、保存,导出 PDF —— 仅此而已。

![Version](https://img.shields.io/badge/version-0.1.0-blue)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey)
![License](https://img.shields.io/badge/license-MIT-green)

## 这是什么

MarkDesk 是一款专注的 Markdown 工具。它不是笔记本、不是知识库、也不是同步服务。它只做好一件事:**让你在本地打开、阅读、编辑 Markdown 文件**,配合忠实的实时预览,以及技术文档所需的一切离线渲染能力。

- **打开**任意 `.md` / `.markdown` 文件(对话框、最近列表、拖放、命令行,或设为 Windows 默认程序)
- **编辑**真正的代码编辑器(语法高亮、查找替换、自动换行)
- **预览**分栏 / 全屏,边打字边更新
- **导出**PDF(无论应用主题如何,PDF 始终以清爽的浅色版面打印)

## 功能一览

| 领域 | 细节 |
| --- | --- |
| 编辑 | 基于 AvalonEdit,Markdown 语法高亮、查找替换、自动换行、行列光标、字数统计 |
| 实时预览 | 仅编辑 / 分栏 / 仅预览 三种布局,150ms 防抖重渲染,滚动同步 |
| 渲染 | 基于 Markdig 的 GitHub 风格 Markdown —— 表格、任务列表、脚注、删除线、emoji(`:smile:`)、标题锚点、自定义容器(`:::warning`)、GitHub Alerts(`> [!NOTE]`) |
| 富内容 | 语法高亮(highlight.js)、数学公式(KaTeX)、图表(Mermaid)—— **全部本地打包,完全离线** |
| 主题 | 浅色 / 深色 / 跟随系统;覆盖标题栏、菜单、弹窗、对话框、消息框 |
| 文件 | 打开 / 保存 / 另存为、最近文件、外部修改检测与重载提示、大文件警告 |
| PDF 导出 | A4 / Letter,通过专用离屏 WebView2 以强制浅色版面打印(绝不干扰屏幕预览) |
| 图片 | 粘贴剪贴板图片(保存到文档旁,插入为 Markdown 链接) |
| 文件关联 | 从工具菜单一键将 MarkDesk 注册为 `.md` 的 Windows 默认程序(用户级,无需管理员) |

## 技术栈

| 层 | 技术 |
| --- | --- |
| 语言 | C# 14 / .NET 10 |
| UI 框架 | WPF(Windows Presentation Foundation) |
| 架构模式 | MVVM(`CommunityToolkit.Mvvm` —— 源生成器、可观察对象、命令) |
| Markdown 解析 | [Markdig](https://github.com/xoofx/markdig) 1.3.2 |
| 代码编辑器 | [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) 6.3.1.120 |
| 预览引擎 | Microsoft Edge WebView2(Chromium 内核) |
| 依赖注入 | `Microsoft.Extensions.DependencyInjection` |
| 前端资源 | highlight.js 11.9.0、KaTeX 0.16.11、Mermaid 11.16.0(本地打包,离线可用) |
| 原生互操作 | `dwmapi`(沉浸式深色标题栏)、`shell32`(文件关联刷新) |

## 架构

```
MarkDesk
├── App.xaml(.cs)            应用启动、DI 容器、命令行参数处理
├── MainWindow.xaml(.cs)     外壳:工具栏、菜单、状态栏、布局/主题装配
├── ViewModels/
│   ├── MainViewModel.cs     文档状态、打开/保存、视图模式、主题
│   └── SettingsViewModel.cs 设置对话框数据模型
├── Models/
│   └── AppSettings.cs       持久化设置(JSON)+ 枚举(ViewMode、ThemeMode…)
├── Services/
│   ├── MarkdownRenderer.cs  Markdig 管线(输出 HTML)
│   ├── PreviewTemplate.cs   HTML 模板 + CSS + JS(alerts、锚点、mermaid 初始化)
│   ├── FileService.cs       带编码检测的加载/保存
│   ├── EncodingDetector.cs  BOM / 启发式编码检测
│   ├── FileWatcher.cs       外部修改通知
│   ├── ImagePasterService.cs 剪贴板图片 → 文件 → Markdown 链接
│   ├── DialogService.cs     主题化消息框与文件对话框
│   ├── ThemeService.cs      系统深浅色检测
│   ├── WindowTheme.cs       DWM 深色标题栏助手(所有窗口共用)
│   ├── FileAssociationService.cs  注册 .md 处理器(HKCU)
│   └── SettingsService.cs   JSON 设置持久化
├── Controls/
│   ├── MarkdownEditor.xaml(.cs)    AvalonEdit 包装 + FindReplacePanel 宿主
│   ├── PreviewView.xaml(.cs)       WebView2 宿主(屏幕预览 + 离屏打印)
│   ├── FindReplacePanel.xaml(.cs)  查找替换浮动面板
│   └── ThemedMessageBox.xaml(.cs)  替代系统 MessageBox 的主题化消息框
├── Views/
│   └── SettingsDialog.xaml(.cs)    设置窗口
└── Assets/web/vendor/      本地打包前端库(highlight、katex、mermaid)
```

**数据流:** 编辑器 → `DocumentText`(绑定)→ 防抖 → `MarkdownRenderer` → `PreviewTemplate.Build` → `PreviewView.UpdateAsync`(WebView2)。

## 上手

### 前置条件
- Windows 10 1809+(x64)
- Microsoft Edge WebView2 运行时(Windows 11 已预装;Windows 10 从 [aka.ms/webview2](https://aka.ms/webview2) 安装)
- 框架依赖版需要:.NET 10 Desktop 运行时

### 从源码运行
```bash
dotnet build MarkDesk.slnx -c Release
src\MarkDesk\bin\Release\net10.0-windows\MarkDesk.exe samples\mermaid-demo.md
```

### 发布
`src/MarkDesk/Properties/PublishProfiles/` 提供两种配置:
- **Portable** —— 自包含单文件 `.exe`(约 78 MB,无需其它依赖)
- **PortableFramework** —— 框架依赖,约 70 个小文件(约 8 MB),需 .NET 10 Desktop 运行时

```bash
dotnet publish src/MarkDesk -c Release -p:PublishProfile=Portable
dotnet publish src/MarkDesk -c Release -p:PublishProfile=PortableFramework
```

## 开发说明

本项目通过与 AI 助手结对编程完成,使用 [opencode](https://opencode.ai) CLI。

开发期间使用过的模型:
- **GLM-5.2**(智谱 AI)—— 主力编码模型
- **MiniMax M2.5** —— 早期会话

## 许可证

MIT —— 详见 [LICENSE](LICENSE)。
