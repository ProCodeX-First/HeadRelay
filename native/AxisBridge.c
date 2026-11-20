// SPDX-License-Identifier: MIT
/* HeadRelay compatibility bridge. Packet checksum/encoding adapted from
 * linuxtrack src/wine_bridge/client/NPClient_main.c (MIT):
 * Copyright (c) 2009 Tulthix, uglyDwarf. Full notice: Licenses/LINUXTRACK-MIT.txt.
 * Remaining glue written for HeadRelay. No vendor SDK binaries or signatures. */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <string.h>
#include <math.h>
#pragma pack(push,1)
typedef struct { int32_t sequence,width,height; float yaw,pitch,roll,x,y,z,rawYaw,rawPitch,rawRoll,rawX,rawY,rawZ,points[8]; int32_t game; unsigned char key[8]; int32_t ack; } SharedPose;
typedef struct { int16_t status,frame; uint32_t checksum; float roll,pitch,yaw,x,y,z,padding[9]; } ClientPose;
#pragma pack(pop)
_Static_assert(sizeof(SharedPose)==108,"shared layout");
_Static_assert(sizeof(ClientPose)==68,"client layout");
static HANDLE mapping,guard;static SharedPose *pose;static unsigned short profile;static LONG sequence;static int enabled=1;static wchar_t heapName[180],mutexName[200];
static void names(void){if(heapName[0])return;if(!GetEnvironmentVariableW(L"HEADRELAY_BRIDGE_HEAP",heapName,150)){lstrcpyW(heapName,L"FT_SharedMem");lstrcpyW(mutexName,L"FT_Mutext");}else{lstrcpyW(mutexName,heapName);lstrcatW(mutexName,L"Mutex");}}
static int connect(void){if(pose)return 1;names();mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,heapName);if(!mapping)return 0;pose=MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(SharedPose));if(!pose){CloseHandle(mapping);mapping=0;return 0;}guard=CreateMutexW(0,FALSE,mutexName);if(!guard){UnmapViewOfFile(pose);pose=0;CloseHandle(mapping);mapping=0;return 0;}return 1;}
static int snapshot(SharedPose *out){if(!connect())return 0;DWORD r=WaitForSingleObject(guard,15);if(r!=WAIT_OBJECT_0&&r!=WAIT_ABANDONED)return 0;pose->game=profile;memcpy(out,pose,sizeof(*out));ReleaseMutex(guard);return 1;}
static uint32_t checksum68(const unsigned char *bytes){uint32_t c=68;for(unsigned i=0;i<68;i+=4){int16_t a,b;memcpy(&a,bytes+i,2);memcpy(&b,bytes+i+2,2);c+=(int32_t)a;uint32_t q=((uint32_t)(int32_t)b^(c<<5))<<11;c^=q;c+=(int32_t)c>>11;}c^=c<<3;c+=(int32_t)c>>5;c^=c<<4;c+=(int32_t)c>>17;c^=c<<25;c+=(int32_t)c>>6;return c;}
static void encode(unsigned char *bytes,const unsigned char key[8]){unsigned char state=0x88;unsigned k=0;for(int i=67;i>=0;--i){unsigned char original=bytes[i];bytes[i]=original^key[k]^state;state=(unsigned char)(state+i+original);k=(k+1)&7;}}
static float scale(float value){float n=value*(16383.0f/3.14159265358979323846f);if(!isfinite(n))return 0;return n>16383?16383:n< -16383?-16383:n;}
#define API int __stdcall
API NP_GetData(ClientPose *out){if(!out)return 1;memset(out,0,sizeof(*out));out->status=1;SharedPose s;if(!snapshot(&s)||s.game!=s.ack||!enabled)return 1;out->status=0;out->frame=(int16_t)InterlockedIncrement(&sequence);out->yaw=scale(s.yaw);out->pitch=scale(s.pitch);out->roll=scale(s.roll);out->checksum=checksum68((unsigned char*)out);int encrypted=0;for(int i=0;i<8;i++)encrypted|=s.key[i];if(encrypted)encode((unsigned char*)out,s.key);return 0;}
API NP_RegisterProgramProfileID(unsigned short id){profile=id;SharedPose s;snapshot(&s);return 0;}
API NP_QueryVersion(unsigned short *v){if(!v)return 1;*v=0x0500;return 0;}
API NP_GetSignature(void *p){if(p)memset(p,0,400);return 1; /* No unlicensed vendor signature. */}
API NP_RegisterWindowHandle(HWND w){(void)w;return 0;}
API NP_UnregisterWindowHandle(void){return 0;}
API NP_RequestData(unsigned short flags){(void)flags;return 0;}
API NP_StartDataTransmission(void){enabled=1;return 0;}
API NP_StopDataTransmission(void){enabled=0;return 0;}
API NP_StartCursor(void){return 0;}
API NP_StopCursor(void){return 0;}
API NP_ReCenter(void){wchar_t eventName[180];if(!GetEnvironmentVariableW(L"HEADRELAY_BRIDGE_RECENTER",eventName,180))lstrcpyW(eventName,L"Local\\HeadRelay.Recenter");HANDLE e=OpenEventW(EVENT_MODIFY_STATE,FALSE,eventName);if(e){SetEvent(e);CloseHandle(e);return 0;}return 1;}
API NP_GetParameter(int a,int b){(void)a;(void)b;return 0;}
API NP_SetParameter(int a,int b){(void)a;(void)b;return 0;}
API NPPriv_ClientNotify(void){return 0;}
API NPPriv_GetLastError(void){return 0;}
API NPPriv_SetData(void){return 0;}
API NPPriv_SetLastError(void){return 0;}
API NPPriv_SetParameter(void){return 0;}
API NPPriv_SetSignature(void){return 0;}
API NPPriv_SetVersion(void){return 0;}
API FTGetData(void *out){SharedPose s;if(!out||!snapshot(&s))return 0;memcpy(out,&s,92);return 1;}
const char * __stdcall FTGetDllVersion(void){return "HeadRelay bridge 0.2";}
const char * __stdcall FTProvider(void){return "HeadRelay";}
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID reserved){(void)reserved;if(reason==DLL_PROCESS_ATTACH)DisableThreadLibraryCalls(module);if(reason==DLL_PROCESS_DETACH){if(pose)UnmapViewOfFile(pose);if(mapping)CloseHandle(mapping);if(guard)CloseHandle(guard);}return TRUE;}
