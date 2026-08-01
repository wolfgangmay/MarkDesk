# Downloads all vendored front-end assets for MarkDesk into src/MarkDesk/Assets/web/vendor.
# Versions must match the ones referenced in src/MarkDesk/Services/PreviewTemplate.cs.
# Usage:  .\scripts\fetch-vendor-assets.ps1 [-Force]
param(
    [switch]$Force
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$vendor = Join-Path $root 'src\MarkDesk\Assets\web\vendor'
New-Item -ItemType Directory -Force -Path `
    "$vendor\highlight", "$vendor\katex\fonts", "$vendor\mermaid" | Out-Null

function Get-File {
    param([string]$Uri, [string]$OutFile)
    if ((Test-Path $OutFile) -and -not $Force) {
        Write-Host "  skip  $(Split-Path $OutFile -Leaf)"
        return
    }
    Write-Host "  fetch $Uri"
    Invoke-WebRequest -Uri $Uri -OutFile $OutFile
    $size = (Get-Item $OutFile).Length
    if ($size -eq 0) { throw "Empty download: $OutFile" }
    Write-Host "        -> $size bytes"
}

# highlight.js 11.9.0 (BSD-3-Clause) - cdnjs
Write-Host 'highlight.js 11.9.0'
Get-File 'https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/highlight.min.js' "$vendor\highlight\highlight.min.js"
Get-File 'https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/styles/github.min.css' "$vendor\highlight\github.min.css"
Get-File 'https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/styles/github-dark.min.css' "$vendor\highlight\github-dark.min.css"

# KaTeX 0.16.11 (MIT) - jsDelivr
Write-Host 'KaTeX 0.16.11'
Get-File 'https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/katex.min.css' "$vendor\katex\katex.min.css"
Get-File 'https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/katex.min.js' "$vendor\katex\katex.min.js"
Get-File 'https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/contrib/auto-render.min.js' "$vendor\katex\auto-render.min.js"

$css = Get-Content "$vendor\katex\katex.min.css" -Raw
$fonts = [regex]::Matches($css, 'fonts/([\w-]+\.woff2)') |
    ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
foreach ($font in $fonts) {
    Get-File "https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/fonts/$font" "$vendor\katex\fonts\$font"
}

# Mermaid 11.4.1 (MIT) - jsDelivr
Write-Host 'Mermaid 11.4.1'
Get-File 'https://cdn.jsdelivr.net/npm/mermaid@11.4.1/dist/mermaid.min.js' "$vendor\mermaid\mermaid.min.js"

Write-Host 'All vendor assets up to date.'
