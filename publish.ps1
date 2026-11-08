param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "QuickPeek\QuickPeek.csproj"
$output = Join-Path $root "publish"

if (Test-Path $output) {
    Remove-Item $output -Recurse -Force
}

Write-Host "发布完整版（含 FFmpeg，支持视频/音频预览）..."
dotnet publish $project -c $Configuration -o (Join-Path $output "full")
if ($LASTEXITCODE -ne 0) { throw "完整版发布失败" }

Write-Host "发布精简版（不含 FFmpeg，仅图片/文本预览）..."
dotnet publish $project -c $Configuration -o (Join-Path $output "lite") -p:LiteBuild=true
if ($LASTEXITCODE -ne 0) { throw "精简版发布失败" }

# 防止参数遗漏导致精简版误带 FFmpeg
$liteFfmpeg = Join-Path $output "lite\ffmpeg"
if (Test-Path $liteFfmpeg) {
    Remove-Item $liteFfmpeg -Recurse -Force
}

# 压缩包名与程序集名保持一致：完整版 QuickPeek.zip，精简版 QuickPeekLite.zip
$packageNames = @{ full = "QuickPeek"; lite = "QuickPeekLite" }
foreach ($flavor in @("full", "lite")) {
    $dir = Join-Path $output $flavor
    Copy-Item (Join-Path $root "LICENSE") $dir -Force
    Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.md") $dir -Force

    $zip = Join-Path $output ($packageNames[$flavor] + ".zip")
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $dir "*") -DestinationPath $zip
    Write-Host "已生成 $zip"
}

Write-Host "打包完成：$output"
