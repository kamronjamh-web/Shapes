#include "corpse_bridge.h"
#include <box3d/box3d.h>
#include <stdlib.h>
#include <math.h>
#include <stdatomic.h>

/* Upstream world-slot discovery is global and not synchronized. Serialize only
   lifecycle operations; independent worlds still step on their own threads. */
static atomic_flag lifecycle_lock = ATOMIC_FLAG_INIT;
static void lock_lifecycle(void) { while (atomic_flag_test_and_set_explicit(&lifecycle_lock,memory_order_acquire)) {} }
static void unlock_lifecycle(void) { atomic_flag_clear_explicit(&lifecycle_lock,memory_order_release); }

typedef struct Entry {
    b3BodyId body;
    /* Mesh shapes borrow this allocation; hull shapes are copied by Box3D. */
    b3MeshData* mesh;
    BxcBodyState cached;
    int dynamic, active, next_free, cache_valid;
    uint32_t generation;
} Entry;
typedef struct World { b3WorldId id; Entry* entries; int count, capacity, corpse_contacts, free_head; } World;
_Static_assert(sizeof(BxcBodyDef) == 84, "Body definition ABI");
_Static_assert(sizeof(BxcBodyState) == 56, "Body state ABI");

static int store(World* w, b3BodyId body, int dynamic) {
    int index;
    if (w->free_head >= 0) {
        index = w->free_head;
        w->free_head = w->entries[index].next_free;
    } else {
        if (w->count == 65535) return -1;
        if (w->count == w->capacity) {
            int capacity = w->capacity ? w->capacity * 2 : 64;
            if (capacity > 65535) capacity = 65535;
            Entry* entries = realloc(w->entries, (size_t)capacity * sizeof(Entry));
            if (!entries) return -1;
            for (int i = w->capacity; i < capacity; i++) entries[i] = (Entry){0};
            w->entries = entries; w->capacity = capacity;
        }
        index = w->count++;
    }
    Entry* entry = &w->entries[index];
    entry->body = body; entry->mesh = NULL; entry->active = 1; entry->dynamic = dynamic; entry->cache_valid = 0;
    return (int)((entry->generation << 16) | (uint32_t)(index + 1));
}
void* bxc_world_create(float gx, float gy, float gz, int corpse_contacts) {
    if (!isfinite(gx) || !isfinite(gy) || !isfinite(gz)) return NULL;
    World* w = calloc(1, sizeof(World));
    if (!w) return NULL;
    b3WorldDef def = b3DefaultWorldDef();
    def.gravity = (b3Vec3){gx, gy, gz};
    def.workerCount = 1; /* Explicit single-thread baseline; no external scheduler. */
    lock_lifecycle(); w->id = b3CreateWorld(&def); unlock_lifecycle();
    if (!b3World_IsValid(w->id)) { free(w); return NULL; }
    w->corpse_contacts = corpse_contacts != 0; w->free_head = -1; return w;
}
void bxc_world_destroy(void* world) {
    World* w = world; if (!w) return;
    lock_lifecycle(); b3DestroyWorld(w->id); unlock_lifecycle();
    for (int i = 0; i < w->count; i++) if (w->entries[i].mesh) b3DestroyMesh(w->entries[i].mesh);
    free(w->entries); free(w);
}
static int attach(World* w, const BxcBodyDef* d, int dynamic, const b3HullData* hull, float volume) {
    if (!w || !d || !hull) return -1;
    b3BodyDef body = b3DefaultBodyDef();
    body.type = dynamic ? b3_dynamicBody : b3_staticBody;
    body.position = (b3Pos){d->px, d->py, d->pz};
    body.rotation = (b3Quat){{d->qx, d->qy, d->qz}, d->qw};
    body.linearVelocity = (b3Vec3){d->vx, d->vy, d->vz};
    body.angularVelocity = (b3Vec3){d->wx, d->wy, d->wz};
    body.linearDamping = d->linear_damping; body.angularDamping = d->angular_damping;
    b3BodyId id = b3CreateBody(w->id, &body);
    if (!b3Body_IsValid(id)) return -1;
    b3ShapeDef shape = b3DefaultShapeDef();
    shape.density = dynamic ? d->mass / volume : 0.0f;
    shape.baseMaterial.friction = d->friction; shape.baseMaterial.restitution = d->restitution;
    /* Static category 1 accepts corpses (2); corpses accept only static by default.
       Symmetric Box3D filtering represents the baseline's one-way Godot mask. */
    shape.filter.categoryBits = dynamic ? 2 : 1;
    shape.filter.maskBits = dynamic ? (w->corpse_contacts ? 3 : 1) : 2;
    b3ShapeId sid = b3CreateHullShape(id, &shape, hull);
    if (!b3Shape_IsValid(sid)) { b3DestroyBody(id); return -1; }
    int handle = store(w, id, dynamic);
    if (handle < 1) b3DestroyBody(id);
    return handle;
}
int bxc_add_box(void* world, const BxcBodyDef* d, int dynamic) {
    if (!d || d->sx <= 0 || d->sy <= 0 || d->sz <= 0) return -1;
    b3BoxHull hull = b3MakeBoxHull(d->sx * .5f, d->sy * .5f, d->sz * .5f);
    return attach(world, d, dynamic != 0, &hull.base, d->sx * d->sy * d->sz);
}
int bxc_add_pyramid(void* world, const BxcBodyDef* d) {
    if (!d || d->sx <= 0 || d->sy <= 0 || d->sz <= 0) return -1;
    float x = d->sx * .5f, y = d->sy * .5f, z = d->sz * .5f;
    b3Vec3 points[] = {{-x,-y,-z},{x,-y,-z},{x,-y,z},{-x,-y,z},{0,y,0}};
    b3HullData* hull = b3CreateHull(points, 5, 5);
    if (!hull) return -1;
    int result = attach(world, d, 1, hull, d->sx * d->sy * d->sz / 3.0f);
    b3DestroyHull(hull); return result;
}
int bxc_add_static_hull(void* world, const BxcBodyDef* d, const float* xyz, int count) {
    if (!xyz || count < 4 || count > 64) return -1;
    b3Vec3 points[64];
    for (int i = 0; i < count; i++) points[i] = (b3Vec3){xyz[3*i],xyz[3*i+1],xyz[3*i+2]};
    b3HullData* hull = b3CreateHull(points, count, count);
    if (!hull) return -1;
    int result = attach(world, d, 0, hull, 1.0f);
    b3DestroyHull(hull); return result;
}
int bxc_add_static_mesh(void* world, const BxcBodyDef* d, const float* xyz, int vertex_count) {
    World* w = world;
    if (!w || !d || !xyz || vertex_count < 3 || vertex_count % 3 != 0 || vertex_count > 300000) return -1;
    b3Vec3* points = malloc((size_t)vertex_count * sizeof(b3Vec3));
    int32_t* indices = malloc((size_t)vertex_count * sizeof(int32_t));
    if (!points || !indices) { free(points); free(indices); return -1; }
    for (int i = 0; i < vertex_count; i++) {
        points[i] = (b3Vec3){xyz[3*i],xyz[3*i+1],xyz[3*i+2]}; indices[i] = i;
    }
    b3MeshDef mesh_def = {0};
    mesh_def.vertices = points; mesh_def.indices = indices;
    mesh_def.vertexCount = vertex_count; mesh_def.triangleCount = vertex_count / 3;
    mesh_def.weldVertices = true; mesh_def.weldTolerance = .00001f; mesh_def.identifyEdges = true;
    /* API contract: vertices are CCW, unlike Godot GetFaces(). */
    b3MeshData* mesh = b3CreateMesh(&mesh_def, NULL, 0);
    free(points); free(indices);
    if (!mesh) return -1;
    b3BodyDef body = b3DefaultBodyDef();
    body.position = (b3Pos){d->px,d->py,d->pz}; body.rotation = (b3Quat){{d->qx,d->qy,d->qz},d->qw};
    b3BodyId id = b3CreateBody(w->id, &body);
    if (!b3Body_IsValid(id)) { b3DestroyMesh(mesh); return -1; }
    b3ShapeDef shape = b3DefaultShapeDef();
    shape.baseMaterial.friction = d->friction; shape.baseMaterial.restitution = d->restitution;
    shape.filter.categoryBits = 1; shape.filter.maskBits = 2;
    b3ShapeId sid = b3CreateMeshShape(id, &shape, mesh, (b3Vec3){1,1,1});
    if (!b3Shape_IsValid(sid)) { b3DestroyBody(id); b3DestroyMesh(mesh); return -1; }
    int handle = store(w,id,0);
    if (handle < 1) { b3DestroyBody(id); b3DestroyMesh(mesh); }
    else w->entries[(handle & 65535) - 1].mesh = mesh;
    return handle;
}
int bxc_step(void* world, float dt, int substeps) {
    if (!world || !isfinite(dt) || dt <= 0 || dt > .25f || substeps < 1 || substeps > 16) return 0;
    b3World_Step(((World*)world)->id, dt, substeps); return 1;
}
static Entry* lookup(World* w, int handle) {
    if (!w || handle < 1) return NULL;
    int index = (handle & 65535) - 1;
    if (index < 0 || index >= w->count) return NULL;
    Entry* entry = &w->entries[index];
    return entry->active && entry->generation == ((uint32_t)handle >> 16) && b3Body_IsValid(entry->body) ? entry : NULL;
}
int bxc_read(void* world, int handle, BxcBodyState* state) {
    Entry* e = lookup(world, handle); if (!e || !state) return 0;
    int asleep = !b3Body_IsAwake(e->body);
    if (asleep && e->cache_valid && e->cached.sleeping) { *state = e->cached; return 1; }
    b3Pos p = b3Body_GetPosition(e->body); b3Quat q = b3Body_GetRotation(e->body);
    b3Vec3 v = b3Body_GetLinearVelocity(e->body), a = b3Body_GetAngularVelocity(e->body);
    *state = (BxcBodyState){p.x,p.y,p.z,q.v.x,q.v.y,q.v.z,q.s,v.x,v.y,v.z,a.x,a.y,a.z,(uint32_t)asleep};
    e->cached = *state; e->cache_valid = 1;
    return 1;
}
int bxc_remove(void* world, int handle) {
    Entry* e = lookup(world, handle); if (!e) return 0;
    World* w = world;
    b3DestroyBody(e->body); e->body = (b3BodyId){0}; e->active = 0; e->cache_valid = 0;
    if (e->mesh) { b3DestroyMesh(e->mesh); e->mesh = NULL; }
    // Keep handles positive; retire the slot instead of ever aliasing an old ID.
    if (e->generation < 32767) {
        e->generation++; e->next_free = w->free_head; w->free_head = (int)(e - w->entries);
    }
    return 1;
}
void bxc_clear_dynamics(void* world) {
    World* w = world; if (!w) return;
    for (int i = 0; i < w->count; i++) if (w->entries[i].dynamic && w->entries[i].active)
        bxc_remove(w, (int)((w->entries[i].generation << 16) | (uint32_t)(i + 1)));
}
int bxc_abi_size(int which) { return which == 0 ? sizeof(BxcBodyDef) : sizeof(BxcBodyState); }
int bxc_dynamic_count(void* world) {
    World* w = world; if (!w) return 0;
    int count = 0;
    for (int i = 0; i < w->count; i++) if (w->entries[i].dynamic && w->entries[i].active) count++;
    return count;
}
int bxc_read_batch(void* world, const int* handles, int count, BxcBodyState* states, uint8_t* valid) {
    if (!world || !handles || !states || !valid || count < 0 || count > 65535) return 0;
    for (int i = 0; i < count; i++) valid[i] = (uint8_t)bxc_read(world, handles[i], &states[i]);
    return 1;
}
void bxc_wake_dynamics(void* world) {
    World* w = world; if (!w) return;
    for (int i = 0; i < w->count; i++) if (w->entries[i].dynamic && w->entries[i].active) {
        w->entries[i].cache_valid = 0; b3Body_SetAwake(w->entries[i].body,true);
    }
}
int bxc_abi_version(void) { return 2; }
int bxc_storage_slots(void* world) { return world ? ((World*)world)->count : 0; }
