using CommunityToolkit.Mvvm.ComponentModel;
using ComputeSharp;
using Screenbox.Core.Enums;
using System.IO;

namespace Screenbox.Core.Shaders.Models;

// Copyright (c) Dani John
// Licensed under the GNU General Public License v3.0.
// See the LICENSE file in the project root for more information.
// Source: https://github.com/rocksdanister/lively
public abstract class BaseModel : ObservableObject
{
    public ShaderTypes Type { get; }
    public float Brightness { get; set; } = 1f;
    public float TimeMultiplier { get; set; } = 1f;
    public bool IsAlbumArt { get; set; }
    public ReadOnlyTexture2D<Rgba32, float4> Image { get; set; }
    public float3 GradientColor { get; set; } = new float3();

    protected BaseModel(ShaderTypes type, bool isAlbumArt)
    {
        this.Type = type;
        this.IsAlbumArt = isAlbumArt;
    }
}
