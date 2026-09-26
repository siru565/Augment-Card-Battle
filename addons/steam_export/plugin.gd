@tool
extends EditorPlugin

## 내보내기(Export)가 끝나면 Steam 실행에 필요한 파일을 exe와 같은 폴더에 복사합니다.
## steam_api64.dll은 리소스가 아니라서 Godot가 pck에 넣지 않기 때문에 따로 옮겨야 합니다.

var _exporter: SteamFilesExporter


func _enter_tree() -> void:
	_exporter = SteamFilesExporter.new()
	add_export_plugin(_exporter)


func _exit_tree() -> void:
	remove_export_plugin(_exporter)
	_exporter = null


class SteamFilesExporter extends EditorExportPlugin:
	const FILES := ["steam_api64.dll", "steam_appid.txt"]

	var _target_dir := ""

	func _get_name() -> String:
		return "SteamFilesExporter"

	func _export_begin(features: PackedStringArray, is_debug: bool, path: String, flags: int) -> void:
		# Windows 내보내기일 때만 복사합니다.
		if not features.has("windows"):
			_target_dir = ""
			return
		var absolute := path
		if path.is_relative_path():
			absolute = ProjectSettings.globalize_path("res://").path_join(path)
		_target_dir = absolute.get_base_dir()

	func _export_end() -> void:
		if _target_dir == "":
			return
		for file_name in FILES:
			var source := ProjectSettings.globalize_path("res://" + file_name)
			if not FileAccess.file_exists(source):
				push_warning("[Steam Export] %s 파일이 없어서 복사하지 못했습니다." % file_name)
				continue
			var error := DirAccess.copy_absolute(source, _target_dir.path_join(file_name))
			if error != OK:
				push_warning("[Steam Export] %s 복사 실패: %s" % [file_name, error_string(error)])
			else:
				print("[Steam Export] %s → %s" % [file_name, _target_dir])
