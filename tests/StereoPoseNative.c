// Owned C ABI port; no Unity or game implementation.
#include <string.h>
static const void *camera;
static unsigned calls,errors;
void stereo_test_camera(const void *self){camera=self;}
void stereo_test_view(const void *self,int eye,float *out){
    ++calls;
    if(!self||self!=camera||eye<0||eye>1||!out){++errors;return;}
    memcpy(out,(const char*)self+64*eye,64);
}
unsigned stereo_test_stat(int which){return which?errors:calls;}
