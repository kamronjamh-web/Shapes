#pragma once
#include <stdint.h>
#ifdef _WIN32
#define BXC_API __declspec(dllexport)
#else
#define BXC_API __attribute__((visibility("default")))
#endif
typedef struct BxcBodyDef {
    float px, py, pz, qx, qy, qz, qw;
    float sx, sy, sz, vx, vy, vz, wx, wy, wz;
    float linear_damping, angular_damping, friction, restitution, mass;
} BxcBodyDef;
typedef struct BxcBodyState {
    float px, py, pz, qx, qy, qz, qw;
    float vx, vy, vz, wx, wy, wz;
    uint32_t sleeping;
} BxcBodyState;
BXC_API void* bxc_world_create(float gx, float gy, float gz, int corpse_contacts);
BXC_API void bxc_world_destroy(void* world);
/* Positive handles encode a reusable slot and generation. Removed handles never
   alias another body; a slot is retired rather than wrapping its generation. */
BXC_API int bxc_add_box(void* world, const BxcBodyDef* def, int dynamic);
BXC_API int bxc_add_pyramid(void* world, const BxcBodyDef* def);
BXC_API int bxc_add_static_hull(void* world, const BxcBodyDef* def, const float* xyz, int count);
BXC_API int bxc_add_static_mesh(void* world, const BxcBodyDef* def, const float* xyz, int vertex_count);
BXC_API int bxc_step(void* world, float dt, int substeps);
BXC_API int bxc_read(void* world, int handle, BxcBodyState* state);
BXC_API int bxc_remove(void* world, int handle);
BXC_API void bxc_clear_dynamics(void* world);
BXC_API int bxc_dynamic_count(void* world);
BXC_API int bxc_read_batch(void* world, const int* handles, int count, BxcBodyState* states, uint8_t* valid);
BXC_API void bxc_wake_dynamics(void* world);
BXC_API int bxc_abi_version(void);
BXC_API int bxc_storage_slots(void* world);
BXC_API int bxc_abi_size(int which);
