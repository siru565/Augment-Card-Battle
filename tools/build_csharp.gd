extends Node

## 에디터 밖에서 C# 프로젝트를 빌드하는 개발용 스크립트입니다.
## tools/build_csharp.tscn을 실행하면 dotnet build를 돌리고 결과를 출력한 뒤 종료합니다.

func _ready() -> void:
	var csproj := ProjectSettings.globalize_path("res://SP_Cardgame.csproj")
	var output: Array = []
	var args := PackedStringArray(["build", csproj, "-nologo", "-v:q", "-clp:NoSummary;ErrorsOnly"])
	print("[빌드] dotnet build 시작: ", csproj)
	# PATH에 dotnet이 없을 수 있어서 기본 설치 경로를 먼저 찾습니다.
	var dotnet := "C:/Program Files/dotnet/dotnet.exe"
	if not FileAccess.file_exists(dotnet):
		dotnet = "dotnet"
	var code := OS.execute(dotnet, args, output, true)
	for line in output:
		print(line)
	print("[빌드] 종료 코드: ", code)
	var log_file := FileAccess.open("res://tools/build_log.txt", FileAccess.WRITE)
	if log_file:
		log_file.store_string("exit=%d\n%s" % [code, "\n".join(output)])
		log_file.close()
	get_tree().quit(code)
