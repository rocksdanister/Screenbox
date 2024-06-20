using ComputeSharp;
using ComputeSharp.Uwp;
using Drizzle.UI.Shared.Shaders;
using Screenbox.Core.Shaders.Helpers;
using Screenbox.Core.Shaders.Models;
using System;

namespace Screenbox.Core.Shaders.Runners;

// Copyright (c) Dani John
// Licensed under the GNU General Public License v3.0.
// See the LICENSE file in the project root for more information.
// Source: https://github.com/rocksdanister/lively
public sealed class TunnelRunner : IShaderRunner
{
    private readonly Func<TunnelModel> properties;
    private readonly TunnelModel currentProperties;
    private ReadOnlyTexture2D<Rgba32, float4> image;
    private double simulatedTime, previousTime;

    public TunnelRunner()
    {
        this.properties ??= () => new TunnelModel();
        this.currentProperties ??= new TunnelModel();
        image = currentProperties.Image;
    }

    public TunnelRunner(Func<TunnelModel> properties) : this()
    {
        this.properties = properties;
        this.currentProperties = new(properties());
    }

    public bool TryExecute(IReadWriteNormalizedTexture2D<float4> texture, TimeSpan timespan, object parameter)
    {
        if (currentProperties.Image != properties().Image || this.image is null || this.image.GraphicsDevice != texture.GraphicsDevice)
        {
            this.image?.Dispose();
            currentProperties.Image = properties().Image;
            this.image = currentProperties.Image ?? ShaderUtil.GetDefaultTexture(texture.GraphicsDevice);
        }

        UpdateProperties();
        // Adjust delta instead of actual time/speed to avoid rewinding time
        simulatedTime += (timespan.TotalSeconds - previousTime) * currentProperties.TimeMultiplier;
        previousTime = timespan.TotalSeconds;

        texture.GraphicsDevice.ForEach(texture, new Tunnel((float)simulatedTime,
            image,
            currentProperties.Brightness,
            currentProperties.Speed,
            currentProperties.IsSquare,
            currentProperties.GradientColor));

        return true;

    }

    private void UpdateProperties()
    {
        // Smoothing, value is increased by small step % every frame
        currentProperties.TimeMultiplier = ShaderUtil.Lerp(currentProperties.TimeMultiplier, properties().TimeMultiplier, 0.05f);
        currentProperties.Brightness = ShaderUtil.Lerp(currentProperties.Brightness, properties().Brightness, 0.05f);
        // Other
        currentProperties.GradientColor = properties().GradientColor;
        currentProperties.IsSquare = properties().IsSquare;
        currentProperties.Speed = properties().Speed;
    }
}
