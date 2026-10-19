// Adapted from McFredward/GloomhavenVR, GPL-3.0.
#define GHVR_CONNECTION_TEST
#include "../tools/steamlink-connection-reader.c"
#include <stdlib.h>
static int checks,calls,connected_calls,class_value=1;static bool linked=true,changed=false;
static int bad, optional_missing;static char *tracking="vrlink",*model="Steam Frame",*registered="vrlink/frame";
static int init_calls,get_calls,shutdown_calls,init_error,get_error,stage;
static bool null_table;static struct System022 *queried_api;
static void check(bool value){++checks;if(!value){fprintf(stderr,"native metadata check failed%d\n",checks);exit(1);}}
static int32_t kind(uint32_t index){check(index==0);return class_value;}
static bool connected(uint32_t index){check(index==0);connected_calls++;return linked;}
static uint32_t string_property(uint32_t index,int32_t property,char *out,uint32_t cap,int32_t *error){
    check(index==0&&cap==512);calls++;*error=0;const char *value=property==1000?tracking:property==1001?model:registered;
    check(property==1000||property==1001||property==1036);
    if(optional_missing&&property!=1001){*error=4;return 0;}
    if(bad==1){*error=1;return 0;}
    if(bad==2)return cap+1;
    if(bad==3)return 0;
    if(bad==4){memset(out,'x',cap);return cap;}
    if(bad==5){memset(out,'x',120);out[120]=0;return 121;}
    if(bad==6){strcpy(out,"bad\nname");return 9;}
    if(changed&&calls>3&&property==1001)value="Quest 3";
    strcpy(out,value);return strlen(value)+1;
}
static uint32_t fake_init(int32_t *error,int32_t app,const char *startup){
    check(error!=NULL&&app==3&&startup==NULL);check(stage==0);stage=1;
    init_calls++;*error=init_error;return init_error?0:41;
}
static void *fake_get(const char *name,int32_t *error){
    check(stage==1&&init_error==0);check(!strcmp(name,"FnTable:IVRSystem_022"));
    stage=2;get_calls++;*error=get_error;return null_table?NULL:queried_api;
}
static void fake_shutdown(void){check(stage==2&&init_error==0);stage=3;shutdown_calls++;}
static void reset_query(struct System022 *api){
    queried_api=api;init_calls=get_calls=shutdown_calls=init_error=get_error=stage=0;
    null_table=false;calls=connected_calls=bad=optional_missing=0;linked=true;class_value=1;changed=false;
}
static void query_checks(struct System022 *api){
    struct Metadata out;
    reset_query(api);
    check(!strcmp(query_metadata(NULL,fake_get,fake_shutdown,&out),"exports"));
    check(!strcmp(query_metadata(fake_init,NULL,fake_shutdown,&out),"exports"));
    check(!strcmp(query_metadata(fake_init,fake_get,NULL,&out),"exports"));
    check(init_calls==0&&get_calls==0&&shutdown_calls==0&&calls==0);
    reset_query(api);init_error=121;
    check(!strcmp(query_metadata(fake_init,fake_get,fake_shutdown,&out),"not_running"));
    check(init_calls==1&&get_calls==0&&shutdown_calls==0&&calls==0&&stage==1);
    for(size_t i=0;i<4;i++){
        const int32_t errors[]={1,110,119,301};reset_query(api);init_error=errors[i];
        check(!strcmp(query_metadata(fake_init,fake_get,fake_shutdown,&out),"initialization"));
        check(init_calls==1&&get_calls==0&&shutdown_calls==0&&calls==0);
    }
    reset_query(api);get_error=105;
    check(!strcmp(query_metadata(fake_init,fake_get,fake_shutdown,&out),"interface"));
    check(init_calls==1&&get_calls==1&&shutdown_calls==1&&calls==0&&stage==3);
    reset_query(api);null_table=true;
    check(!strcmp(query_metadata(fake_init,fake_get,fake_shutdown,&out),"interface"));
    check(shutdown_calls==1&&calls==0);
    reset_query(api);linked=false;
    check(!strcmp(query_metadata(fake_init,fake_get,fake_shutdown,&out),"disconnected"));
    check(init_calls==1&&get_calls==1&&shutdown_calls==1&&calls==0);
    reset_query(api);bad=1;
    check(!strcmp(query_metadata(fake_init,fake_get,fake_shutdown,&out),"properties"));
    check(shutdown_calls==1&&calls==1);
    reset_query(api);changed=true;
    check(!strcmp(query_metadata(fake_init,fake_get,fake_shutdown,&out),"changed"));
    check(shutdown_calls==1&&calls==6);
    reset_query(api);
    /* This actual production sequence has no Windows process-list prerequisite.
     * The fake SDK enforces Background/NULL startup, never Scene or a retry. */
    check(query_metadata(fake_init,fake_get,fake_shutdown,&out)==NULL);
    check(init_calls==1&&get_calls==1&&shutdown_calls==1&&stage==3&&calls==6);
    check(out.connected&&out.device_class==1&&!strcmp(out.tracking,"vrlink")
        &&!strcmp(out.model,"Steam Frame")&&!strcmp(out.registered,"vrlink/frame"));
}
int main(void){
    struct System022 api={0};api.deviceClass=kind;api.connected=connected;api.stringProperty=string_property;struct Metadata out;
    check(read_metadata(&api,&out)==NULL);check(!strcmp(out.tracking,"vrlink")&&!strcmp(out.model,"Steam Frame"));check(calls==6&&connected_calls==2);
    linked=false;calls=0;check(!strcmp(read_metadata(&api,&out),"disconnected")&&calls==0);linked=true;
    class_value=2;check(!strcmp(read_metadata(&api,&out),"disconnected"));class_value=1;
    for(bad=1;bad<=6;bad++){calls=0;check(!strcmp(read_metadata(&api,&out),"properties"));check(calls<=3);}bad=0;
    changed=true;calls=0;check(!strcmp(read_metadata(&api,&out),"changed"));changed=false;
    optional_missing=1;calls=0;check(read_metadata(&api,&out)==NULL);check(!*out.tracking&&!*out.registered);optional_missing=0;
    api.stringProperty=NULL;check(!strcmp(read_metadata(&api,&out),"interface"));check(!strcmp(read_metadata(NULL,&out),"interface"));
    int metadata_checks=checks;api.stringProperty=string_property;query_checks(&api);
    printf("{\"native_public_metadata_checks\":%d,\"native_background_query_checks\":%d,\"actual_Windows_execution\":false,\"actual_Proton_execution\":false}\n",metadata_checks,checks-metadata_checks);return 0;
}
