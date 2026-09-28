// 판 테마마다 테이블 천 위에 깔리는 무늬입니다. (ThemeBackdrop이 화면 전체 ColorRect에 씌웁니다)
// 에셋 없이 코드로 그려서, 테마가 바뀌면 mode만 바꾸면 됩니다.
// mode: 0 없음 · 1 미니게임 대회(네온 격자) · 2 빙고(빙고공 물방울) · 3 레이스(차선 · 체크무늬)
//       4 영토(육각 지도) · 5 비밀 임무(레이더) · 6 보스(용암 균열) · 7 시한폭탄(경고 줄무늬)
shader_type canvas_item;

uniform int mode = 0;
uniform vec2 rect_size = vec2(1280.0, 720.0);
uniform vec4 accent : source_color = vec4(1.0, 0.8, 0.3, 1.0);
uniform vec4 accent2 : source_color = vec4(0.3, 0.8, 1.0, 1.0);
// 0~1, 테마가 바뀔 때 서서히 나타납니다.
uniform float strength = 1.0;
// 시한폭탄이 곧 터질 때 1
uniform float danger = 0.0;

float hash(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

vec2 hash2(vec2 p) {
	return vec2(hash(p), hash(p + 17.13));
}

float line(float d, float w) {
	return 1.0 - smoothstep(w * 0.5 - 0.8, w * 0.5 + 0.8, abs(d));
}

// 보로노이: x = 가장 가까운 점까지 거리, y = 두 번째와의 차이(균열 선)
vec2 voronoi(vec2 p) {
	vec2 cell = floor(p);
	float d1 = 8.0;
	float d2 = 8.0;
	for (int y = -1; y <= 1; y++) {
		for (int x = -1; x <= 1; x++) {
			vec2 o = vec2(float(x), float(y));
			vec2 pt = o + hash2(cell + o);
			float d = length(pt - fract(p));
			if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) { d2 = d; }
		}
	}
	return vec2(d1, d2 - d1);
}

vec4 arcade(vec2 p, float t) {
	// 아래쪽에서 원근감 있게 뻗어 나가는 네온 격자 + 은은한 주사선
	vec2 uv = p / rect_size;
	float horizon = 0.42;
	vec4 c = vec4(0.0);
	if (uv.y > horizon) {
		float depth = (uv.y - horizon) / (1.0 - horizon);
		float z = 1.0 / max(depth, 0.02);
		float gx = (uv.x - 0.5) * z * 6.0;
		float gy = z * 1.4 + t * 0.8;
		float dx = min(fract(gx), 1.0 - fract(gx));
		float dy = min(fract(gy), 1.0 - fract(gy));
		float lx = 1.0 - smoothstep(0.0, fwidth(gx) * 1.5, dx);
		float ly = 1.0 - smoothstep(0.0, fwidth(gy) * 1.5, dy);
		float grid = max(lx, ly) * smoothstep(0.0, 0.25, depth);
		c = mix(accent, accent2, uv.x) * grid * 0.5;
	}
	float scan = 0.5 + 0.5 * sin(p.y * 1.6);
	c += vec4(accent2.rgb, 1.0) * 0.03 * scan;
	return c;
}

vec4 bingo(vec2 p, float t) {
	// 비스듬히 흘러가는 빙고공 물방울 무늬
	vec2 q = (p + vec2(t * 12.0, t * 6.0)) / 90.0;
	vec2 cell = floor(q);
	vec2 f = fract(q) - 0.5;
	float h = hash(cell);
	float r = 0.26 + 0.08 * h;
	float d = length(f) * 90.0;
	float ball = 1.0 - smoothstep(r * 90.0 - 1.0, r * 90.0 + 1.0, d);
	float inner = 1.0 - smoothstep(r * 45.0 - 1.0, r * 45.0 + 1.0, d);
	vec4 col = h < 0.5 ? accent : accent2;
	return col * (ball - inner * 0.6) * 0.13;
}

vec4 race(vec2 p, float t) {
	// 가로 차선이 왼쪽으로 흘러가고, 양쪽 끝에 체크무늬 결승선이 있습니다.
	vec2 uv = p / rect_size;
	float lane = fract(uv.y * 5.0);
	float dash = step(0.5, fract((p.x + t * 160.0) / 60.0));
	float lanes = line((lane - 0.5) * rect_size.y / 5.0, 3.0) * dash * 0.35;
	vec2 ck = floor(p / 18.0);
	float checker = mod(ck.x + ck.y, 2.0);
	float band = step(uv.x, 0.03) + step(0.97, uv.x);
	vec4 c = accent * lanes;
	c += mix(vec4(0.0, 0.0, 0.0, 0.55), vec4(1.0, 1.0, 1.0, 0.45), checker) * band;
	return c;
}

vec4 territory(vec2 p, float t) {
	// 육각 격자 지도 + 크게 나뉜 네 영역의 은은한 색
	vec2 q = p / 46.0;
	vec2 r = vec2(1.0, 1.7320508);
	vec2 h = r * 0.5;
	vec2 a = mod(q, r) - h;
	vec2 b = mod(q - h, r) - h;
	vec2 g = dot(a, a) < dot(b, b) ? a : b;
	vec2 ag = abs(g);
	float edge = max(dot(ag, normalize(vec2(1.0, 1.7320508))), ag.x);
	float hexLine = smoothstep(0.44, 0.5, edge);
	vec2 v = voronoi(p / 380.0);
	float border = 1.0 - smoothstep(0.0, 0.04, v.y);
	float region = hash(floor(p / 380.0 + v.x));
	vec4 tint = region < 0.25 ? vec4(0.9, 0.35, 0.3, 1.0) : region < 0.5 ? vec4(0.5, 0.4, 0.9, 1.0) : region < 0.75 ? vec4(0.3, 0.8, 0.5, 1.0) : vec4(0.3, 0.7, 0.95, 1.0);
	return accent * hexLine * 0.18 + tint * 0.05 + accent2 * border * 0.25;
}

vec4 mission(vec2 p, float t) {
	// 점 격자 + 가운데에서 도는 레이더
	vec2 grid = mod(p, 28.0) - 14.0;
	float dots = 1.0 - smoothstep(1.0, 2.2, length(grid));
	vec2 c = rect_size * vec2(0.37, 0.45);
	vec2 d = p - c;
	float dist = length(d);
	float ang = atan(d.y, d.x);
	float sweep = fract((ang / 6.2831853) - t * 0.12);
	float beam = pow(1.0 - sweep, 10.0) * (1.0 - smoothstep(0.0, 520.0, dist));
	float rings = line(mod(dist, 120.0) - 60.0, 2.0) * (1.0 - smoothstep(200.0, 560.0, dist));
	return accent * (dots * 0.18 + beam * 0.35 + rings * 0.14);
}

vec4 boss(vec2 p, float t) {
	// 용암 균열: 균열 선이 붉게 숨 쉬듯 빛납니다.
	vec2 v = voronoi(p / 110.0);
	float crack = 1.0 - smoothstep(0.0, 0.06, v.y);
	float pulse = 0.65 + 0.35 * sin(t * 2.0 + hash(floor(p / 110.0)) * 6.0);
	float ember = step(0.996, hash(floor(p / 6.0) + floor(t * 3.0)));
	return mix(accent, accent2, 1.0 - v.y * 3.0) * crack * pulse * 0.42 + accent2 * ember * 0.5;
}

vec4 bomb(vec2 p, float t) {
	// 위아래 노랑·검정 경고 줄무늬, 곧 터질 땐 화면 가장자리가 붉게 깜빡입니다.
	vec2 uv = p / rect_size;
	float stripe = step(0.5, fract((p.x + p.y) / 40.0 + t * 0.2));
	float band = step(uv.y, 0.018) + step(0.982, uv.y);
	vec4 c = mix(vec4(0.05, 0.05, 0.05, 0.6), vec4(accent.rgb, 0.55), stripe) * band;
	float edge = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
	float glow = (1.0 - smoothstep(0.0, 0.18, edge)) * danger * (0.5 + 0.5 * sin(t * 12.0));
	c += vec4(1.0, 0.15, 0.1, 1.0) * glow * 0.35;
	float spark = step(0.997, hash(floor(p / 5.0) + floor(t * 6.0)));
	c += accent2 * spark * 0.35;
	return c;
}

void fragment() {
	vec2 p = UV * rect_size;
	float t = TIME;
	vec4 c = vec4(0.0);
	if (mode == 1) { c = arcade(p, t); }
	else if (mode == 2) { c = bingo(p, t); }
	else if (mode == 3) { c = race(p, t); }
	else if (mode == 4) { c = territory(p, t); }
	else if (mode == 5) { c = mission(p, t); }
	else if (mode == 6) { c = boss(p, t); }
	else if (mode == 7) { c = bomb(p, t); }
	c = clamp(c, 0.0, 1.0);
	// 알파는 색의 밝기를 따라갑니다. (검정 줄무늬처럼 알파를 직접 준 곳은 그대로)
	float a = max(c.a, max(c.r, max(c.g, c.b)));
	COLOR = vec4(c.rgb / max(a, 0.001), a * strength);
}
