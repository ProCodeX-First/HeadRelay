// SPDX-License-Identifier: MIT
/* HeadRelay process-presence helper; original Axis code. */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
int WINAPI WinMain(HINSTANCE a,HINSTANCE b,LPSTR cmd,int show){(void)a;(void)b;(void)cmd;(void)show;int count=0;LPWSTR *args=CommandLineToArgvW(GetCommandLineW(),&count);if(!args||count!=2){if(args)LocalFree(args);return 2;}DWORD pid=0;for(wchar_t *p=args[1];*p;p++){if(*p<L'0'||*p>L'9'){LocalFree(args);return 2;}pid=pid*10+(*p-L'0');}LocalFree(args);HANDLE parent=OpenProcess(SYNCHRONIZE,FALSE,pid);if(!parent)return 3;WaitForSingleObject(parent,INFINITE);CloseHandle(parent);return 0;}