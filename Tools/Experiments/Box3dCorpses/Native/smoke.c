#include "corpse_bridge.h"
#include <math.h>
#include <stdio.h>
int main(void) {
    void* w = bxc_world_create(0, -9.8f, 0, 0);
    if (!w) return 1;
    BxcBodyDef floor = {.py=-.5f,.qw=1,.sx=30,.sy=1,.sz=30,.friction=.8f};
    BxcBodyDef cube = {.py=4,.qw=1,.sx=1,.sy=1,.sz=1,.linear_damping=.25f,.angular_damping=.45f,.friction=.8f,.mass=1};
    BxcBodyDef pyramid = cube; pyramid.px = 2;
    int f = bxc_add_box(w,&floor,0), a=bxc_add_box(w,&cube,1), b=bxc_add_pyramid(w,&pyramid);
    if (f < 1 || a < 1 || b < 1) return 2;
    for (int i=0;i<240;i++) if (!bxc_step(w,1.0f/60.0f,4)) return 3;
    BxcBodyState sa,sb;
    if (!bxc_read(w,a,&sa) || !bxc_read(w,b,&sb)) return 4;
    printf("cube y=%g pyramid y=%g asleep=%u/%u\n",sa.py,sb.py,sa.sleeping,sb.sleeping);
    if (!isfinite(sa.py) || !isfinite(sb.py) || sa.py < .4f || sa.py > .7f || sb.py < .4f || sb.py > .7f) return 5;
    if (!bxc_remove(w,a) || bxc_read(w,a,&sa)) return 6;
    int c=bxc_add_box(w,&cube,1); if (c <= b) return 7;
    bxc_clear_dynamics(w);
    if (bxc_read(w,b,&sb) || bxc_read(w,c,&sa) || !bxc_read(w,f,&sa)) return 8;
    int slots=bxc_storage_slots(w);
    for (int i=0;i<10000;i++) {
        int next=bxc_add_box(w,&cube,1);
        if (next<1 || bxc_read(w,a,&sa) || !bxc_remove(w,next)) return 9;
    }
    if (bxc_storage_slots(w)!=slots || bxc_dynamic_count(w)!=0 || bxc_abi_version()!=2) return 10;
    printf("10000 spawn/remove cycles retain %d native storage slots; stale IDs rejected\n",slots);
    float triangles[]={4,2,-1, 4,2,1, 6,2,1, 4,2,-1, 6,2,1, 6,2,-1};
    BxcBodyDef mesh_def={.qw=1,.friction=1};
    int mesh=bxc_add_static_mesh(w,&mesh_def,triangles,6);
    /* Mesh shapes borrow their mesh allocation. Repeated real-capacity hull/body
       churn before first terrain contact catches accidental early mesh frees. */
    for (int pass=0;pass<5;pass++) {
        for (int i=0;i<2056;i++) {
            BxcBodyDef churn=cube; churn.px=(float)(i%46)-23; churn.pz=(float)(i/46)-23;
            int id=(i&1)?bxc_add_pyramid(w,&churn):bxc_add_box(w,&churn,1);
            if (id<1) return 14;
        }
        if (bxc_dynamic_count(w)!=2056) return 15;
        bxc_clear_dynamics(w);
        if (bxc_dynamic_count(w)!=0 || !bxc_read(w,mesh,&sa) || bxc_storage_slots(w)>2058) return 16;
    }
    printf("borrowed terrain survives five 2056 mixed-body spawn/clear cycles (%d bounded slots)\n",bxc_storage_slots(w));
    cube.px=5; cube.py=4;
    int on_mesh=bxc_add_box(w,&cube,1);
    if (mesh<1 || on_mesh<1) return 11;
    for(int i=0;i<240;i++) bxc_step(w,1.0f/60.0f,4);
    int handles[]={on_mesh,a}; BxcBodyState states[2]; uint8_t valid[2];
    if (!bxc_read_batch(w,handles,2,states,valid) || valid[0]!=1 || valid[1]!=0
        || states[0].py<2.4f || states[0].py>2.7f) return 12;
    printf("CCW terrain mesh rests cube y=%g; batch rejects stale body\n",states[0].py);
    bxc_remove(w,mesh); bxc_wake_dynamics(w);
    for(int i=0;i<240;i++) bxc_step(w,1.0f/60.0f,4);
    if (!bxc_read_batch(w,handles,2,states,valid) || states[0].py<.4f || states[0].py>.7f) return 13;
    printf("terrain removal wakes sleeper and invalidates cache: cube y=%g\n",states[0].py);
    bxc_world_destroy(w); puts("native corpse bridge smoke passed"); return 0;
}
