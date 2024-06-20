using ComputeSharp;
using Screenbox.Core.ViewModels;
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Screenbox.Controls;

// Copyright (c) Dani John
// Licensed under the GNU General Public License v3.0.
// See the LICENSE file in the project root for more information.
// Source: https://github.com/rocksdanister/lively
public sealed partial class ComputeSharpVisualizerPlayer : UserControl
{
    public ShaderRunnerViewModel Shader
    {
        get { return (ShaderRunnerViewModel)GetValue(ShaderProperty); }
        set { SetValue(ShaderProperty, value); }
    }

    public static readonly DependencyProperty ShaderProperty =
        DependencyProperty.Register("Shader", typeof(ShaderRunnerViewModel), typeof(ComputeSharpVisualizerPlayer), new PropertyMetadata(null));

    public MediaViewModel Media
    {
        get { return (MediaViewModel)GetValue(MediaProperty); }
        set
        {
            SetValue(MediaProperty, value);
            _ = UpdateMusic();
        }
    }

    public static readonly DependencyProperty MediaProperty =
        DependencyProperty.Register("Media", typeof(MediaViewModel), typeof(ComputeSharpVisualizerPlayer), new PropertyMetadata(null));

    public ComputeSharpVisualizerPlayer()
    {
        this.InitializeComponent();
    }

    private async Task UpdateMusic()
    {
        if (Shader is null)
            return;

        float3 gradientColor;
        ReadOnlyTexture2D<Rgba32, float4> texture;
        if (Media is null)
        {
            var installFolder = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
            var assetsFolder = await StorageFolder.GetFolderFromPathAsync(Path.Combine(installFolder, "Assets"));
            var defaultTextureFile = await StorageFile.GetFileFromPathAsync(Path.Combine(assetsFolder.Path, "Default-texture-bg.jpg"));

            texture = GraphicsDevice.GetDefault().LoadReadOnlyTexture2D<Rgba32, float4>(defaultTextureFile.Path);
            gradientColor = new float3(32f/256, 24f/256, 35f/256);
        }
        else
        {
            var imgBytes = await GetThumbnailBytesAsync(Media.ThumbnailSource);

            texture = await ConvertToTexture2DAsync(imgBytes);
            gradientColor = await GetAverageColorFromThumbnailAsync(Media.ThumbnailSource);
        }
        Shader.ShaderProperties.Image = texture;
        Shader.ShaderProperties.GradientColor = gradientColor;
    }

    public async Task<byte[]> GetThumbnailBytesAsync(StorageItemThumbnail item)
    {
        if (item is null)
            return null;

        using var memoryStream = new MemoryStream();
        using var inputStream = item.CloneStream();

        var buffer = new byte[inputStream.Size];
        await inputStream.ReadAsync(buffer.AsBuffer(), (uint)inputStream.Size, InputStreamOptions.None);

        await memoryStream.WriteAsync(buffer, 0, buffer.Length);
        memoryStream.Seek(0, SeekOrigin.Begin);
        return memoryStream.ToArray();
    }

    public async Task<ReadOnlyTexture2D<Rgba32, float4>> ConvertToTexture2DAsync(byte[] imageBytes)
    {
        if (imageBytes is null)
            return null;

        using var stream = new MemoryStream(imageBytes);
        var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
        var pixels = await decoder.GetPixelDataAsync();

        byte[] pixelBytes = pixels.DetachPixelData();
        int width = (int)decoder.PixelWidth;
        int height = (int)decoder.PixelHeight;

        // Convert byte array to RGBA format
        var rgbaPixels = new Rgba32[width * height];
        for (int i = 0; i < rgbaPixels.Length; i++)
        {
            rgbaPixels[i] = new Rgba32(
                pixelBytes[i * 4 + 2],
                pixelBytes[i * 4 + 1],
                pixelBytes[i * 4 + 0],
                pixelBytes[i * 4 + 3]
            );
        }
        var texture = GraphicsDevice.GetDefault().AllocateReadOnlyTexture2D<Rgba32, float4>(width, height);
        texture.CopyFrom(rgbaPixels);

        return texture;
    }

    public async Task<float3> GetAverageColorFromThumbnailAsync(StorageItemThumbnail thumbnail)
    {
        if (thumbnail is null)
            return new float3();

        using var stream = thumbnail.AsStreamForRead();
        stream.Seek(0, SeekOrigin.Begin);
        var image = await SixLabors.ImageSharp.Image.LoadAsync<SixLabors.ImageSharp.PixelFormats.Rgba32>(stream);

        long r = 0;
        long g = 0;
        long b = 0;
        long pixelCount = image.Width * image.Height;

        for (int x = 0; x < image.Width; x++)
        {
            for (int y = 0; y < image.Height; y++)
            {
                var pixel = image[x, y];
                r += pixel.R;
                g += pixel.G;
                b += pixel.B;
            }
        }
        byte avgR = (byte)(r / pixelCount);
        byte avgG = (byte)(g / pixelCount);
        byte avgB = (byte)(b / pixelCount);

        return new float3(avgR/256f, avgG/256f, avgB/256f);
    }
}
