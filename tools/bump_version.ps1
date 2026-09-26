# project.godot의 application/config/version 패치 번호를 1 올리고, 새 버전을 tools\last_version.txt에 적습니다.
$path = Join-Path $PSScriptRoot "..\project.godot" | Resolve-Path
$text = [IO.File]::ReadAllText($path)
$match = [regex]::Match($text, 'config/version="(\d+)\.(\d+)\.(\d+)"')
if ($match.Success) {
    $version = "{0}.{1}.{2}" -f $match.Groups[1].Value, $match.Groups[2].Value, ([int]$match.Groups[3].Value + 1)
    $text = $text.Remove($match.Index, $match.Length).Insert($match.Index, "config/version=""$version""")
} else {
    $version = "0.1.0"
    $text = $text -replace '(config/name="[^"]*")', "`$1`nconfig/version=""$version"""
}
[IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding $false))
[IO.File]::WriteAllText((Join-Path $PSScriptRoot "last_version.txt"), $version)
Write-Output "version=$version"
