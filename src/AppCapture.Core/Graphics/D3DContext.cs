using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace AppCapture.Core.Graphics;

/// <summary>
/// キャプチャ・合成・出力で共有する D3D11 デバイス。
/// イミディエイトコンテキストは複数スレッドから使うので、触るときは必ず <see cref="Lock"/> を取ること。
/// </summary>
public sealed class D3DContext : IDisposable
{
    public D3DContext()
    {
        D3D11.D3D11CreateDevice(
            null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
            out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
        Device = device;
        Context = context;

        // WGC のフレーム到着スレッドなど、ロック外からのアクセスに対する保険
        using (var mt = Device.QueryInterface<ID3D11Multithread>())
            mt.SetMultithreadProtected(true);

        using var dxgiDevice = Device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var inspectable));
        try
        {
            WinRTDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }

    /// <summary>WGC のフレームプールに渡す WinRT 側のデバイス</summary>
    public IDirect3DDevice WinRTDevice { get; }

    public object Lock { get; } = new();

    public void Dispose()
    {
        WinRTDevice.Dispose();
        Context.Dispose();
        Device.Dispose();
    }

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
}
