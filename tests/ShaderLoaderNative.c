// Owned synchronous Unity6 binding fixture: distinct GC handles, IL2CPP object
// pointers and native Unity object pointers; no Unity/Valve/game implementation.
#include <stdint.h>
#include <stddef.h>
#include <string.h>

typedef struct { const void *begin; int length; } ShaderSpan;
_Static_assert(sizeof(ShaderSpan)==16 && offsetof(ShaderSpan,length)==8,"Unity6 x64 span ABI");
#define BUNDLE_HANDLE ((uintptr_t)UINT64_C(0x1234567800000011))
#define SHADER_HANDLE ((uintptr_t)UINT64_C(0x1234567800000022))
#define BUNDLE_OBJECT ((uintptr_t)0x400110)
#define SHADER_OBJECT ((uintptr_t)0x400220)
#define TYPE_OBJECT ((uintptr_t)0x700033)
static int mode, memory_calls, asset_calls, errors, engine_frees, visits, roots[3];
static uint16_t expected_name[512];
static int expected_name_length;
static void (*visit)(void);
void shader_test_reset(int selected_mode) { mode=selected_mode; memory_calls=asset_calls=errors=engine_frees=visits=0; }
void shader_test_visit(void (*callback)(void)) { visit=callback; }
void shader_test_name(const uint16_t *name,int length) {
    if(length<=0||length>512){errors++;return;}
    expected_name_length=length;memcpy(expected_name,name,(size_t)length*2);
}
static int kind(uintptr_t object) { return object==BUNDLE_OBJECT?0:object==SHADER_OBJECT?1:object==TYPE_OBJECT?2:-1; }
void shader_test_root(uintptr_t object,int delta) { int i=kind(object);if(i<0){errors++;return;}roots[i]+=delta;if(roots[i]<0)errors++; }
uintptr_t shader_test_memory(const ShaderSpan *bytes,uint32_t crc) {
    memory_calls++;
    if(!bytes||!bytes->begin||bytes->length!=4097||crc){errors++;return 0;}
    if(visit){visits++;visit();}
    for(int i=0;i<bytes->length;i++)if(((const uint8_t*)bytes->begin)[i]!=(uint8_t)i){errors++;break;}
    return mode==1?0:BUNDLE_HANDLE;
}
uintptr_t shader_test_asset(uintptr_t self,const ShaderSpan *name,uintptr_t type) {
    asset_calls++;
    if(visit){visits++;visit();}
    if(self!=0x600110||type!=TYPE_OBJECT||roots[0]<1||roots[2]<1||!name||!name->begin
        ||name->length!=expected_name_length||memcmp(name->begin,expected_name,(size_t)expected_name_length*2))errors++;
    return mode==5?0:SHADER_HANDLE;
}
uintptr_t shader_test_target(uintptr_t handle) {
    if(mode==2)return 0;
    if(handle==BUNDLE_HANDLE)return BUNDLE_OBJECT;
    if(handle==SHADER_HANDLE)return SHADER_OBJECT;
    errors++;return 0;
}
void shader_test_free(uintptr_t handle) { if(mode!=3||handle!=BUNDLE_HANDLE)errors++;engine_frees++; }
uintptr_t shader_test_class(uintptr_t object) { if(mode==4&&object==BUNDLE_OBJECT)return 0x502;int k=kind(object);return k==0?0x501:k==1?0x502:0x503; }
int shader_test_id(uintptr_t object) { return mode==3&&object==BUNDLE_OBJECT?0:kind(object)+1; }
uintptr_t shader_test_cached(uintptr_t object) { return mode==6?0:object==BUNDLE_OBJECT?0x600110:object==SHADER_OBJECT?0x600220:0; }
int shader_test_stat(int index) {
    switch(index){case 0:return errors;case 1:return memory_calls;case 2:return asset_calls;case 3:return engine_frees;case 4:return visits;default:return -1;}
}
