using CommunityToolkit.Mvvm.ComponentModel;
using Screenbox.Core.Enums;

namespace Screenbox.Core.Shaders.Models;

// Copyright (c) Dani John
// Licensed under the GNU General Public License v3.0.
// See the LICENSE file in the project root for more information.
// Source: https://github.com/rocksdanister/lively
public partial class TunnelModel : BaseModel
{
    [ObservableProperty]
    private float speed = 1f;

    [ObservableProperty]
    private bool isSquare = false;

    public TunnelModel() : base(ShaderTypes.tunnel, true) { }

    public TunnelModel(TunnelModel properties) : base(ShaderTypes.tunnel, true)
    {
        this.IsSquare = properties.IsSquare;
        this.speed = properties.Speed;
    }
}
