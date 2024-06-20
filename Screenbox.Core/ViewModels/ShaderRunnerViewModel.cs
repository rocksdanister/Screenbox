using CommunityToolkit.Mvvm.ComponentModel;
using ComputeSharp.Uwp;
using Screenbox.Core.Enums;
using Screenbox.Core.Shaders.Models;

namespace Screenbox.Core.ViewModels;

// Copyright (c) Dani John
// Licensed under the GNU General Public License v3.0.
// See the LICENSE file in the project root for more information.
// Source: https://github.com/rocksdanister/lively
public sealed partial class ShaderRunnerViewModel : ObservableObject
{
    public ShaderRunnerViewModel(IShaderRunner shaderRunner,
        BaseModel properties,
        ShaderTypes type,
        float scaleFactor,
        float maxScaleFactor)
    {
        this.ShaderRunner = shaderRunner;
        this.ShaderProperties = properties;
        this.ShaderType = type;
        this.ScaleFactor = scaleFactor;
        this.MaxScaleFactor = maxScaleFactor;
    }

    public IShaderRunner ShaderRunner { get; }

    public BaseModel ShaderProperties { get; }

    public ShaderTypes ShaderType { get; }

    public float ScaleFactor { get; }

    public float MaxScaleFactor { get; }

    [ObservableProperty]
    private bool isSelected;
}
