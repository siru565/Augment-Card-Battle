extends Node

## 개발용: tools/cmd.txt에 적힌 명령 한 줄을 Windows cmd로 실행하고,
## 결과를 tools/cmd_out.txt에 저장한 뒤 종료합니다. (패키지 설치 확인 등에 사용)

func _ready() -> void:
	var command := FileAccess.get_file_as_string("res://tools/cmd.txt").strip_edges()
	var output: Array = []
	var code := -1
	if command != "":
		code = OS.execute("cmd.exe", PackedStringArray(["/c", command]), output, true)
	var file := FileAccess.open("res://tools/cmd_out.txt", FileAccess.WRITE)
	if file:
		file.store_string("exit=%d\n%s" % [code, "\n".join(output)])
		file.close()
	get_tree().quit()
