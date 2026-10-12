#[compute]
#version 450
#include "crowd_world.glslinc"

// One Jacobi contact pass, with optional fort drive/world and rolling phases.
// Reimport this resource after editing included GLSL files.
// Stronghold broad ramp stages preserve ordinary final-target range behavior.
// Rolling completion accounts for world-coordinate float precision (two ULPs).
layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;
struct Body { vec4 position_radius; vec4 previous_height; vec4 drive_pressure; vec4 world_velocity_alive; };
struct Result { vec4 position_pressure; vec4 velocity_counts; };
layout(set = 0, binding = 0, std430) buffer Bodies { Body bodies[]; };
layout(set = 0, binding = 1, std430) buffer Heads { int heads[]; };
layout(set = 0, binding = 2, std430) buffer Next { int next_slot[]; };
layout(set = 0, binding = 3, std430) buffer Cells { ivec4 cells[]; };
layout(set = 0, binding = 4, std430) buffer Results { Result results[]; };
layout(push_constant, std430) uniform Params {
    uint capacity; uint buckets; uint phase; uint stacking;
    float horizontal_cell; float vertical_cell; float max_radius; float max_height;
    float dt; float stiffness; float climb_step; float max_pile;
    uint budget; float max_push; uint reserved0; uint reserved1;
} p;
#include "crowd_drive.glslinc"

uint bucket(ivec3 cell) {
    return (uint(cell.x) * 73856093u ^ uint(cell.y) * 19349663u ^ uint(cell.z) * 83492791u) & (p.buckets - 1u);
}
float move_toward(float from, float to, float amount) { return from + clamp(to - from, -amount, amount); }
vec3 normalized_or_zero(vec3 v) { float len = length(v); return len > 0.0 ? v / len : vec3(0.0); }

void main() {
    uint slot = gl_GlobalInvocationID.x;
    if (p.phase == 6u || p.phase == 7u) {
        if (slot >= p.capacity) return;
        if (p.phase == 6u) drive_step(slot); else drive_roll(slot);
        return;
    }
    // Movement constraints precede contact-grid construction. Each invocation
    // owns one body; a dispatch barrier publishes resolved inputs to every reader.
    if (p.phase == 5u) {
        if (slot >= p.capacity) return;
        Body body = bodies[slot];
        if (body.world_velocity_alive.w == 0.0 || body.world_velocity_alive.x == 0.0) return;
        vec3 proposed = body.position_radius.xyz;
        vec3 resolved = world_resolve(body.previous_height.xyz, proposed, body.position_radius.w, body.previous_height.w);
        bodies[slot].position_radius.xyz = resolved;
        if (resolved.y > proposed.y + 0.0001) bodies[slot].world_velocity_alive.z = 0.0;
        return;
    }
    // Direct world-query validation exercises geometry without crowd contacts.
    if (p.phase == 4u) {
        if (slot >= p.capacity) return;
        Body body = bodies[slot];
        vec3 from = body.previous_height.xyz;
        vec3 desired = body.position_radius.xyz;
        float radius = body.position_radius.w;
        float height = body.previous_height.w;
        results[slot].position_pressure = vec4(world_resolve(from, desired, radius, height), world_ground(desired));
        results[slot].velocity_counts = vec4(world_support(from, desired, radius), world_max_foot(desired, radius, height), 0.0, 0.0);
        return;
    }
    if (p.phase == 0u) {
        if (slot < p.buckets) heads[slot] = -1;
        return;
    }
    if (p.phase == 1u) {
        if (slot >= p.capacity) return;
        next_slot[slot] = -1;
        if (bodies[slot].world_velocity_alive.w == 0.0) return;
        ivec3 cell = ivec3(floor(bodies[slot].position_radius.xyz / vec3(p.horizontal_cell, p.vertical_cell, p.horizontal_cell)));
        cells[slot] = ivec4(cell, 0);
        next_slot[slot] = atomicExchange(heads[bucket(cell)], int(slot));
        return;
    }
    if (p.phase == 2u) {
        if (slot >= p.buckets) return;
        // Every bucket visit starts at its head and counts every rejection toward
        // the global budget*8 scan cap. Thus only its highest budget*8 slots can
        // ever be observed, including repeated visits and hash collisions.
        int current = heads[slot];
        int sorted = -1;
        int smallest = -1;
        uint retained = 0u;
        uint limit = p.budget * 8u;
        while (current >= 0) {
            // Preserve the original chain before rewriting processed nodes.
            int following = next_slot[current];
            if (retained == limit && current < smallest) {
                current = following;
                continue;
            }
            if (sorted < current) {
                next_slot[current] = sorted;
                sorted = current;
            } else {
                int previous = sorted;
                while (next_slot[previous] > current) previous = next_slot[previous];
                next_slot[current] = next_slot[previous];
                next_slot[previous] = current;
            }
            if (retained < limit) {
                retained++;
                smallest = smallest < 0 ? current : min(smallest, current);
            } else {
                // The inserted node precedes the old minimum. Only already
                // processed nodes occur in this sorted chain, so trimming its
                // tail cannot lose an unvisited original-chain node.
                int before_tail = current;
                while (next_slot[before_tail] != smallest) before_tail = next_slot[before_tail];
                next_slot[before_tail] = -1;
                smallest = before_tail;
            }
            current = following;
        }
        heads[slot] = sorted;
        return;
    }
    if (slot >= p.capacity) return;
    Body body = bodies[slot];
    vec3 position = body.position_radius.xyz;
    float radius = body.position_radius.w;
    float body_height = body.previous_height.w;
    float pressure = body.drive_pressure.w;
    float vertical_velocity = body.world_velocity_alive.z;
    results[slot].position_pressure = vec4(position, pressure);
    results[slot].velocity_counts = vec4(vertical_velocity, 0.0, 0.0, 0.0);
    if (body.world_velocity_alive.w == 0.0) return;

    float search = radius + p.max_radius + 0.05;
    int min_x = int(floor((position.x - search) / p.horizontal_cell));
    int max_x = int(floor((position.x + search) / p.horizontal_cell));
    int min_z = int(floor((position.z - search) / p.horizontal_cell));
    int max_z = int(floor((position.z + search) / p.horizontal_cell));
    int min_y = int(floor((min(position.y, body.previous_height.y) - p.max_height - 0.05) / p.vertical_cell));
    int max_y = int(floor((max(position.y, body.previous_height.y) + body_height + 0.05) / p.vertical_cell));
    vec3 push = vec3(0.0);
    float compression = 0.0;
    float support = uintBitsToFloat(0xff800000u);
    float climb = support;
    float climb_from_lower = support;
    uint accepted = 0u, scanned = 0u;
    float ground = world_enabled() ? world_ground(position) : body.world_velocity_alive.x;
    float world_support_height = world_enabled() ? world_support(position, position, radius) : body.world_velocity_alive.y;
    float pile_limit = ground + p.max_pile;
    vec3 drive = normalized_or_zero(body.drive_pressure.xyz);
    for (int y = min_y; y <= max_y && accepted < p.budget && scanned < p.budget * 8u; y++)
    for (int z = min_z; z <= max_z && accepted < p.budget && scanned < p.budget * 8u; z++)
    for (int x = min_x; x <= max_x && accepted < p.budget && scanned < p.budget * 8u; x++)
    for (int other_slot = heads[bucket(ivec3(x, y, z))]; other_slot >= 0 && accepted < p.budget && scanned < p.budget * 8u; other_slot = next_slot[other_slot]) {
        scanned++;
        if (any(notEqual(cells[other_slot].xyz, ivec3(x, y, z)))) continue;
        Body other = bodies[other_slot];
        if (other_slot == int(slot) || other.world_velocity_alive.w == 0.0) continue;
        vec3 other_position = other.position_radius.xyz;
        if (other_position.y + other.previous_height.w < min(position.y, body.previous_height.y) - 0.05
            || other_position.y > max(position.y, body.previous_height.y) + body_height + 0.05) continue;
        vec3 away = position - other_position;
        away.y = 0.0;
        float distance_squared = dot(away, away);
        float width = radius + other.position_radius.w;
        if (distance_squared >= width * width) continue;
        float height = (body_height + other.previous_height.w) * 0.5;
        float center_delta = position.y + body_height * 0.5 - other_position.y - other.previous_height.w * 0.5;
        if (abs(center_delta) > height + 0.25 && body.previous_height.y < other_position.y + other.previous_height.w) continue;
        accepted++;
        float distance = sqrt(distance_squared);
        vec3 normal;
        if (distance > 0.0001) normal = away / distance;
        else {
            uint pair = min(slot, uint(other_slot)) * 73856093u ^ max(slot, uint(other_slot)) * 19349663u;
            float angle = float(pair & 1023u) * (6.283185307179586 / 1024.0);
            normal = vec3(cos(angle), 0.0, sin(angle)) * (slot < uint(other_slot) ? 1.0 : -1.0);
        }
        float vertical_ratio = abs(center_delta) / max(0.1, height);
        float contact_width = width * sqrt(max(0.0, 1.0 - vertical_ratio * vertical_ratio));
        float overlap = contact_width - distance;
        if (overlap > 0.0) {
            float horizontal_weight = vertical_ratio < 0.1 ? 1.0 : distance / max(0.001, sqrt(distance_squared + center_delta * center_delta));
            push += normal * (overlap * p.stiffness * 0.5 * horizontal_weight);
            compression = max(compression, overlap / width);
        }
        float surface = other_position.y + other.previous_height.w * 0.5 - body_height * 0.5
            + height * sqrt(max(0.0, 1.0 - distance_squared / (width * width)));
        bool below = other_position.y < position.y - 0.12;
        if (below && surface <= body.previous_height.y + 0.001) support = max(support, min(surface, body.previous_height.y));
        bool ahead = -dot(normal, drive) > 0.2 || (distance < 0.001 && slot > uint(other_slot));
        if (p.stacking != 0u && ahead && dot(body.drive_pressure.xyz, body.drive_pressure.xyz) > 0.01 && overlap > width * 0.06
            && other_position.y <= body.previous_height.y + 0.12 && surface <= pile_limit) {
            climb = max(climb, surface);
            if (below) climb_from_lower = max(climb_from_lower, surface);
        }
    }
    pressure = move_toward(pressure, compression, p.dt * 3.0);
    if (dot(push, push) > p.max_push * p.max_push) push = normalized_or_zero(push) * p.max_push;
    vec3 next_position = position + push;
    if (support > next_position.y) { next_position.y = support; vertical_velocity = 0.0; }
    if (p.stacking != 0u) {
        if (position.y > world_support_height + 0.2 && support < position.y - 0.05) climb = climb_from_lower;
        if (pressure > 0.05 && climb > next_position.y) {
            next_position.y = move_toward(next_position.y, climb, p.climb_step);
            vertical_velocity = 0.0;
        }
    }
    if (world_enabled() && any(notEqual(next_position, position)))
        next_position = world_resolve(position, next_position, radius, body_height);
    results[slot].position_pressure = vec4(next_position, pressure);
    results[slot].velocity_counts = vec4(vertical_velocity, float(accepted), float(scanned), world_enabled() ? world_ground(next_position) : 0.0);
}
