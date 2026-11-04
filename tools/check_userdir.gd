extends SceneTree
func _initialize():
    print("PUNCHCONSTRUCT_USERDIR=" + OS.get_user_data_dir())
    print("PUNCHCONSTRUCT_PROJECT=" + str(ProjectSettings.get_setting("application/config/name")))
    quit()
