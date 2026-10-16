using Vortice.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace AppCapture.Core.Graphics;

/// <summary>デバッグ用: BGRA8 テクスチャを PNG に保存する</summary>
public static class TextureDump
{
    /// <summary>GPU から読み戻す（呼び出し側で D3DContext.Lock を取ること）</summary>
    public static (byte[] Pixels, int Width, int Height) Read(D3DContext d3d, ID3D11Texture2D texture)
    {
        var desc = texture.Description;
        desc.Usage = ResourceUsage.Staging;
        desc.BindFlags = BindFlags.None;
        desc.CPUAccessFlags = CpuAccessFlags.Read;
        desc.MiscFlags = ResourceOptionFlags.None;
        using var staging = d3d.Device.CreateTexture2D(desc);
        d3d.Context.CopyResource(staging, texture);

        int w = (int)desc.Width, h = (int)desc.Height;
        var pixels = new byte[w * h * 4];
        var map = d3d.Context.Map(staging, 0, MapMode.Read);
        try
        {
            unsafe
            {
                for (int y = 0; y < h; y++)
                    new ReadOnlySpan<byte>((byte*)map.DataPointer + y * map.RowPitch, w * 4).CopyTo(pixels.AsSpan(y * w * 4));
            }
        }
        finally
        {
            d3d.Context.Unmap(staging, 0);
        }
        return (pixels, w, h);
    }

    public static async Task SavePngAsync(byte[] pixels, int width, int height, string path)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();

        var bytes = new byte[stream.Size];
        stream.Seek(0);
        using (var reader = new DataReader(stream))
        {
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
        }
        await File.WriteAllBytesAsync(path, bytes);
    }
}
