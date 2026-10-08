namespace HellDrive;

public static class ProfileEngine
{
    public static void ApplyPreset(GameProfile p, PresetLevel preset)
    {
        p.Preset = preset;
        switch (preset)
        {
            case PresetLevel.Safe:
                p.Resolution = "1280 x 720"; p.Priority = "Normal"; p.ManageBackground = false; break;
            case PresetLevel.Balanced:
                p.Resolution = "1280 x 720"; p.Priority = "Above Normal"; p.ManageBackground = false; break;
            case PresetLevel.Aggressive:
                p.Resolution = "854 x 480"; p.Priority = "High"; p.ManageBackground = true; break;
            case PresetLevel.Ultra:
                p.Resolution = "640 x 360"; p.Priority = "High"; p.ManageBackground = true; break;
        }
    }
}
