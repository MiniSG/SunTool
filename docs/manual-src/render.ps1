param([string]$Pptx, [string]$OutDir)
# PowerPoint COM 으로 각 슬라이드를 PNG 로 내보냅니다.
New-Item -ItemType Directory -Force $OutDir | Out-Null
Get-ChildItem $OutDir -Filter "slide-*.png" | Remove-Item -Force
$app = New-Object -ComObject PowerPoint.Application
try {
    $pres = $app.Presentations.Open($Pptx, $true, $false, $false)  # ReadOnly, Untitled, WithWindow=false
    $i = 1
    foreach ($slide in $pres.Slides) {
        $slide.Export((Join-Path $OutDir ("slide-{0:D2}.png" -f $i)), "PNG", 1600, 900)
        $i++
    }
    $pres.Close()
    "exported $($i - 1) slides"
}
finally {
    $app.Quit()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($app)
}
