extends Control
## A separate, quiet background and a locally deforming foreground portrait.
## Only this scene owns the animation clock; hiding/freeing it stops the work.

const DESIGN_SIZE := Vector2(1920.0, 1080.0)
const COLUMNS := 64
const ROWS := 36

class PortraitParticles extends Node2D:
	var phase: float = 0.0
	var warm: bool = false
	var count: int = 22

	func _draw() -> void:
		for index in range(count):
			var progress: float = fposmod(phase / (2.4 if warm else 9.6) + index * 0.618033989, 1.0)
			var fade: float = pow(sin(progress * PI), 2.0)
			var point: Vector2
			var tint: Color
			var radius: float
			if warm:
				var wrist := Vector2(795.0, 790.0) if index % 2 == 0 else Vector2(1320.0, 1000.0)
				point = wrist + Vector2(sin(index * 3.7) * (18.0 + progress * 28.0), -progress * 108.0)
				tint = Color(1.0, 0.73, 0.29, fade * 0.78)
				radius = 1.2 + float(index % 3) * 0.45
			else:
				point = Vector2(45.0 + fposmod(index * 147.3, 1800.0) + sin(progress * TAU + index) * 21.0, 1150.0 - progress * 1290.0)
				tint = Color(0.33, 0.77, 0.9, fade * (0.30 + float(index % 3) * 0.1))
				radius = 1.5 + float(index % 4) * 0.55
			draw_circle(point, radius * 2.8, Color(tint.r, tint.g, tint.b, tint.a * 0.10))
			draw_circle(point, radius, tint)
			if warm:
				draw_line(point + Vector2(0.0, 1.0), point + Vector2(0.0, 5.0), Color(tint.r, tint.g, tint.b, tint.a * 0.45), 1.0)

var animation_time: float = 0.0
var animation_ticks: int = 0
var animation_ready: bool = false
var preview_frozen: bool = false
var _canvas: Node2D
var _material: ShaderMaterial
var _ambient: PortraitParticles
var _sparks: PortraitParticles


func _ready() -> void:
	var backdrop := load("res://PunchConstruct/art/character_select_background.ctex") as Texture2D
	var actor := load("res://PunchConstruct/art/character_select_actor.ctex") as Texture2D
	var shader := load("res://PunchConstruct/scenes/character_select_motion.gdshader") as Shader
	if backdrop == null or actor == null or shader == null:
		push_error("[PunchConstruct] Animated selection resources missing; retaining static poster.")
		set_process(false)
		return
	_canvas = Node2D.new()
	_canvas.name = "AnimatedPortrait"
	# Keep the native drawing order so the name, description and buttons stay
	# above this canvas. _layout compensates its parent's overscan transform.
	add_child(_canvas)
	var background := Sprite2D.new()
	background.name = "Backdrop"
	background.texture = backdrop
	background.centered = false
	_canvas.add_child(background)
	_ambient = PortraitParticles.new()
	_ambient.name = "HarborMotes"
	_canvas.add_child(_ambient)
	var foreground := MeshInstance2D.new()
	foreground.name = "ConstructPortrait"
	foreground.texture = actor
	foreground.mesh = _make_mesh()
	_material = ShaderMaterial.new()
	_material.shader = shader
	_material.set_shader_parameter("phase", 0.0)
	foreground.material = _material
	_canvas.add_child(foreground)
	_sparks = PortraitParticles.new()
	_sparks.name = "GloveSparks"
	_sparks.warm = true
	_sparks.count = 12
	_canvas.add_child(_sparks)
	resized.connect(_layout)
	get_viewport().size_changed.connect(_layout)
	_layout()
	$Poster.hide()
	animation_ready = true
	set_process(true)


func _make_mesh() -> ArrayMesh:
	var vertices := PackedVector3Array()
	var uv := PackedVector2Array()
	var indices := PackedInt32Array()
	for y in range(ROWS + 1):
		for x in range(COLUMNS + 1):
			var point := Vector2(float(x) / COLUMNS, float(y) / ROWS)
			vertices.append(Vector3(point.x * DESIGN_SIZE.x, point.y * DESIGN_SIZE.y, 0.0))
			uv.append(point)
	for y in range(ROWS):
		for x in range(COLUMNS):
			var first := y * (COLUMNS + 1) + x
			indices.append_array(PackedInt32Array([first, first + 1, first + COLUMNS + 1,
				first + 1, first + COLUMNS + 2, first + COLUMNS + 1]))
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_TEX_UV] = uv
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


func _layout() -> void:
	if _canvas == null:
		return
	var extent := get_viewport_rect().size
	var cover: float = maxf(extent.x / DESIGN_SIZE.x, extent.y / DESIGN_SIZE.y)
	_canvas.global_transform = Transform2D(Vector2(cover, 0.0), Vector2(0.0, cover), (extent - DESIGN_SIZE * cover) * 0.5)


func _process(delta: float) -> void:
	if not animation_ready or not is_visible_in_tree() or preview_frozen:
		return
	_layout()
	# No global TIME: inactive selections stop and each scene owns its material.
	animation_time += minf(delta, 0.1)
	animation_ticks += 1
	_material.set_shader_parameter("phase", animation_time)
	_update_particles()


func _update_particles() -> void:
	for particles in [_ambient, _sparks]:
		if particles != null:
			particles.phase = animation_time
			particles.queue_redraw()


func set_preview_time(seconds: float) -> void:
	preview_frozen = true
	animation_time = maxf(seconds, 0.0)
	if _material != null:
		_material.set_shader_parameter("phase", animation_time)
	_update_particles()


func resume_animation() -> void:
	preview_frozen = false


func animation_state() -> Dictionary:
	return {"ready": animation_ready, "time": animation_time, "ticks": animation_ticks,
		"visible": is_visible_in_tree(), "frozen": preview_frozen,
		"particles": 34 if animation_ready else 0,
		"canvas_scale": _canvas.scale.x if _canvas != null else 0.0}
