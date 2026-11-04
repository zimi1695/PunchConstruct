shader_type canvas_item;
render_mode unshaded;

uniform float phase = 0.0;

void vertex() {
	vec2 original = VERTEX;
	float breath = sin(phase * 1.308996939); // 4.8 seconds.
	float torso = smoothstep(0.23, 0.52, UV.x);
	// Keep artwork that is cropped by a canvas edge pinned to that edge.
	float edge = smoothstep(0.0, 0.028, UV.y) * (1.0 - smoothstep(0.968, 1.0, UV.y));
	edge *= 1.0 - smoothstep(0.973, 1.0, UV.x);
	vec2 movement = vec2(breath * 4.5 * (UV.x - 0.63), -breath * 10.5 * (1.0 - UV.y * 0.45)) * torso;
	float head = (1.0 - smoothstep(0.48, 0.63, UV.y)) * smoothstep(0.43, 0.55, UV.x);
	head *= 1.0 - smoothstep(0.81, 0.9, UV.x);
	vec2 neck = vec2(1255.0, 595.0);
	vec2 relative = original - neck;
	float angle = sin(phase * 0.654498469) * 0.014;
	vec2 turned = vec2(relative.x * cos(angle) - relative.y * sin(angle), relative.x * sin(angle) + relative.y * cos(angle));
	movement += ((turned - relative) + vec2(0.0, sin(phase * 0.654498469) * 2.8)) * head;
	float left_guard = 1.0 - smoothstep(0.4, 1.0, length((UV - vec2(0.45, 0.69)) / vec2(0.15, 0.19)));
	float right_guard = 1.0 - smoothstep(0.4, 1.0, length((UV - vec2(0.66, 0.87)) / vec2(0.17, 0.2)));
	movement.y += sin(phase * 1.963495408) * 12.0 * left_guard;
	movement.y -= sin(phase * 1.308996939) * 9.5 * right_guard;
	VERTEX += movement * edge;
}
