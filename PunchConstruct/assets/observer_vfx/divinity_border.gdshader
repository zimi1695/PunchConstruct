// Adapted from Watcher 0.10.3 StanceVfxHelper (Boninall).

shader_type canvas_item;

uniform vec4 border_color : source_color = vec4(0.85, 0.15, 0.85, 0.52);
uniform vec4 border_color_2 : source_color = vec4(1.0, 0.6, 1.0, 0.36);
uniform float border_width : hint_range(0.01, 0.5) = 0.25;
uniform float noise_scale : hint_range(1.0, 30.0) = 8.0;
uniform float noise_speed : hint_range(0.0, 5.0) = 1.2;
uniform float noise_intensity : hint_range(0.0, 1.0) = 0.4;
uniform vec2 noise_direction = vec2(0.0, -0.5);
uniform float pulse_alpha : hint_range(0.0, 1.0) = 1.0;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float value_noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 3; i++) {
        v += a * value_noise(p);
        p *= 2.0;
        a *= 0.5;
    }
    return v;
}

void fragment() {
    vec2 uv = UV;
    float dx = min(uv.x, 1.0 - uv.x);
    float dy = min(uv.y, 1.0 - uv.y);
    float d = min(dx, dy);

    // Animated fractal noise for organic border
    vec2 noise_uv = uv * noise_scale + noise_direction * TIME * noise_speed;
    float n1 = fbm(noise_uv);
    float n2 = fbm(noise_uv * 0.7 - vec2(TIME * noise_speed * 0.4, 0.0));
    float n = (n1 + n2) * 0.5;

    // Distort edge distance with noise for fiery/watery look
    float distorted_d = d + (n - 0.5) * noise_intensity * border_width;

    float vignette = 1.0 - smoothstep(0.0, border_width, distorted_d);
    // Softer inner glow layer
    float inner_glow = 1.0 - smoothstep(0.0, border_width * 1.8, distorted_d);

    // Two-tone color blending driven by noise
    vec3 col = mix(border_color.rgb, border_color_2.rgb, n);
    float base_alpha = mix(border_color.a, border_color_2.a, n * 0.5);

    // Combine sharp border + soft inner glow
    float alpha = max(vignette * base_alpha, inner_glow * base_alpha * 0.3);
    COLOR = vec4(col, alpha * pulse_alpha);
}
