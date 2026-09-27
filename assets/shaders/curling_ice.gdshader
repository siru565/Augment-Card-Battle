// 컬링 얼음판입니다. (MinigameStage가 시트 크기의 ColorRect에 씌웁니다)
// 선과 원을 픽셀로 긋지 않고 거리 계산(SDF) + smoothstep으로 그려서, 가장자리가 계단 없이 매끄럽습니다.
// 얼음 위의 자잘한 알갱이(페블)와 은은한 반사도 여기서 그립니다.
shader_type canvas_item;

uniform vec2 rect_size = vec2(315.0, 410.0);
// 하우스 가운데 (픽셀, 이 사각형 기준)
uniform vec2 house_center = vec2(157.0, 80.0);
uniform float house_radius = 50.0;
uniform float button_radius = 11.0;
// 뒷선 높이 (픽셀)
uniform float back_line_y = 16.0;

float hash(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

// 두께 w인 선(거리 d)의 부드러운 덮개입니다. 1픽셀 폭으로 가장자리를 녹입니다.
float line_mask(float d, float w) {
	return 1.0 - smoothstep(w * 0.5 - 0.75, w * 0.5 + 0.75, abs(d));
}

// 반지름 r인 원 안쪽의 부드러운 덮개입니다.
float disc(float dist, float r) {
	return 1.0 - smoothstep(r - 0.9, r + 0.9, dist);
}

void fragment() {
	vec2 p = UV * rect_size;

	// 얼음 바탕: 위쪽이 살짝 밝은 푸른 흰색 + 잔잔한 알갱이 + 대각선 반사
	vec3 ice = mix(vec3(0.86, 0.91, 0.95), vec3(0.93, 0.96, 0.99), 1.0 - UV.y);
	float pebble = hash(floor(p / 2.0)) * 0.035 - 0.0175;
	ice += pebble;
	float sheen = smoothstep(0.0, 1.0, sin((p.x + p.y) * 0.012) * 0.5 + 0.5) * 0.03;
	ice += sheen;

	// 하우스: 파랑 → 흰색 → 빨강 → 금색 버튼 (안쪽일수록 위에 그립니다)
	float dist = length(p - house_center);
	vec3 color = ice;
	color = mix(color, vec3(0.18, 0.50, 0.84), disc(dist, house_radius));
	color = mix(color, vec3(0.97, 0.98, 1.00), disc(dist, house_radius * 0.68));
	color = mix(color, vec3(0.88, 0.27, 0.24), disc(dist, house_radius * 0.40));
	color = mix(color, vec3(0.94, 0.72, 0.29), disc(dist, button_radius));
	// 버튼 테두리 (흰 선)
	color = mix(color, vec3(1.0), line_mask(dist - button_radius, 2.0) * 0.9);

	// 하우스를 가로지르는 티 라인과 가운데 세로선 (은은하게)
	color = mix(color, vec3(0.35, 0.45, 0.58), line_mask(p.y - house_center.y, 1.5) * 0.35);
	color = mix(color, vec3(0.35, 0.45, 0.58), line_mask(p.x - rect_size.x * 0.5, 1.5) * 0.18);

	// 뒷선
	color = mix(color, vec3(0.22, 0.30, 0.42), line_mask(p.y - back_line_y, 2.5) * 0.8);

	// 시트 가장자리 테두리 (안쪽 그림자처럼 부드럽게)
	float edge = min(min(p.x, rect_size.x - p.x), min(p.y, rect_size.y - p.y));
	color = mix(color, vec3(0.30, 0.45, 0.60), 1.0 - smoothstep(0.0, 3.0, edge));
	color *= mix(0.9, 1.0, smoothstep(0.0, 14.0, edge));

	COLOR = vec4(color, 1.0);
}
