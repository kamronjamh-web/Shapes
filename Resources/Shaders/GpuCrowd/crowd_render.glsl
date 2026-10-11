#[compute]
#version 450
#include "crowd_world.glslinc"

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;
struct RenderBody {
    vec4 position_radius;
    vec4 previous_height;
    vec4 color;
    vec4 previous_quaternion;
    vec4 quaternion;
    vec4 misc; // yaw, flash seconds remaining at input upload, role, role index
};
struct RenderInstance { vec4 row0; vec4 row1; vec4 row2; vec4 color; vec4 custom; };
layout(set = 0, binding = 0, std430) readonly buffer Bodies { RenderBody bodies[]; };
layout(set = 0, binding = 1, std430) writeonly buffer Cubes { RenderInstance cubes[]; };
layout(set = 0, binding = 2, std430) writeonly buffer Pyramids { RenderInstance pyramids[]; };
layout(push_constant, std430) uniform Params {
    vec4 local_row0;
    vec4 local_row1;
    vec4 local_row2;
    vec4 control; // interpolation blend, elapsed input seconds, rolling enabled, live count
    vec4 capacities; // cube allocation, pyramid allocation, reserved, reserved
} p;

// Match Godot Quaternion.Slerp: shortest path and an unnormalized linear
// fallback for near-identical normalized quaternions. Basis normalizes by |q|².
vec4 godot_slerp(vec4 from, vec4 to, float weight) {
    float cosine = dot(from, to);
    if (cosine < 0.0) { cosine = -cosine; to = -to; }
    float scale0 = 1.0 - weight, scale1 = weight;
    if (1.0 - cosine > 0.00001) {
        float omega = acos(clamp(cosine, -1.0, 1.0));
        float sine = sin(omega);
        scale0 = sin((1.0 - weight) * omega) / sine;
        scale1 = sin(weight * omega) / sine;
    }
    return scale0 * from + scale1 * to;
}
mat3 godot_quaternion_basis(vec4 q) {
    float s = 2.0 / dot(q, q);
    float xs = q.x * s, ys = q.y * s, zs = q.z * s;
    float wx = q.w * xs, wy = q.w * ys, wz = q.w * zs;
    float xx = q.x * xs, xy = q.x * ys, xz = q.x * zs;
    float yy = q.y * ys, yz = q.y * zs, zz = q.z * zs;
    return mat3(vec3(1.0 - (yy + zz), xy + wz, xz - wy),
                vec3(xy - wz, 1.0 - (xx + zz), yz + wx),
                vec3(xz + wy, yz - wx, 1.0 - (xx + yy)));
}
vec3 local_vector(vec3 v) {
    return vec3(world_dot(p.local_row0.xyz, v), world_dot(p.local_row1.xyz, v), world_dot(p.local_row2.xyz, v));
}
void main() {
    uint index = gl_GlobalInvocationID.x;
    if (index >= uint(p.control.w)) return;
    RenderBody body = bodies[index];
    bool pyramid = body.misc.z != 0.0;
    uint role_index = uint(body.misc.w);
    if (role_index >= uint(pyramid ? p.capacities.y : p.capacities.x)) return;
    float blend = clamp(p.control.x, 0.0, 1.0);
    precise vec3 feet = body.previous_height.xyz + (body.position_radius.xyz - body.previous_height.xyz) * blend;
    float c = cos(body.misc.x), s = sin(body.misc.x);
    mat3 orientation = mat3(vec3(c, 0.0, -s), vec3(0.0, 1.0, 0.0), vec3(s, 0.0, c));
    if (p.control.z != 0.0 && !pyramid)
        orientation = godot_quaternion_basis(godot_slerp(body.previous_quaternion, body.quaternion, blend)) * orientation;
    mat3 scaled = mat3(orientation[0] * (body.position_radius.w * 2.0),
                       orientation[1] * body.previous_height.w,
                       orientation[2] * (body.position_radius.w * 2.0));
    float half_height = (abs(scaled[0].y) + abs(scaled[1].y) + abs(scaled[2].y)) * 0.5;
    float visual_radius = 0.5 * max(abs(scaled[0].x) + abs(scaled[1].x) + abs(scaled[2].x),
                                   abs(scaled[0].z) + abs(scaled[1].z) + abs(scaled[2].z));
    feet.y = min(feet.y, world_max_foot(feet, visual_radius, half_height * 2.0));
    vec3 origin = local_vector(feet + vec3(0.0, half_height, 0.0)) + vec3(p.local_row0.w, p.local_row1.w, p.local_row2.w);
    vec3 x = local_vector(scaled[0]), y = local_vector(scaled[1]), z = local_vector(scaled[2]);
    RenderInstance instance;
    instance.row0 = vec4(x.x, y.x, z.x, origin.x);
    instance.row1 = vec4(x.y, y.y, z.y, origin.y);
    instance.row2 = vec4(x.z, y.z, z.z, origin.z);
    instance.color = body.color;
    instance.custom = vec4(body.misc.y - max(0.0, p.control.y) > 0.0 ? 1.0 : 0.0, 0.0, 0.0, 1.0);
    if (pyramid) pyramids[role_index] = instance;
    else cubes[role_index] = instance;
}
