using System.Runtime.InteropServices;
using AppCapture.Core.Graphics;
using AppCapture.Core.Tracking;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using RectI = AppCapture.Core.Tracking.RectI;

namespace AppCapture.Core.Composition;

/// <summary>合成する 1 レイヤー。Bounds は仮想デスクトップ座標（物理ピクセル）</summary>
public readonly record struct Layer(ID3D11Texture2D Texture, int Left, int Top, int Width, int Height);

/// <summary>
/// キャンバス（仮想デスクトップ上の矩形）に、ウィンドウのテクスチャを下から順に描く。
/// 背景は透明。DWM のサーフェスは乗算済みアルファなので、premultiplied の over 合成をする。
/// </summary>
public sealed class Compositor : IDisposable
{
    private const string ShaderSource = """
        cbuffer Params : register(b0) { float4 dst; }; // left, top, right, bottom (NDC)
        Texture2D tex : register(t0);
        SamplerState samp : register(s0);
        struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
        VSOut VS(uint id : SV_VertexID)
        {
            float2 uv = float2(id & 1, id >> 1);
            VSOut o;
            o.pos = float4(lerp(dst.x, dst.z, uv.x), lerp(dst.y, dst.w, uv.y), 0, 1);
            o.uv = uv;
            return o;
        }
        float4 PS(VSOut i) : SV_Target { return tex.Sample(samp, i.uv); }
        """;

    private readonly D3DContext _d3d;
    private readonly ID3D11VertexShader _vs;
    private readonly ID3D11PixelShader _ps;
    private readonly ID3D11Buffer _params;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11BlendState _blend;
    private readonly ID3D11RasterizerState _rasterizer;
    private readonly Dictionary<nint, ID3D11ShaderResourceView> _srvCache = [];
    private ID3D11RenderTargetView? _rtv;

    public Compositor(D3DContext d3d)
    {
        _d3d = d3d;
        var device = d3d.Device;

        var vsCode = Compiler.Compile(ShaderSource, "VS", "Compositor.hlsl", "vs_4_0");
        var psCode = Compiler.Compile(ShaderSource, "PS", "Compositor.hlsl", "ps_4_0");
        _vs = device.CreateVertexShader(vsCode.Span);
        _ps = device.CreatePixelShader(psCode.Span);

        _params = device.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        // 1:1 で描くので point サンプリング（サイズ変更の過渡期に伸びてもにじませない）
        _sampler = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipPoint, TextureAddressMode.Clamp));

        var blend = new BlendDescription();
        blend.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.One,
            DestinationBlend = Blend.InverseSourceAlpha,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.InverseSourceAlpha,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        _blend = device.CreateBlendState(blend);
        _rasterizer = device.CreateRasterizerState(RasterizerDescription.CullNone);
    }

    /// <summary>合成結果（キャンバスサイズの BGRA8）。読むときは D3DContext.Lock を取ること</summary>
    public ID3D11Texture2D? Output { get; private set; }

    /// <summary>
    /// layers を下（奥）から上（手前）の順に描く。呼び出し側で D3DContext.Lock を取ること。
    /// </summary>
    public ID3D11Texture2D Render(RectI canvas, IReadOnlyList<Layer> bottomToTop)
    {
        EnsureOutput(canvas.Width, canvas.Height);
        var ctx = _d3d.Context;

        ctx.ClearRenderTargetView(_rtv!, new Color4(0, 0, 0, 0));
        ctx.OMSetRenderTargets(_rtv!);
        ctx.OMSetBlendState(_blend);
        ctx.RSSetViewport(0, 0, canvas.Width, canvas.Height);
        ctx.RSSetState(_rasterizer);
        ctx.IASetInputLayout(null);
        ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        ctx.VSSetShader(_vs);
        ctx.VSSetConstantBuffer(0, _params);
        ctx.PSSetShader(_ps);
        ctx.PSSetSampler(0, _sampler);

        var used = new HashSet<nint>();
        foreach (var layer in bottomToTop)
        {
            int x = layer.Left - canvas.Left, y = layer.Top - canvas.Top;
            if (x >= canvas.Width || y >= canvas.Height || x + layer.Width <= 0 || y + layer.Height <= 0) continue;

            var p = new Params(
                x * 2f / canvas.Width - 1f,
                1f - y * 2f / canvas.Height,
                (x + layer.Width) * 2f / canvas.Width - 1f,
                1f - (y + layer.Height) * 2f / canvas.Height);
            var mapped = ctx.Map(_params, MapMode.WriteDiscard);
            Marshal.StructureToPtr(p, mapped.DataPointer, false);
            ctx.Unmap(_params);

            used.Add(layer.Texture.NativePointer);
            ctx.PSSetShaderResource(0, GetSrv(layer.Texture));
            ctx.Draw(4, 0);
        }

        ctx.PSSetShaderResource(0, null!);
        ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!);

        // 作り直されたテクスチャの SRV を捨てる
        foreach (var key in _srvCache.Keys.Where(k => !used.Contains(k)).ToList())
        {
            _srvCache[key].Dispose();
            _srvCache.Remove(key);
        }
        return Output!;
    }

    private ID3D11ShaderResourceView GetSrv(ID3D11Texture2D texture)
    {
        if (!_srvCache.TryGetValue(texture.NativePointer, out var srv))
        {
            srv = _d3d.Device.CreateShaderResourceView(texture);
            _srvCache[texture.NativePointer] = srv;
        }
        return srv;
    }

    private void EnsureOutput(int width, int height)
    {
        if (Output is { } o && o.Description.Width == width && o.Description.Height == height) return;
        _rtv?.Dispose();
        Output?.Dispose();
        Output = _d3d.Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
        });
        _rtv = _d3d.Device.CreateRenderTargetView(Output);
    }

    public void Dispose()
    {
        foreach (var srv in _srvCache.Values) srv.Dispose();
        _srvCache.Clear();
        _rtv?.Dispose();
        Output?.Dispose();
        _rasterizer.Dispose();
        _blend.Dispose();
        _sampler.Dispose();
        _params.Dispose();
        _ps.Dispose();
        _vs.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Params(float Left, float Top, float Right, float Bottom);
}
