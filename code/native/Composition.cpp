#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <dcomp.h>
#include <wrl/client.h>
#include <vector>
#include <cmath>
#include <algorithm>
#include <cstring>
using Microsoft::WRL::ComPtr;
struct Surface {
    HWND window{}, owner{};
    void(__cdecl* input)(UINT,UINT)=nullptr;
    int maskWidth=0,maskHeight=0;bool mirror=false;
    std::vector<unsigned char> alpha;
    int regionWidth=0,regionHeight=0;bool regionMirror=false,regionValid=false;
    ComPtr<IDCompositionDevice> device;
    ComPtr<IDCompositionTarget> target;
    ComPtr<IDCompositionVisual> visual;
    ComPtr<IDCompositionScaleTransform> scale;
};
static bool Occupied(const Surface* s,int x,int y) {
    if(x<0||y<0||x>=s->maskWidth||y>=s->maskHeight)return false;
    for(int yy=std::max(0,y-1);yy<=std::min(s->maskHeight-1,y+1);++yy)
        for(int xx=std::max(0,x-1);xx<=std::min(s->maskWidth-1,x+1);++xx)
            if(s->alpha[yy*s->maskWidth+xx]>=16)return true;
    return false;
}
// HTTRANSPARENT alone only continues hit testing within this thread. Exclude
// transparent pixels from the native HWND so other processes receive real input.
// Expand the low-resolution mask by one sample to retain antialiased edges.
static HRESULT UpdateRegion(Surface* s) {
    RECT client{};GetClientRect(s->window,&client);
    int width=client.right,height=client.bottom;
    if(s->maskWidth<=0||s->maskHeight<=0||width<=0||height<=0)return S_OK;
    if(s->regionValid&&width==s->regionWidth&&height==s->regionHeight&&s->mirror==s->regionMirror)return S_OK;
    std::vector<RECT> rectangles;
    for(int y=0;y<s->maskHeight;++y) {
        int top=y*height/s->maskHeight,bottom=(y+1)*height/s->maskHeight;
        if(top==bottom)continue;
        for(int x=0;x<s->maskWidth;) {
            int sample=s->mirror?s->maskWidth-1-x:x;
            if(!Occupied(s,sample,y)){++x;continue;}
            int first=x++;
            while(x<s->maskWidth&&Occupied(s,s->mirror?s->maskWidth-1-x:x,y))++x;
            int left=first*width/s->maskWidth,right=x*width/s->maskWidth;
            if(left<right)rectangles.push_back({left,top,right,bottom});
        }
    }
    HRGN region=nullptr;
    if(rectangles.empty())region=CreateRectRgn(0,0,0,0);
    else {
        std::vector<unsigned char> buffer(sizeof(RGNDATAHEADER)+rectangles.size()*sizeof(RECT));
        auto data=reinterpret_cast<RGNDATA*>(buffer.data());
        data->rdh.dwSize=sizeof(RGNDATAHEADER);data->rdh.iType=RDH_RECTANGLES;
        data->rdh.nCount=(DWORD)rectangles.size();data->rdh.nRgnSize=(DWORD)(rectangles.size()*sizeof(RECT));
        data->rdh.rcBound={0,0,width,height};
        std::memcpy(data->Buffer,rectangles.data(),rectangles.size()*sizeof(RECT));
        region=ExtCreateRegion(nullptr,(DWORD)buffer.size(),data);
    }
    if(!region)return HRESULT_FROM_WIN32(GetLastError()?GetLastError():ERROR_INVALID_DATA);
    if(!SetWindowRgn(s->window,region,TRUE)) {auto error=GetLastError();DeleteObject(region);return HRESULT_FROM_WIN32(error?error:ERROR_INVALID_DATA);}
    // Successful SetWindowRgn transfers ownership of the region to Windows.
    s->regionWidth=width;s->regionHeight=height;s->regionMirror=s->mirror;s->regionValid=true;
    return S_OK;
}
static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM w, LPARAM l) {
    if(message==WM_NCCREATE) SetWindowLongPtrW(window,GWLP_USERDATA,(LONG_PTR)((CREATESTRUCT*)l)->lpCreateParams);
    auto surface=(Surface*)GetWindowLongPtrW(window,GWLP_USERDATA);
    if(surface && message==WM_NCHITTEST) {
        POINT p{(short)LOWORD(l),(short)HIWORD(l)};ScreenToClient(window,&p);RECT r{};GetClientRect(window,&r);
        if(r.right<=0||r.bottom<=0||surface->alpha.empty())return HTTRANSPARENT;
        if(p.x<0||p.y<0||p.x>=r.right||p.y>=r.bottom)return HTTRANSPARENT;
        int x=((p.x+1)*surface->maskWidth-1)/r.right,y=((p.y+1)*surface->maskHeight-1)/r.bottom;
        if(surface->mirror)x=surface->maskWidth-1-x;
        return Occupied(surface,x,y)?HTCLIENT:HTTRANSPARENT;
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
    // surface_shape on the same UI thread updates the region before presentation.
}
extern "C" __declspec(dllexport) HRESULT surface_shape(Surface* s,const unsigned char* pixels,int width,int height,int stride,int mirror) {
    if(!pixels||width<=0||height<=0||width>4096||height>4096||stride<width*4)return E_INVALIDARG;
    std::vector<unsigned char> next(width*height);
    for(int y=0;y<height;y++)for(int x=0;x<width;x++)next[y*width+x]=pixels[y*stride+x*4+3];
    if(s->maskWidth!=width||s->maskHeight!=height||s->alpha!=next)s->regionValid=false;
    s->maskWidth=width;s->maskHeight=height;s->mirror=mirror!=0;s->alpha.swap(next);
    return UpdateRegion(s);
}
extern "C" __declspec(dllexport) void surface_show(Surface* s,int visible) {if(!visible&&GetCapture()==s->window)ReleaseCapture();ShowWindow(s->window,visible?SW_SHOWNOACTIVATE:SW_HIDE);}
