# SunTool 포터블 단일 exe 빌드
# 사용: PowerShell 에서  .\publish.ps1
# 결과: .\publish\SunTool.exe  (이 파일 하나만 복사하면 .NET 설치 없이 실행됩니다)

param([string]$BuildDate = '')   # 예: 2026.11.09 (비우면 오늘 날짜)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root 'publish'

dotnet publish (Join-Path $root 'src\SunTool.App\SunTool.App.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded `
    -o $out `
    @(if ($BuildDate) { "-p:SunToolBuildDate=$BuildDate" })

if ($LASTEXITCODE -ne 0) { throw "publish 실패 (exit $LASTEXITCODE)" }

Get-ChildItem $out -Filter *.exe | ForEach-Object {
    "{0}  ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB)
}
