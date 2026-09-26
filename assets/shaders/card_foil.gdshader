// 카드 앞면 위에 겹치는 빛 효과입니다. (CardView가 앞면 안쪽 크기의 ColorRect에 이 셰이더를 씌웁니다)
// - 프리즘 카드: 무지개 띠가 대각선으로 흐르고, 밝은 빛줄기가 지나갑니다. 마우스를 올리면 마우스 위치를 따라 색이 움직입니다.
// - 특수 카드(gloss_only = 1): 무지개 없이 흰 광택만 주기적으로 한 번씩 지나갑니다.
shader_type canvas_item;

uniform float mouse_shift = 0.0;
uniform float strength = 0.25;
uniform float gloss_only = 0.0;
uniform float seed = 0.0;
uniform vec2 rect_size = vec2(100.0, 150.0);
uniform float corner_radius = 6.0;

vec3 hsv2rgb(vec3 c) {
	vec4 k = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
	vec3 p = abs(fract(c.xxx + k.xyz) * 6.0 - k.www);
	return c.z * mix(k.xxx, clamp(p - k.xxx, 0.0, 1.0), c.y);
}

// 둥근 모서리 밖은 그리지 않도록 가장자리를 부드럽게 잘라 냅니다.
float rounded_mask(vec2 uv) {
	vec2 p = uv * rect_size;
	vec2 half_size = rect_size * 0.5;
	vec2 q = abs(p - half_size) - (half_size - vec2(corner_radius));
	float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - corner_radius;
	return clamp(0.5 - d, 0.0, 1.0);
}

void fragment() {
	float t = TIME + seed;
	// 대각선 위치 (왼쪽 위 0 → 오른쪽 아래 1)
	float diag = (UV.x * rect_size.x + UV.y * rect_size.y * 0.55) / (rect_size.x + rect_size.y * 0.55);

	float rainbow_alpha = strength * (1.0 - gloss_only);
	vec3 rainbow = hsv2rgb(vec3(fract(diag * 1.4 + t * 0.12 + mouse_shift), 0.6, 1.0));

	float shine;
	if (gloss_only > 0.5) {
		float phase = mod(t, 3.4) / 1.1;
		float center = -0.2 + phase * 1.4;
		shine = phase <= 1.0 ? 0.22 * exp(-pow((diag - center) / 0.05, 2.0)) : 0.0;
	} else {
		float sweep = mod(t * 0.35 + mouse_shift, 1.6) - 0.3;
		shine = 0.42 * exp(-pow((diag - sweep) / 0.06, 2.0));
	}

	float alpha = rainbow_alpha + shine;
	vec3 color = (rainbow * rainbow_alpha + vec3(1.0) * shine) / max(alpha, 0.0001);
	COLOR = vec4(color, clamp(alpha, 0.0, 1.0) * rounded_mask(UV));
}
