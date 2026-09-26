# 개발용: 바로 전에 실행한 게임의 로그(godot.log)를 tools\prev_godot.log로 복사하고 오류 줄만 모아 둡니다.
$logs = Join-Path $env:APPDATA "Godot\app_userdata\SP_Cardgame\logs"
$prev = Get-ChildItem $logs -Filter *.log | Sort-Object LastWriteTime -Descending | Select-Object -Skip 1 -First 1
Copy-Item $prev.FullName (Join-Path $PSScriptRoot "prev_godot.log") -Force
Select-String -Path $prev.FullName -Pattern "ERROR|SCRIPT ERROR|Exception|WARNING" | ForEach-Object { $_.Line } | Set-Content (Join-Path $PSScriptRoot "prev_errors.txt")
"$($prev.Name) $($prev.LastWriteTime)"
