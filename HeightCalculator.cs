using System.Globalization;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using ObjectType = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.ObjectType;

namespace CharacterHeight;

public readonly record struct HeightSample(
    float CreatorCentimeters,
    float ModelScale,
    float RacialScale,
    float CustomScale,
    float ShownCentimeters,
    float GameUnits);

public static unsafe class HeightCalculator {
    [StructLayout(LayoutKind.Explicit, Size = 0x2A8)]
    private struct CharacterHeightScale {
        [FieldOffset(0x2A4)] public float ModelScale;
    }

    public static string Format(float centimeters, HeightUnit unit, int decimals) {
        return unit switch {
            HeightUnit.Meters => FormatNumber(centimeters / 100f, decimals) + " m",
            HeightUnit.FeetInches => FormatFeet(centimeters, decimals),
            _ => FormatNumber(centimeters, decimals) + " cm",
        };
    }

    public static string FormatNumber(float value, int decimals) {
        decimals = Math.Clamp(decimals, 0, 2);
        var format = decimals switch {
            0 => "0",
            1 => "0.0",
            _ => "0.00",
        };
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    public static string FormatFeet(float centimeters, int decimals) {
        var totalInches = centimeters / 2.54f;
        var feet = (int)MathF.Floor(totalInches / 12f);
        var inches = totalInches - feet * 12f;
        if (decimals <= 0) {
            var rounded = (int)MathF.Round(inches);
            if (rounded >= 12) {
                feet++;
                rounded = 0;
            }

            return $"{feet}'{rounded}\"";
        }

        return $"{feet}'{FormatNumber(inches, decimals)}\"";
    }

    public static bool TryMeasure(GameObject* gameObject, Configuration config, out HeightSample sample) {
        sample = default;
        if (gameObject == null || !gameObject->IsCharacter()) return false;

        var character = (Character*)gameObject;
        var customize = character->DrawData.CustomizeData;
        if (!HeightTable.TryCreatorCentimeters(customize.Race, customize.Tribe, customize.Sex, customize.Height, out var creatorCm))
            return false;

        TryReadScales(gameObject->DrawObject, out var modelScale, out var customScale);
        var local = Service.Objects.LocalPlayer;
        var isLocal = local != null && local.Address == (nint)gameObject;
        var racial = RacialFactor(modelScale, VanillaSkeletonScale(customize.Race, customize.Tribe, customize.Sex, customize.Height), isLocal);
        var shown = creatorCm * racial * CustomExtra(customScale, config.ApplyCustomScale);
        sample = new HeightSample(creatorCm, modelScale, racial, customScale, shown, character->ModelContainer.CalculateHeight());
        return true;
    }

    public static bool TryReadScales(DrawObject* drawObject, out float modelScale, out float customScale) {
        modelScale = 1f;
        customScale = 1f;
        if (drawObject == null || drawObject->GetObjectType() != ObjectType.CharacterBase) return false;

        var scales = (CharacterHeightScale*)drawObject;
        if (float.IsFinite(scales->ModelScale) && scales->ModelScale > 0.0001f)
            modelScale = scales->ModelScale;

        var seenCustom = drawObject->Object.Scale.Z;
        if (float.IsFinite(seenCustom) && seenCustom > 0.0001f)
            customScale = seenCustom;

        return true;
    }

    // Vanilla human.cmp body-size endpoints. Index is race, then clan (Hyur Highlander is the second row).
    // Male min, male max, female min, female max. The slider lerps these; the creator centimeters already include that.
    private static ReadOnlySpan<float> VanillaScales => [
        0.96f, 1.04f, 0.96f, 1.04f,
        1.056f, 1.144f, 1.056f, 1.144f,
        0.961f, 1.039f, 0.961f, 1.039f,
        0.961f, 1.039f, 0.961f, 1.039f,
        0.945f, 1.055f, 0.945f, 1.055f,
        0.945f, 1.055f, 0.945f, 1.055f,
        0.91f, 0.99f, 0.96f, 1.04f,
        0.91f, 0.99f, 0.96f, 1.04f,
        0.962f, 1.038f, 1f, 1.16f,
        0.962f, 1.038f, 1f, 1.16f,
        1.16f, 1.24f, 0.93f, 1.01f,
        1.16f, 1.24f, 0.93f, 1.01f,
        0.892f, 0.968f, 0.986f, 1.066f,
        0.892f, 0.968f, 0.986f, 1.066f,
        0.984f, 1.066f, 1.111f, 1.189f,
        0.984f, 1.066f, 1.111f, 1.189f,
    ];

    private static float VanillaSkeletonScale(byte race, byte tribe, byte sex, byte height) {
        if (race is < 1 or > 8) return 1f;
        var clan = tribe >= 1 ? (tribe - 1) % 2 : 0;
        var index = ((race - 1) * 2 + clan) * 4 + (sex == 1 ? 2 : 0);
        var min = VanillaScales[index];
        var max = VanillaScales[index + 1];
        var slider = Math.Clamp(height, (byte)0, (byte)100) / 100f;
        return min + (max - min) * slider;
    }

    private static float RacialFactor(float live, float vanilla, bool isLocal) {
        if (!float.IsFinite(live) || live <= 0.0001f) return 1f;
        if (!float.IsFinite(vanilla) || vanilla <= 0.0001f) return 1f;
        // Other characters often keep 1 here even though their body already matches the creator height.
        if (!isLocal && MathF.Abs(live - 1f) <= 0.001f) return 1f;
        var ratio = live / vanilla;
        if (!float.IsFinite(ratio) || MathF.Abs(ratio - 1f) <= 0.001f) return 1f;
        return ratio;
    }

    private static float CustomExtra(float scale, bool apply) {
        if (!apply || !float.IsFinite(scale) || scale <= 0.0001f) return 1f;
        if (MathF.Abs(scale - 1f) <= 0.001f) return 1f;
        return scale;
    }
}
