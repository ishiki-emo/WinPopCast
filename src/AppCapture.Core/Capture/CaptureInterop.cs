using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace AppCapture.Core.Capture;

/// <summary>WGC と D3D11 の間の COM interop（ComImport を使わず vtable を直接呼ぶ）</summary>
internal static unsafe class CaptureInterop
{
    private static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid IID_IGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid IID_ID3D11Texture2D = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    public static GraphicsCaptureItem CreateItemForWindow(nint hwnd) => CreateItem(hwnd, slot: 3);

    public static GraphicsCaptureItem CreateItemForMonitor(nint hmonitor) => CreateItem(hmonitor, slot: 4);

    private static GraphicsCaptureItem CreateItem(nint handle, int slot)
    {
        using var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        Guid iidInterop = IID_IGraphicsCaptureItemInterop;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(factory.ThisPtr, in iidInterop, out var interop));
        try
        {
            // IGraphicsCaptureItemInterop: [3] CreateForWindow(HWND, REFIID, void**), [4] CreateForMonitor(HMONITOR, REFIID, void**)
            var fn = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)(*(nint**)interop)[slot];
            Guid iidItem = IID_IGraphicsCaptureItem;
            nint itemPtr;
            Marshal.ThrowExceptionForHR(fn(interop, handle, &iidItem, &itemPtr));
            try
            {
                return GraphicsCaptureItem.FromAbi(itemPtr);
            }
            finally
            {
                Marshal.Release(itemPtr);
            }
        }
        finally
        {
            Marshal.Release(interop);
        }
    }

    /// <summary>WGC フレームの IDirect3DSurface から ID3D11Texture2D を取り出す（呼び出し側で Dispose）</summary>
    public static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        var objRef = ((IWinRTObject)surface).NativeObject;
        Guid iidAccess = IID_IDirect3DDxgiInterfaceAccess;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(objRef.ThisPtr, in iidAccess, out var access));
        try
        {
            // IDirect3DDxgiInterfaceAccess: [3] GetInterface(REFIID, void**)
            var fn = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)access)[3];
            Guid iidTex = IID_ID3D11Texture2D;
            nint tex;
            Marshal.ThrowExceptionForHR(fn(access, &iidTex, &tex));
            return new ID3D11Texture2D(tex);
        }
        finally
        {
            Marshal.Release(access);
        }
    }
}
