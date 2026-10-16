using System.Diagnostics;
using WinPopCast.Core.Graphics;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Foundation.Metadata;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace WinPopCast.Core.Capture;

/// <summary>
/// 1 ウィンドウ分の WGC セッション。届いたフレームは自前のテクスチャへコピーして保持する。
/// <see cref="Texture"/> を読むときは <see cref="D3DContext.Lock"/> を取ること。
/// </summary>
public sealed class WindowCapture : IDisposable
{
    private readonly D3DContext _d3d;
    private readonly GraphicsCaptureItem _item;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private SizeInt32 _poolSize;
    private bool _disposed;

    private WindowCapture(D3DContext d3d, nint hwnd, GraphicsCaptureItem item, long startTimestamp)
    {
        _d3d = d3d;
        Handle = hwnd;
        _item = item;
        _poolSize = item.Size;
        StartTimestamp = startTimestamp;

        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            d3d.WinRTDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
        _pool.FrameArrived += OnFrameArrived;
        _item.Closed += (_, _) => Closed?.Invoke(this);

        _session = _pool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = false;
        if (IsBorderOptional) _session.IsBorderRequired = false;
        _session.StartCapture();
        StartedTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>黄色い枠を消せる環境か（Windows 11 以降）</summary>
    public static bool IsBorderOptional { get; } =
        ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired");

    /// <summary>枠なしキャプチャの許可を求める。非パッケージアプリでは即許可されるが、呼ばないと IsBorderRequired=false が無視される</summary>
    public static async Task RequestBorderlessAccessAsync()
    {
        if (!IsBorderOptional) return;
        try { await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless); }
        catch { /* 古いビルドでは存在しない。枠が出るだけなので無視 */ }
    }

    public static WindowCapture Start(D3DContext d3d, nint hwnd)
    {
        long ts = Stopwatch.GetTimestamp();
        return new(d3d, hwnd, CaptureInterop.CreateItemForWindow(hwnd), ts);
    }

    public nint Handle { get; }

    /// <summary>最新フレームのコピー（まだ届いていなければ null）</summary>
    public ID3D11Texture2D? Texture { get; private set; }

    /// <summary>最新フレームの内容サイズ（物理ピクセル）</summary>
    public SizeInt32 ContentSize { get; private set; }

    public long FrameCount { get; private set; }

    /// <summary>Start を呼んだ時刻（Stopwatch）</summary>
    public long StartTimestamp { get; }

    /// <summary>StartCapture から戻った時刻（Stopwatch）。StartTimestamp との差がセッション生成のコスト</summary>
    public long StartedTimestamp { get; }

    /// <summary>最初のフレームが届いた時刻（Stopwatch）。0 なら未着</summary>
    public long FirstFrameTimestamp { get; private set; }

    /// <summary>キャプチャ対象のウィンドウが閉じられた（フレームプールのスレッドで発火）</summary>
    public event Action<WindowCapture>? Closed;

    /// <summary>最初のフレームが届いた（フレームプールのスレッドで発火。開始遅延の計測用）</summary>
    public event Action<WindowCapture>? FirstFrameArrived;

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = sender.TryGetNextFrame();
        if (frame is null) return;

        var size = frame.ContentSize;
        bool first = false;
        lock (_d3d.Lock)
        {
            if (_disposed) return;

            using (var src = CaptureInterop.GetTexture(frame.Surface))
            {
                EnsureTexture(size);
                // フレームプールのテクスチャはプールサイズのまま。内容がある範囲だけ切り出す
                int w = Math.Min(size.Width, _poolSize.Width);
                int h = Math.Min(size.Height, _poolSize.Height);
                _d3d.Context.CopySubresourceRegion(Texture!, 0, 0, 0, 0, src, 0, new Box(0, 0, 0, w, h, 1));
            }
            ContentSize = size;
            FrameCount++;
            if (FirstFrameTimestamp == 0)
            {
                FirstFrameTimestamp = Stopwatch.GetTimestamp();
                first = true;
            }
        }
        if (first) FirstFrameArrived?.Invoke(this);

        // ウィンドウのサイズが変わったらプールを作り直す（次のフレームから新サイズで届く）
        if (size.Width != _poolSize.Width || size.Height != _poolSize.Height)
        {
            _poolSize = size;
            sender.Recreate(_d3d.WinRTDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
        }
    }

    private void EnsureTexture(SizeInt32 size)
    {
        if (Texture is { } t && t.Description.Width == size.Width && t.Description.Height == size.Height) return;
        Texture?.Dispose();
        Texture = _d3d.Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)Math.Max(1, size.Width),
            Height = (uint)Math.Max(1, size.Height),
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
        });
    }

    public void Dispose()
    {
        lock (_d3d.Lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _pool.FrameArrived -= OnFrameArrived;
        _session.Dispose();
        _pool.Dispose();
        lock (_d3d.Lock)
        {
            Texture?.Dispose();
            Texture = null;
        }
    }
}
