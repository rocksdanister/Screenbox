using ComputeSharp;

namespace Screenbox.Core.Shaders.Helpers;

// Copyright (c) Dani John
// Licensed under the GNU General Public License v3.0.
// See the LICENSE file in the project root for more information.
// Source: https://github.com/rocksdanister/lively
public static class ShaderUtil
{
    public static ReadOnlyTexture2D<Rgba32, float4> GetDefaultTexture(GraphicsDevice device)
    {
        return device.AllocateReadOnlyTexture2D<Rgba32, float4>(1, 1);
    }

    public static float Lerp(float start, float target, float by) => start * (1 - by) + target * by;
}
