shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_add, world_vertex_coords;

uniform sampler2D pillar_atlas : source_color, filter_linear_mipmap, repeat_disable;
uniform float frames_per_second = 8.0;
uniform float animation_time_override = -1.0;
uniform float opacity : hint_range(0.0, 1.0) = 0.6;
uniform float intensity : hint_range(0.0, 4.0) = 0.8;
uniform float flow_speed : hint_range(0.0, 2.0) = 0.35;

varying vec4 rarity_color;
varying float animation_phase;
varying float surface_kind;
varying float rarity_rank;

void vertex() {
    vec3 center = MODEL_MATRIX[3].xyz;
    vec3 toward_camera = INV_VIEW_MATRIX[3].xyz - center;
    toward_camera.y = 0.0;
    float planar_length = length(toward_camera);
    vec3 right = planar_length > 0.0001
        ? vec3(toward_camera.z, 0.0, -toward_camera.x) / planar_length
        : vec3(1.0, 0.0, 0.0);
    float height = INSTANCE_CUSTOM.y;
    float width = INSTANCE_CUSTOM.z;
    surface_kind = UV2.x;
    rarity_rank = INSTANCE_CUSTOM.w;
    if (surface_kind > 1.5) {
        // Independent horizontal glow, lifted just enough to avoid terrain z-fighting.
        VERTEX = center + vec3((UV.x - 0.5) * width * 1.3, 0.025, (UV.y - 0.5) * width * 1.3);
    } else {
        vec3 forward = vec3(-right.z, 0.0, right.x);
        vec3 crossed_right = (right + forward * surface_kind) * 0.70710678;
        // Frame UV 0.90 is the ground flare: preserve the supplied atlas anchor.
        VERTEX = center + crossed_right * (UV.x - 0.5) * width
            + vec3(0.0, (0.90 - UV.y) * height, 0.0);
    }
    rarity_color = COLOR;
    animation_phase = INSTANCE_CUSTOM.x;
}

void fragment() {
    float animation_time = animation_time_override >= 0.0 ? animation_time_override : TIME;
    if (surface_kind > 1.5) {
        float radius = length((UV - vec2(0.5)) * 2.0);
        float glow = exp(-radius * radius * 5.5) * (1.0 - smoothstep(0.6, 1.0, radius));
        ALBEDO = mix(rarity_color.rgb, vec3(1.0), exp(-radius * radius * 35.0) * 0.45) * intensity;
        ALPHA = glow * (0.16 + rarity_rank * 0.018) * rarity_color.a;
    } else {
    float frame = mod(animation_time * frames_per_second + animation_phase * 6.0, 6.0);
    float current = floor(frame);
    float next = mod(current + 1.0, 6.0);
    // Keep bilinear taps inside each cell rather than sampling the adjacent frame.
    vec2 texel = 1.0 / vec2(textureSize(pillar_atlas, 0));
    float cell_x = clamp(UV.x / 6.0, texel.x * 0.5, 1.0 / 6.0 - texel.x * 0.5);
    float cell_y = clamp(UV.y, texel.y * 0.5, 1.0 - texel.y * 0.5);
    vec3 before = texture(pillar_atlas, vec2(current / 6.0 + cell_x, cell_y)).rgb;
    vec3 after = texture(pillar_atlas, vec2(next / 6.0 + cell_x, cell_y)).rgb;
    vec3 beam = mix(before, after, fract(frame));
    float brightness = max(beam.r, max(beam.g, beam.b));
    // The supplied atlas has a black background; black contributes no light.
    float flowing_y = UV.y + animation_time * flow_speed + animation_phase;
    float energy = 0.78 + 0.22 * sin(flowing_y * 32.0 + sin(UV.x * 25.0) * 1.7);
    float core = exp(-abs(UV.x - 0.5) * 28.0) * smoothstep(0.02, 0.25, brightness);
    float soft_edges = 1.0 - smoothstep(0.3, 0.5, abs(UV.x - 0.5));
    ALBEDO = mix(rarity_color.rgb, vec3(1.0), core * 0.95) * intensity * (1.0 + core * 0.65);
    ALPHA = brightness * energy * soft_edges * opacity * rarity_color.a * 0.65;
    }
}
