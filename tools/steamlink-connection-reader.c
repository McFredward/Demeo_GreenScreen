/* Read-only child process. Never link this OpenVR lifetime into Unity/OpenXR.
 * Public ABI: ValveSoftware/openvr v1.26.7 headers/openvr_capi.h, IVRSystem_022.
 * Only HMD0 connection/class and string properties1000/1001/1036 are read.
 */
#include <stdbool.h>
#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>
#ifndef GHVR_CONNECTION_TEST
#include <windows.h>
#include <wchar.h>
#define TABLE_CALL __stdcall
#define EXPORT_CALL __cdecl
#else
#define TABLE_CALL
#define EXPORT_CALL
#endif
#define STRING_CAP 512
#define TEXT_CAP 96
struct System022 {
    void *recommended, *projection, *raw, *distortion, *eye, *vsync, *d3d9, *dxgi, *output;
    void *desktop, *visibility, *poses, *seated, *standing, *sorted, *activity, *transform, *role, *roleFor;
    int32_t (TABLE_CALL *deviceClass)(uint32_t);
    bool (TABLE_CALL *connected)(uint32_t);
    void *booleanProperty, *floatProperty, *intProperty, *uintProperty, *matrixProperty, *arrayProperty;
    uint32_t (TABLE_CALL *stringProperty)(uint32_t, int32_t, char *, uint32_t, int32_t *);
};
_Static_assert(offsetof(struct System022,deviceClass)==19*sizeof(void*),"public022 class slot");
_Static_assert(offsetof(struct System022,connected)==20*sizeof(void*),"public022 connection slot");
_Static_assert(offsetof(struct System022,stringProperty)==27*sizeof(void*),"public022 string slot");
struct Metadata { bool connected; int32_t device_class; char tracking[STRING_CAP],model[STRING_CAP],registered[STRING_CAP]; };
typedef uint32_t (EXPORT_CALL *Init)(int32_t *,int32_t,const char *);
typedef void *(EXPORT_CALL *Get)(const char *,int32_t *);
typedef void (EXPORT_CALL *Shutdown)(void);
static bool property(struct System022 *api,int32_t id,char *out,bool optional) {
    memset(out,0xff,STRING_CAP);int32_t error=0;
    uint32_t size=api->stringProperty(0,id,out,STRING_CAP,&error);
    if(optional&&error==4){out[0]=0;return true;}
    if(error||size<(optional?1u:2u)||size>STRING_CAP||out[size-1]!=0||strlen(out)!=size-1||size-1>TEXT_CAP)return false;
    for(uint32_t i=0;i<size-1;++i)if((unsigned char)out[i]<32||(unsigned char)out[i]>126)return false;
    return true;
}
static const char *read_metadata(struct System022 *api,struct Metadata *out) {
    memset(out,0,sizeof(*out));
    if(!api||!api->deviceClass||!api->connected||!api->stringProperty)return "interface";
    out->connected=api->connected(0);out->device_class=api->deviceClass(0);
    if(!out->connected||out->device_class!=1)return "disconnected";
    if(!property(api,1000,out->tracking,true)||!property(api,1001,out->model,false)||!property(api,1036,out->registered,true))return "properties";
    /* Repeat the narrow identity to reject ordinary mid-query device switches.
     * Public individual reads are not an atomic hardware identity/security proof. */
    struct Metadata second={0};
    if(!api->connected(0)||api->deviceClass(0)!=1||!property(api,1000,second.tracking,true)||
       !property(api,1001,second.model,false)||!property(api,1036,second.registered,true)||
       strcmp(out->tracking,second.tracking)||strcmp(out->model,second.model)||strcmp(out->registered,second.registered))return "changed";
    return NULL;
}
static const char *query_metadata(Init init,Get get,Shutdown shutdown,struct Metadata *out) {
    if(!init||!get||!shutdown)return "exports";
    /* Valve's public Background=3 contract forbids starting/keeping SteamVR alive.
     * Do not require Windows vrserver.exe/vrcompositor.exe names: under Proton
     * their native Linux processes are outside this PE process census. The SDK
     * resolves Proton's C:\\vrclient bridge and public environment itself.
     * OpenVR1.26.7 headers/openvr.h: EVRApplicationType and EVRInitError121. */
    int32_t error=0;init(&error,3,NULL);
    if(error)return error==121?"not_running":"initialization";
    struct System022 *api=get("FnTable:IVRSystem_022",&error);
    const char *reason=error?"interface":read_metadata(api,out);
    shutdown(); /* Exactly our successfully initialized Background lifetime. */
    return reason;
}
#ifndef GHVR_CONNECTION_TEST
static void text(const char *s) { putchar('"');for(;*s;++s){if(*s=='"'||*s=='\\')putchar('\\');putchar(*s);}putchar('"'); }
static int failure(const char *reason){printf("{\"schema\":\"ghvr-steamlink-connection-v1\",\"error\":\"%s\"}\n",reason);return 2;}
static HANDLE checked_library(const wchar_t *path){
    size_t count=wcslen(path);if(count<4||count>=1024||!((path[0]>=L'A'&&path[0]<=L'Z')||(path[0]>=L'a'&&path[0]<=L'z'))||path[1]!=L':'||path[2]!=L'\\')return INVALID_HANDLE_VALUE;
    wchar_t canonical[1024];DWORD n=GetFullPathNameW(path,1024,canonical,NULL);
    if(!n||n>=1024||_wcsicmp(path,canonical))return INVALID_HANDLE_VALUE;
    if(_wcsicmp(wcsrchr(path,L'\\')+1,L"openvr_api.dll"))return INVALID_HANDLE_VALUE;
    for(size_t i=3;i<count;++i){if(path[i]!=L'\\')continue;wchar_t c=canonical[i];canonical[i]=0;DWORD flags=GetFileAttributesW(canonical);canonical[i]=c;if(flags==INVALID_FILE_ATTRIBUTES||(flags&FILE_ATTRIBUTE_REPARSE_POINT))return INVALID_HANDLE_VALUE;}
    DWORD flags=GetFileAttributesW(path);if(flags==INVALID_FILE_ATTRIBUTES||(flags&(FILE_ATTRIBUTE_REPARSE_POINT|FILE_ATTRIBUTE_DIRECTORY)))return INVALID_HANDLE_VALUE;
    HANDLE h=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,NULL,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,NULL);if(h==INVALID_HANDLE_VALUE)return h;
    LARGE_INTEGER size;IMAGE_DOS_HEADER dos;IMAGE_NT_HEADERS64 pe;DWORD bytes;
    bool ok=GetFileSizeEx(h,&size)&&size.QuadPart>=512&&size.QuadPart<=8*1024*1024&&ReadFile(h,&dos,sizeof(dos),&bytes,NULL)&&bytes==sizeof(dos)&&dos.e_magic==IMAGE_DOS_SIGNATURE&&dos.e_lfanew>=64&&dos.e_lfanew<=size.QuadPart-(LONGLONG)sizeof(pe);
    if(ok)ok=SetFilePointer(h,dos.e_lfanew,NULL,FILE_BEGIN)!=INVALID_SET_FILE_POINTER&&ReadFile(h,&pe,sizeof(pe),&bytes,NULL)&&bytes==sizeof(pe)&&pe.Signature==IMAGE_NT_SIGNATURE&&pe.FileHeader.Machine==IMAGE_FILE_MACHINE_AMD64&&pe.FileHeader.SizeOfOptionalHeader>=sizeof(IMAGE_OPTIONAL_HEADER64)&&pe.OptionalHeader.Magic==IMAGE_NT_OPTIONAL_HDR64_MAGIC&&(pe.FileHeader.Characteristics&IMAGE_FILE_DLL);
    if(!ok){CloseHandle(h);return INVALID_HANDLE_VALUE;}return h;
}
int wmain(int argc,wchar_t **argv){
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX|SEM_NOOPENFILEERRORBOX);SetDllDirectoryW(L"");
    if(argc!=2)return failure("arguments");
    HANDLE file=checked_library(argv[1]);if(file==INVALID_HANDLE_VALUE)return failure("library");
    HMODULE library=LoadLibraryExW(argv[1],NULL,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
    if(!library){CloseHandle(file);return failure("library");}
    Init init=NULL;Get get=NULL;Shutdown shutdown=NULL;
    FARPROC address=GetProcAddress(library,"VR_InitInternal2");memcpy(&init,&address,sizeof(init));
    address=GetProcAddress(library,"VR_GetGenericInterface");memcpy(&get,&address,sizeof(get));
    address=GetProcAddress(library,"VR_ShutdownInternal");memcpy(&shutdown,&address,sizeof(shutdown));
    struct Metadata out;const char *reason=query_metadata(init,get,shutdown,&out);
    FreeLibrary(library);CloseHandle(file);
    if(reason)return failure(reason);
    printf("{\"schema\":\"ghvr-steamlink-connection-v1\",\"connected\":true,\"device_class\":1,\"tracking_system\":");text(out.tracking);
    printf(",\"model\":");text(out.model);printf(",\"registered_type\":");text(out.registered);printf("}\n");return 0;
}
#endif
