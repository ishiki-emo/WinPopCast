using System.Runtime.InteropServices;
using System.Text;
using AppCapture.Core.Graphics;
using Vortice.Direct3D11;

namespace AppCapture.Spout;

/// <summary>
/// Spout2 送信者（native/SpoutBridge 経由で SpoutDX を使う）。
/// テクスチャのサイズが変わると Spout 側で共有テクスチャが作り直される。
/// </summary>
public sealed class SpoutSender : IDisposable
{
    private readonly D3DContext _d3d;
    private nint _handle;

    public SpoutSender(D3DContext d3d, string senderName)
    {
        _d3d = d3d;
        Name = senderName;
        lock (d3d.Lock)
            _handle = Native.SpoutBridge_Create(Encoding.UTF8.GetBytes(senderName + "\0"), d3d.Device.NativePointer);
        if (_handle == 0) throw new InvalidOperationException($"Spout 送信者 '{senderName}' を作成できませんでした");
    }

    public string Name { get; }

    /// <summary>BGRA8 テクスチャを送信する。呼び出し側で D3DContext.Lock を取っていなければここで取る</summary>
    public bool Send(ID3D11Texture2D texture)
    {
        lock (_d3d.Lock)
            return _handle != 0 && Native.SpoutBridge_SendTexture(_handle, texture.NativePointer);
    }

    public void Dispose()
    {
        lock (_d3d.Lock)
        {
            if (_handle == 0) return;
            Native.SpoutBridge_Destroy(_handle);
            _handle = 0;
        }
    }

    private static class Native
    {
        private const string Dll = "AppCaptureSpoutBridge.dll";

        [DllImport(Dll)]
        public static extern nint SpoutBridge_Create(byte[] senderName, nint device);

        [DllImport(Dll)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool SpoutBridge_SendTexture(nint handle, nint texture);

        [DllImport(Dll)]
        public static extern void SpoutBridge_Destroy(nint handle);
    }
}
