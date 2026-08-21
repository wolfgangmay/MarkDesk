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
    private const string MermaidJs = "https://mdassets/vendor/mermaid/mermaid.min.js";

    public string Build(string bodyHtml, bool dark = false)
    {
        var scheme = dark ? "dark" : "light";
        var highlightCss = dark ? HighlightCssDark : HighlightCssLight;
        return string.Format(CultureInfo.InvariantCulture, Template, scheme, highlightCss, HighlightJs, KatexCss, KatexJs, KatexAutoRender, MermaidJs, bodyHtml);
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
  --scroll-thumb:#c8c8c8; --scroll-thumb-hover:#8a8a8a;
}}
html[data-theme=""dark""] {{
  --fg:#e6edf3; --bg:#1e1e1e; --border:#30363d; --muted:#8b949e;
  --code-bg:#161b22; --link:#58a6ff; --quote:#8b949e; --quote-border:#30363d;
  --scroll-thumb:#4a4a52; --scroll-thumb-hover:#6b6b75;
}}
/* WebView2 ships native Chromium scrollbars (with arrow buttons) unless the
   page styles them; match the themed WPF scrollbars used by the other panes. */
::-webkit-scrollbar {{ width:10px; height:10px; }}
::-webkit-scrollbar-track {{ background:transparent; }}
::-webkit-scrollbar-thumb {{
  background:var(--scroll-thumb); border-radius:5px;
  border:2px solid var(--bg);
}}
::-webkit-scrollbar-thumb:hover {{ background:var(--scroll-thumb-hover); }}
::-webkit-scrollbar-corner {{ background:transparent; }}
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
.mermaid {{ text-align:center; margin:16px 0; }}
.mermaid svg {{ max-width:100%; height:auto; }}
.footnotes {{ font-size:.9em; color:var(--muted); border-top:1px solid var(--border); margin-top:32px; padding-top:16px; }}
.footnote-ref sup {{ font-size:.75em; }}
/* Custom containers (:::name ... :::) */
div.warning,div.danger {{ border-left:4px solid #cf222e; background:#cf222e14; }}
div.note,div.info {{ border-left:4px solid #0969da; background:#0969da14; }}
div.tip {{ border-left:4px solid #1a7f37; background:#1a7f3714; }}
div.success {{ border-left:4px solid #1f883d; background:#1f883d14; }}
div.warning,div.danger,div.note,div.info,div.tip,div.success {{ padding:8px 16px; margin:16px 0; border-radius:4px; }}
div.warning > :first-child,div.danger > :first-child,div.note > :first-child,div.info > :first-child,div.tip > :first-child,div.success > :first-child {{ margin-top:0; }}
div.warning > :last-child,div.danger > :last-child,div.note > :last-child,div.info > :last-child,div.tip > :last-child,div.success > :last-child {{ margin-bottom:0; }}
/* GitHub-style alerts (> [!NOTE] etc.) */
.markdown-alert {{ padding:8px 16px; margin:16px 0; border-left:4px solid var(--border); border-radius:4px; }}
.markdown-alert > :first-child {{ margin-top:0; }}
.markdown-alert > :last-child {{ margin-bottom:0; }}
.markdown-alert-title {{ font-weight:600; margin:0 0 6px; }}
.markdown-alert-note {{ border-left-color:#0969da; background:#0969da14; }} .markdown-alert-note .markdown-alert-title {{ color:#0969da; }}
.markdown-alert-tip {{ border-left-color:#1a7f37; background:#1a7f3714; }} .markdown-alert-tip .markdown-alert-title {{ color:#1a7f37; }}
.markdown-alert-important {{ border-left-color:#8250df; background:#8250df14; }} .markdown-alert-important .markdown-alert-title {{ color:#8250df; }}
.markdown-alert-warning {{ border-left-color:#9a6700; background:#9a670014; }} .markdown-alert-warning .markdown-alert-title {{ color:#9a6700; }}
.markdown-alert-caution {{ border-left-color:#cf222e; background:#cf222e14; }} .markdown-alert-caution .markdown-alert-title {{ color:#cf222e; }}
/* Long-document first paint: skip layout/paint of offscreen blocks. Without
   this a multi-MB document (~100k nodes) pays full layout before load, which
   made every file switch show the preview seconds late. Print media keeps
   full layout (pagination must see real heights), and the print-measure
   pass disables it too (see ApplyPrintLayoutAsync). */
@media screen {{
  body > :not(script) {{ content-visibility:auto; contain-intrinsic-size:auto 480px; }}
  html.md-measuring body > :not(script) {{ content-visibility:visible; }}
}}
</style>
<style media=""print"">
/* Page geometry (size + margins) comes exclusively from the WebView2 print
   settings (see PreviewView.ApplyPrintLayout) — no page CSS here. */
body {{ max-width:none; padding:0; color:#000; background:#fff; }}
p,li,dd,dt {{ orphans:3; widows:3; }}
h1,h2,h3,h4,h5,h6 {{ break-after:avoid; }}
tr {{ break-inside:avoid; }}
table {{ display:table; overflow:visible; font-size:.85em; }}
img {{ max-height:23cm; object-fit:contain; }}
pre {{ white-space:pre-wrap; word-break:break-word; }}
/* Small cohesive units stay together; the print-layout script tags short
   blocks (<= ~30% page height) with .md-keep so only cheap moves happen. */
.md-keep,.markdown-alert,.katex-display,.mermaid,div.warning,div.danger,div.note,div.info,div.tip,div.success {{ break-inside:avoid; }}
</style>
</head>
<body>
{7}
<script src=""{2}""></script>
<script src=""{4}""></script>
<script src=""{5}""></script>
<script src=""{6}""></script>
<script>
  window.__mdReadyJobs = [];
  (function(){{
    var bad = /^\s*(javascript|vbscript|data):/i;
    function sanitize(root){{
      root.querySelectorAll('a[href]').forEach(function(a){{
        if (bad.test(a.getAttribute('href')||'')) a.removeAttribute('href');
      }});
    }}
    sanitize(document.body);
    document.addEventListener('click', function(e){{
      var a = e.target.closest && e.target.closest('a');
      if (a && bad.test(a.getAttribute('href')||'')) e.preventDefault();
    }}, true);
  }})();
  (function(){{
    document.addEventListener('click', function(e){{
      var a = e.target.closest && e.target.closest('a');
      if (a) return; // links have their own handling
      var el = e.target.closest && e.target.closest('[data-line]');
      if (el && window.chrome && window.chrome.webview)
        window.chrome.webview.postMessage({{type:'mdline', line: parseInt(el.getAttribute('data-line'),10)||0}});
    }});
  }})();
  (function(){{
    var map = {{NOTE:'note',TIP:'tip',IMPORTANT:'important',WARNING:'warning',CAUTION:'caution'}};
    document.querySelectorAll('blockquote').forEach(function(bq){{
      var p = bq.querySelector('p');
      if(!p) return;
      var m = p.innerHTML.match(/^\s*\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]\s*(?:<br\s*\/?>)?/i);
      if(!m) return;
      var type = map[m[1].toUpperCase()];
      p.innerHTML = p.innerHTML.slice(m[0].length);
      bq.classList.add('markdown-alert','markdown-alert-'+type);
      var h = document.createElement('p');
      h.className = 'markdown-alert-title';
      h.textContent = m[1].charAt(0).toUpperCase()+m[1].slice(1).toLowerCase();
      bq.insertBefore(h, p);
    }});
  }})();
  (function(){{
    document.addEventListener('click', function(e){{
      var a = e.target.closest && e.target.closest('a');
      if(!a) return;
      var href = a.getAttribute('href');
      if(!href || href.charAt(0) !== '#') return;
      if(href.length <= 1){{ e.preventDefault(); return; }}
      var raw = href.slice(1), id = raw;
      try {{ id = decodeURIComponent(raw); }} catch(err) {{}}
      var el = document.getElementById(raw) || document.getElementById(id) || document.getElementsByName(id)[0];
      if(el){{ e.preventDefault(); el.scrollIntoView({{behavior:'smooth'}}); }}
    }});
  }})();
  if (window.hljs && document.querySelector('pre code')) {{ hljs.highlightAll(); }}
  if (window.renderMathInElement) {{
    // Auto-render walks every text node — seconds on a multi-MB document.
    // Skip it entirely when the text cannot contain math (no delimiters).
    var mdText = document.body.textContent || '';
    if (mdText.indexOf('$') >= 0 || mdText.indexOf('\\\\(') >= 0 || mdText.indexOf('\\\\[') >= 0) {{
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
  }}
  if (window.mermaid) {{
    mermaid.initialize({{
      startOnLoad:false,
      theme: document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'default',
      securityLevel:'loose'
    }});
    document.querySelectorAll('pre code.language-mermaid').forEach((code) => {{
      const pre = code.parentElement;
      const div = document.createElement('div');
      div.className = 'mermaid';
      div.textContent = code.textContent;
      pre.replaceWith(div);
    }});
    var mNodes = document.querySelectorAll('.mermaid');
    if (mNodes.length) {{
      window.__mdReadyJobs.push(mermaid.run().catch(function(err){{
        Array.prototype.forEach.call(mNodes, function(el){{
          if(!el.querySelector('svg'))
            el.innerHTML = '<pre style=""color:#c00;white-space:pre-wrap"">Mermaid error: ' + String(err && err.message || err).replace(/</g,'&lt;') + '</pre>';
        }});
      }}));
    }}
  }}
  // Resolves when everything that can change layout height has settled
  // (webfonts, images, async diagram rendering). The PDF export waits on
  // this before measuring blocks for pagination decisions.
  Array.prototype.forEach.call(document.images, function(img) {{
    if (img.decode) window.__mdReadyJobs.push(img.decode().catch(function() {{}}));
  }});
  if (document.fonts && document.fonts.ready) window.__mdReadyJobs.push(document.fonts.ready);
  window.__mdPrintReady = Promise.allSettled(window.__mdReadyJobs);
</script>
</body>
</html>";
}
