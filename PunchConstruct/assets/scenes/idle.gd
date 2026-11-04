extends Node
func _ready():
    get_parent().get_animation_state().set_animation("idle_loop", true, 0)
