using System.Globalization;

namespace MarkDesk.Services;

public sealed class PreviewTemplate
{
    private const string HighlightCssLight = "https://mdassets/vendor/highlight/github.min.css";
    private const string HighlightCssDark = "https://mdassets/vendor/highlight/github-dark.min.css";
    private const string HighlightJs = "https://mdassets/vendor/highlight/highlight.min.js";
    private const string KatexCss = "https://mdassets/vendor/katex/katex.min.css";
    private const string KatexJs = "https://mdassets/vendor/katex/katex.min.js";
    private const string KatexAutoRender = "https://mdassets/vendor/katex/auto-render.min.js";

    public string Build(string bodyHtml, bool dark = false)
    {
        var scheme = dark ? "dark" : "light";
        var highlightCss = dark ? HighlightCssDark : HighlightCssLight;
        return string.Format(CultureInfo.InvariantCulture, Template, scheme, highlightCss, HighlightJs, KatexCss, KatexJs, KatexAutoRender, bodyHtml);
    }

    private const string Template = @"<!DOCTYPE html>
<html data-theme=""{0}"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<base href=""https://mdlocal/"">
<link rel=""stylesheet"" href=""{1}"">
<link rel=""stylesheet"" href=""{3}"">
<style>
:root {{
  --fg:#24292f; --bg:#ffffff; --border:#d0d7de; --muted:#57606a;
  --code-bg:#f6f8fa; --link:#0969da; --quote:#6e7781; --quote-border:#d0d7de;
}}
html[data-theme=""dark""] {{
  --fg:#e6edf3; --bg:#0d1117; --border:#30363d; --muted:#8b949e;
  --code-bg:#161b22; --link:#58a6ff; --quote:#8b949e; --quote-border:#30363d;
}}
* {{ box-sizing:border-box; }}
body {{
  font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif;
  color:var(--fg); background:var(--bg);
  line-height:1.6; max-width:980px; margin:0 auto; padding:32px 24px;
}}
h1,h2,h3,h4,h5,h6 {{ line-height:1.25; margin:24px 0 16px; font-weight:600; }}
h1 {{ font-size:2em; border-bottom:1px solid var(--border); padding-bottom:.3em; }}
h2 {{ font-size:1.5em; border-bottom:1px solid var(--border); padding-bottom:.3em; }}
a {{ color:var(--link); text-decoration:none; }}
a:hover {{ text-decoration:underline; }}
img {{ max-width:100%; }}
code {{ font-family:'Cascadia Code',Consolas,monospace; background:var(--code-bg); padding:.2em .4em; border-radius:6px; font-size:.9em; }}
pre {{ background:var(--code-bg); padding:16px; border-radius:8px; overflow:auto; }}
pre code {{ background:none; padding:0; }}
blockquote {{ color:var(--quote); border-left:.25em solid var(--quote-border); margin:0 0 16px; padding:0 1em; }}
table {{ border-collapse:collapse; margin:16px 0; display:block; overflow:auto; }}
th,td {{ border:1px solid var(--border); padding:6px 13px; }}
th {{ background:var(--code-bg); font-weight:600; }}
hr {{ border:0; border-top:1px solid var(--border); margin:24px 0; }}
ul.contains-task-list {{ list-style:none; padding-left:1.5em; }}
.footnotes {{ font-size:.9em; color:var(--muted); border-top:1px solid var(--border); margin-top:32px; padding-top:16px; }}
.footnote-ref sup {{ font-size:.75em; }}
</style>
<style media=""print"">
@page {{ size:A4; margin:18mm; }}
body {{ max-width:none; color:#000; background:#fff; }}
pre,blockquote {{ page-break-inside:avoid; }}
pre {{ white-space:pre-wrap; word-break:break-word; }}
h1,h2,h3 {{ page-break-after:avoid; }}
</style>
</head>
<body>
{6}
<script src=""{2}""></script>
<script src=""{4}""></script>
<script src=""{5}""></script>
<script>
  if (window.hljs) {{ hljs.highlightAll(); }}
  if (window.renderMathInElement) {{
    renderMathInElement(document.body, {{
      delimiters:[
        {{left:'$$',right:'$$',display:true}},
        {{left:'$',right:'$',display:false}},
        {{left:'\\\\(',right:'\\\\)',display:false}},
        {{left:'\\\\[',right:'\\\\]',display:true}}
      ],
      throwOnError:false
    }});
  }}
</script>
</body>
</html>";
}
