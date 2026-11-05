#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <dcomp.h>
#include <wrl/client.h>
#include <vector>
#include <cmath>
using Microsoft::WRL::ComPtr;
struct Surface {
    HWND window{}, owner{};
    void(__cdecl* input)(UINT,UINT)=nullptr;
    int maskWidth=0,maskHeight=0;bool mirror=false;
    std::vector<unsigned char> alpha;
    ComPtr<IDCompositionDevice> device;
    ComPtr<IDCompositionTarget> target;
    ComPtr<IDCompositionVisual> visual;
    ComPtr<IDCompositionScaleTransform> scale;
};
static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM w, LPARAM l) {
    if(message==WM_NCCREATE) SetWindowLongPtrW(window,GWLP_USERDATA,(LONG_PTR)((CREATESTRUCT*)l)->lpCreateParams);
    auto surface=(Surface*)GetWindowLongPtrW(window,GWLP_USERDATA);
    if(surface && message==WM_NCHITTEST) {
        POINT p{(short)LOWORD(l),(short)HIWORD(l)};ScreenToClient(window,&p);RECT r{};GetClientRect(window,&r);
        if(r.right<=0||r.bottom<=0||surface->alpha.empty())return HTTRANSPARENT;
        int x=p.x*surface->maskWidth/r.right,y=p.y*surface->maskHeight/r.bottom;
        if(surface->mirror)x=surface->maskWidth-1-x;
        return x>=0&&y>=0&&x<surface->maskWidth&&y<surface->maskHeight&&surface->alpha[y*surface->maskWidth+x]>=16?HTCLIENT:HTTRANSPARENT;
    }
    if(message==WM_MOUSEACTIVATE) return MA_NOACTIVATE;
    if(message==WM_ERASEBKGND) return 1;
    if(surface && message==WM_CAPTURECHANGED && surface->input){surface->input(message,0);return 0;}
    if(surface && message>=WM_MOUSEFIRST && message<=WM_MOUSELAST) {
        if(message==WM_LBUTTONDOWN||message==WM_LBUTTONDBLCLK)SetCapture(window);
        if(surface->input)surface->input(message,(UINT)w);
        if(message==WM_LBUTTONUP && GetCapture()==window)ReleaseCapture();return 0;
    }
    return DefWindowProcW(window,message,w,l);
}
extern "C" __declspec(dllexport) void surface_destroy(Surface* s) {
    if(!s)return;s->target.Reset();s->visual.Reset();s->scale.Reset();s->device.Reset();
    if(s->window)DestroyWindow(s->window);delete s;
}
extern "C" __declspec(dllexport) HRESULT surface_create(HWND owner,void(__cdecl* input)(UINT,UINT),Surface** output) {
    *output=nullptr;
    static const ATOM atom=[] {WNDCLASSW c{};c.style=CS_DBLCLKS;c.hInstance=GetModuleHandleW(nullptr);c.lpfnWndProc=WindowProc;c.lpszClassName=L"BigBlueFish.Composition";c.hCursor=LoadCursor(nullptr,IDC_ARROW);return RegisterClassW(&c);}();
    if(!atom)return HRESULT_FROM_WIN32(GetLastError());
    auto s=new Surface();s->owner=owner;s->input=input;
    s->window=CreateWindowExW(WS_EX_NOREDIRECTIONBITMAP|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,L"BigBlueFish.Composition",L"蓝色大肥鱼 · 动画",WS_POPUP,0,0,1,1,owner,nullptr,GetModuleHandleW(nullptr),s);
    HRESULT hr=s->window?S_OK:HRESULT_FROM_WIN32(GetLastError());
    if(SUCCEEDED(hr))hr=DCompositionCreateDevice(nullptr,__uuidof(IDCompositionDevice),(void**)s->device.GetAddressOf());
    if(SUCCEEDED(hr))hr=s->device->CreateTargetForHwnd(s->window,TRUE,&s->target);
    if(SUCCEEDED(hr))hr=s->device->CreateVisual(&s->visual);
    if(SUCCEEDED(hr))hr=s->device->CreateScaleTransform(&s->scale);
    if(SUCCEEDED(hr))hr=s->visual->SetTransform(s->scale.Get());
    if(SUCCEEDED(hr))hr=s->target->SetRoot(s->visual.Get());
    if(FAILED(hr)){surface_destroy(s);return hr;}*output=s;return S_OK;
}
extern "C" __declspec(dllexport) HRESULT surface_attach(Surface* s,void* swapchain) {
    HRESULT hr=s->visual->SetContent((IUnknown*)swapchain);if(FAILED(hr))return hr;return s->device->Commit();
}
extern "C" __declspec(dllexport) void surface_bounds(Surface* s,int left,int top,int width,int height,int mirror) {
    SetWindowPos(s->window,nullptr,left,top,width,height,SWP_NOACTIVATE|SWP_NOZORDER);
    s->scale->SetScaleX((mirror?-1.f:1.f)*width/1280);s->scale->SetScaleY(height/720.f);
    s->visual->SetOffsetX(mirror?(float)width:0.f);s->device->Commit();
}
extern "C" __declspec(dllexport) void surface_shape(Surface* s,const unsigned char* pixels,int width,int height,int stride,int mirror) {
    s->maskWidth=width;s->maskHeight=height;s->mirror=mirror!=0;s->alpha.resize(width*height);
    for(int y=0;y<height;y++)for(int x=0;x<width;x++)s->alpha[y*width+x]=pixels[y*stride+x*4+3];
}
extern "C" __declspec(dllexport) void surface_show(Surface* s,int visible) {if(!visible&&GetCapture()==s->window)ReleaseCapture();ShowWindow(s->window,visible?SW_SHOWNOACTIVATE:SW_HIDE);}
