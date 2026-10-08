using System;
using System.Collections.Generic;

namespace HELLDRIVE;

/// <summary>
/// Example profiles. Replace config paths and keys with values appropriate
/// to the specific game. The reducer itself remains game-agnostic.
/// </summary>
public static class GameGraphicsProfiles
{
    public static GameGraphicsProfile Create(
        string gameId,
        string displayName,
        double targetFps = 60)
    {
        var profile = GameGraphicsProfile.CreateDefault(
            gameId,
            displayName);

        profile.TargetFps = targetFps;
        profile.LowFpsThreshold = targetFps - 8;
        profile.RecoveryFps = targetFps + 12;

        return profile;
    }

    public static Dictionary<GraphicsLevel, Dictionary<string, string>>
        ExampleIniLevels()
    {
        return new()
        {
            [GraphicsLevel.Native] = new(),
            [GraphicsLevel.Light] = new()
            {
                ["RenderScale"] = "95",
                ["ShadowQuality"] = "3"
            },
            [GraphicsLevel.Medium] = new()
            {
                ["RenderScale"] = "88",
                ["ShadowQuality"] = "2",
                ["VolumetricQuality"] = "2"
            },
            [GraphicsLevel.Low] = new()
            {
                ["RenderScale"] = "80",
                ["ShadowQuality"] = "1",
                ["VolumetricQuality"] = "1",
                ["EffectsQuality"] = "1"
            },
            [GraphicsLevel.Emergency] = new()
            {
                ["RenderScale"] = "70",
                ["ShadowQuality"] = "0",
                ["VolumetricQuality"] = "0",
                ["EffectsQuality"] = "0",
                ["ViewDistance"] = "0"
            }
        };
    }
}
