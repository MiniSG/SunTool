# SunTool 포터블 단일 exe 빌드
# 사용: PowerShell 에서  .\publish.ps1
# 결과: .\publish\SunTool.exe  (이 파일 하나만 복사하면 .NET 설치 없이 실행됩니다)
#
# 버전: 2026.11.9.N  - N 은 같은 날 몇 번째 빌드인지 (같은 날 다시 실행하면 1씩 증가)
#       -BuildDate / -BuildNumber 로 직접 지정할 수 있습니다 (GitHub Actions 릴리스가 사용).
param(
    [string]$BuildDate = '',    # 예: 2026.11.09 (비우면 오늘)
    [int]$BuildNumber = 0       # 0 이면 .build-number 파일로 자동 증가
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root 'publish'

if (-not $BuildDate) { $BuildDate = (Get-Date).ToString('yyyy.MM.dd') }
if ($BuildNumber -le 0) {
    $counterFile = Join-Path $root '.build-number'
    $BuildNumber = 1
    if (Test-Path $counterFile) {
        $parts = (Get-Content $counterFile -Raw).Trim() -split '\|'
        if ($parts.Count -eq 2 -and $parts[0] -eq $BuildDate) { $BuildNumber = [int]$parts[1] + 1 }
    }
    Set-Content $counterFile "$BuildDate|$BuildNumber" -Encoding ASCII
}
"버전: $BuildDate (그날 $BuildNumber 번째 빌드)"

dotnet publish (Join-Path $root 'src\SunTool.App\SunTool.App.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded `
    -p:SunToolBuildDate=$BuildDate `
    -p:SunToolBuildNumber=$BuildNumber `
    -o $out

if ($LASTEXITCODE -ne 0) { throw "publish 실패 (exit $LASTEXITCODE)" }

Get-ChildItem $out -Filter *.exe | ForEach-Object {
    "{0}  ({1:N1} MB, 파일 버전 {2})" -f $_.FullName, ($_.Length / 1MB), $_.VersionInfo.FileVersion
}