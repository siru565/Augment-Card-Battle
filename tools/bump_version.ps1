# project.godot의 application/config/version 패치 번호를 1 올리고, 새 버전을 tools\last_version.txt에 적습니다.
# 환경 변수 NEXT_VERSION이 있으면 그 버전으로 바로 바꿉니다. (예: set NEXT_VERSION=0.2.0)
$path = Join-Path $PSScriptRoot "..\project.godot" | Resolve-Path
$text = [IO.File]::ReadAllText($path)
$match = [regex]::Match($text, 'config/version="(\d+)\.(\d+)\.(\d+)"')
if ($env:NEXT_VERSION -match '^\d+\.\d+\.\d+$' -and $match.Success) {
    $version = $env:NEXT_VERSION
    $text = $text.Remove($match.Index, $match.Length).Insert($match.Index, "config/version=""$version""")
} elseif ($match.Success) {
    $version = "{0}.{1}.{2}" -f $match.Groups[1].Value, $match.Groups[2].Value, ([int]$match.Groups[3].Value + 1)
    $text = $text.Remove($match.Index, $match.Length).Insert($match.Index, "config/version=""$version""")
} else {
    $version = "0.1.0"
    $text = $text -replace '(config/name="[^"]*")', "`$1`nconfig/version=""$version"""
}
[IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding $false))
[IO.File]::WriteAllText((Join-Path $PSScriptRoot "last_version.txt"), $version)
Write-Output "version=$version"
