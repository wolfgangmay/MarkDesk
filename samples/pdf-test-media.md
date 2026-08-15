# 媒体与公式压力测试

本文件验证 PDF 导出对图片(不同宽高比)、Mermaid 图表(多类型)、KaTeX 公式(行内/展示/长公式)的排版与分页处理。

## 一、图片

### 小图(240×160,应内联于文字流)

![small image](pdf-assets/small.svg)

此图较小,应当跟随文字排版,不需要单独占页。

### 高图(400×1200,应受 23cm 高度约束缩放)

![tall image](pdf-assets/tall.svg)

超高图片必须被缩放到一页之内,不允许溢出到页面外。

### 宽图(1600×300,应按页宽缩放)

![wide image](pdf-assets/wide.svg)

超宽图片按内容宽度缩放,不允许被裁剪。

## 二、Mermaid 图表(每张图必须保持完整,不得切断)

### 流程图

```mermaid
flowchart TD
    A[开始] --> B{是否已有文档?}
    B -->|是| C[打开现有文档]
    B -->|否| D[创建新文档]
    C --> E[编辑内容]
    D --> E
    E --> F{选择视图}
    F --> G[编辑模式]
    F --> H[分屏模式]
    F --> I[预览模式]
    G --> J[导出 PDF]
    H --> J
    I --> J
```

### 时序图

```mermaid
sequenceDiagram
    participant U as 用户
    participant M as MarkDesk
    participant W as WebView2
    U->>M: 打开 Markdown 文件
    M->>M: 解析与渲染
    M->>W: 加载预览 HTML
    W-->>M: 渲染完成
    U->>M: 请求导出 PDF
    M->>W: 执行打印布局脚本
    W-->>M: 布局就绪
    M->>W: PrintToPdf
    W-->>M: PDF 文件
    M-->>U: 保存完成
```

### 甘特图

```mermaid
gantt
    title 项目进度示例
    dateFormat YYYY-MM-DD
    section 阶段一
    需求分析     :a1, 2025-01-01, 10d
    概要设计     :a2, after a1, 8d
    section 阶段二
    详细设计     :a3, after a2, 12d
    编码实现     :a4, after a3, 20d
    section 阶段三
    测试验收     :a5, after a4, 10d
    发布上线     :a6, after a5, 2d
```

## 三、数学公式

### 行内公式

质能方程 $E = mc^2$、欧拉公式 $e^{i\pi} + 1 = 0$、黄金比例 $\varphi = \frac{1+\sqrt{5}}{2}$ 都应与文字基线对齐。

### 展示公式

$$\oint_{\partial \Omega} \mathbf{F} \cdot d\mathbf{r} = \iint_{\Omega} (\nabla \times \mathbf{F}) \cdot d\mathbf{A}$$

### 超长公式(不允许切断,允许横向缩排)

$$\sum_{n=1}^{\infty} \frac{1}{n^2} = \frac{\pi^2}{6} \qquad \prod_{p \in \mathbb{P}} \frac{1}{1 - p^{-2}} = \frac{\pi^2}{6} \qquad \int_{-\infty}^{\infty} e^{-x^2}\,dx = \sqrt{\pi} \qquad \lim_{n \to \infty} \left(1 + \frac{1}{n}\right)^n = e$$

### 公式组

$$\begin{aligned} \nabla \cdot \mathbf{E} &= \frac{\rho}{\varepsilon_0} \\ \nabla \cdot \mathbf{B} &= 0 \\ \nabla \times \mathbf{E} &= -\frac{\partial \mathbf{B}}{\partial t} \\ \nabla \times \mathbf{B} &= \mu_0 \mathbf{J} + \mu_0 \varepsilon_0 \frac{\partial \mathbf{E}}{\partial t} \end{aligned}$$

## 四、图文混排与分页混合场景

以下把公式、图、代码交错排列,验证复杂混合内容的分页质量。

$$\hat{y} = \sigma(W_l \cdot \sigma(W_{l-1} \cdot \ldots \sigma(W_1 x + b_1) \ldots + b_{l-1}) + b_l)$$

![small again](pdf-assets/small.svg)

```text
神经网络:输入层 -> 隐藏层(可多层)-> 输出层
激活函数:Sigmoid / ReLU / GELU / Tanh
训练方法:反向传播 + 梯度下降(SGD、Adam、AdamW)
```

$$P(A \mid B) = \frac{P(B \mid A)\,P(A)}{P(B)}$$

贝叶斯定理如上。连续的公式与文字交替,每 个 公 式 块 都 是 原 子 单 元,不应被切断;文字部分正常按孤行规则分页。

![tall again](pdf-assets/tall.svg)

最后一段文字:本文档的所有媒体元素(图 ×5、Mermaid ×3、公式 ×7)在导出 PDF 后,均应完整呈现在各自页面内,不出现裁剪、溢出或空白异常。
