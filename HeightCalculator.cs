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

    public static string Format(float centimeters, HeightUnit unit, int decimals, bool auto) {
        if (auto)
            return IsImperial(unit) ? FormatImperial(centimeters, decimals) : FormatMetric(centimeters, decimals);

        return unit switch {
            HeightUnit.Millimeters => FormatNumber(centimeters * 10f, decimals) + " mm",
            HeightUnit.Meters => FormatNumber(centimeters / 100f, decimals) + " m",
            HeightUnit.Kilometers => FormatNumber(centimeters / 100_000f, decimals) + " km",
            HeightUnit.FeetInches => FormatFeet(centimeters, decimals),
            HeightUnit.Miles => FormatNumber(centimeters / 2.54f / (5280f * 12f), decimals) + " mi",
            _ => FormatNumber(centimeters, decimals) + " cm",
        };
    }

    private static bool IsImperial(HeightUnit unit) => unit is HeightUnit.FeetInches or HeightUnit.Miles;

    private static string FormatMetric(float centimeters, int decimals) {
        var abs = MathF.Abs(centimeters);
        if (abs > 100_000f) return FormatNumber(centimeters / 100_000f, decimals) + " km";
        if (abs > 1_000f) return FormatNumber(centimeters / 100f, decimals) + " m";
        if (abs < 0.1f) return FormatNumber(centimeters * 10_000f, decimals) + " µm";
        if (abs < 10f) return FormatNumber(centimeters * 10f, decimals) + " mm";
        return FormatNumber(centimeters, decimals) + " cm";
    }

    private static string FormatImperial(float centimeters, int decimals) {
        var inches = centimeters / 2.54f;
        var abs = MathF.Abs(inches);
        if (abs >= 5280f * 12f)
            return FormatNumber(inches / (5280f * 12f), decimals) + " mi";
        if (abs < 12f)
            return FormatNumber(inches, decimals) + " in";
        return FormatFeet(centimeters, decimals);
    }

    public static string FormatNumber(float value, int decimals) {
        decimals = Math.Clamp(decimals, 0, 3);
        var format = decimals switch {
            0 => "0",
            1 => "0.0",
            2 => "0.00",
            _ => "0.000",
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
        var custom = CustomExtra(customScale);
        var stature = creatorCm * racial;
        var gameUnits = character->ModelContainer.CalculateHeight();
        var shown = stature * custom;
        if (customize.BodyType == 1) {
            RememberAdult(customize.Race, customize.Tribe, customize.Sex, stature, gameUnits, isLocal);
        } else if (float.IsFinite(gameUnits) && gameUnits > 0.0001f
            && TryAdultRuler(customize.Race, customize.Tribe, customize.Sex, out var ruler)) {
            shown = gameUnits * (ruler.Centimeters / ruler.GameUnits) * custom;
        }

        sample = new HeightSample(creatorCm, modelScale, racial, customScale, shown, gameUnits);
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
        if (float.IsFinite(seenCustom) && seenCustom > 0f)
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

    private static float CustomExtra(float scale) {
        if (!float.IsFinite(scale) || scale <= 0f) return 1f;
        if (MathF.Abs(scale - 1f) <= 0.001f) return 1f;
        return scale;
    }

    private readonly record struct BodyKey(byte Race, byte Tribe, byte Sex);

    private struct StatureRuler {
        public float Centimeters;
        public float GameUnits;
    }

    private static readonly Dictionary<BodyKey, StatureRuler> adultRulers = new();
    private static StatureRuler localAdult;
    private static bool hasLocalAdult;

    private static void RememberAdult(byte race, byte tribe, byte sex, float statureCm, float gameUnits, bool isLocal) {
        if (!float.IsFinite(statureCm) || statureCm <= 1f) return;
        if (!float.IsFinite(gameUnits) || gameUnits <= 0.01f) return;
        var ruler = new StatureRuler { Centimeters = statureCm, GameUnits = gameUnits };
        adultRulers[new BodyKey(race, tribe, sex)] = ruler;
        if (!isLocal) return;
        localAdult = ruler;
        hasLocalAdult = true;
    }

    private static bool TryAdultRuler(byte race, byte tribe, byte sex, out StatureRuler ruler) {
        if (adultRulers.TryGetValue(new BodyKey(race, tribe, sex), out ruler)) return true;
        if (!hasLocalAdult) return false;
        ruler = localAdult;
        return true;
    }
}
