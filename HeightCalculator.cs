using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Havok.Animation.Rig;
using FFXIVClientStructs.Havok.Common.Base.Math.Quaternion;
using FFXIVClientStructs.Havok.Common.Base.Math.Vector;
using ObjectType = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.ObjectType;

namespace CharacterHeight;

public enum AdultHeadSource {
    None,
    Cache,
    Table,
    OtherSexCache,
    OtherSexTable,
}

public readonly record struct BodyIdentity(byte Race, byte Tribe, byte Sex, bool Young, ushort RaceSexId) {
    public byte Clan => Race == 1 && Tribe >= 1 ? (byte)((Tribe - 1) % 2) : (byte)0;
}

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
            HeightUnit.Megametres => FormatMegametres(centimeters, decimals),
            HeightUnit.AstronomicalUnits => FormatAu(centimeters, decimals),
            HeightUnit.FeetInches => FormatFeet(centimeters, decimals),
            HeightUnit.Miles => FormatNumber(centimeters / 2.54f / (5280f * 12f), decimals) + " mi",
            _ => FormatNumber(centimeters, decimals) + " cm",
        };
    }

    private const double CentimetersPerMegametre = 100_000_000d;
    private const double CentimetersPerAu = 14_959_787_070_000d;
    private const double MetricMegametreCentimeters = 1_000_000_000d;
    private const double MetricAuCentimeters = 1650d * CentimetersPerMegametre;
    private const double ImperialAuCentimeters = 1_000_000d * 5280d * 12d * 2.54d;

    private static bool IsImperial(HeightUnit unit) => unit is HeightUnit.FeetInches or HeightUnit.Miles;

    private static string FormatMetric(float centimeters, int decimals) {
        var abs = Math.Abs((double)centimeters);
        if (abs >= MetricAuCentimeters) return FormatAu(centimeters, decimals);
        if (abs >= MetricMegametreCentimeters) return FormatMegametres(centimeters, decimals);
        if (abs > 100_000d) return FormatNumber(centimeters / 100_000f, decimals) + " km";
        if (abs > 1_000d) return FormatNumber(centimeters / 100f, decimals) + " m";
        if (abs < 0.1d) return FormatNumber(centimeters * 10_000f, decimals) + " µm";
        if (abs < 10d) return FormatNumber(centimeters * 10f, decimals) + " mm";
        return FormatNumber(centimeters, decimals) + " cm";
    }

    private static string FormatImperial(float centimeters, int decimals) {
        var abs = Math.Abs((double)centimeters);
        if (abs >= ImperialAuCentimeters) return FormatAu(centimeters, decimals);
        var inches = centimeters / 2.54f;
        var absInches = MathF.Abs(inches);
        if (absInches >= 5280f * 12f)
            return FormatNumber(inches / (5280f * 12f), decimals) + " mi";
        if (absInches < 12f)
            return FormatNumber(inches, decimals) + " in";
        return FormatFeet(centimeters, decimals);
    }

    private static string FormatMegametres(float centimeters, int decimals) =>
        FormatNumber((float)(centimeters / CentimetersPerMegametre), decimals) + " Mm";

    private static string FormatAu(float centimeters, int decimals) =>
        FormatNumber((float)(centimeters / CentimetersPerAu), decimals) + " AU";

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

    public static string FormatOffset(float centimeters, float totalCentimeters, HeightUnit unit, int decimals, bool auto) {
        var sign = centimeters < 0f ? "-" : "+";
        var abs = MathF.Abs(centimeters);
        if (ShowsFeet(unit, auto)) {
            if (abs / 2.54f < 12f)
                return sign + FormatNumber(abs / 2.54f, decimals) + " in";
            return sign + FormatFeet(abs, decimals);
        }

        var suffix = auto ? MetricSuffix(totalCentimeters) : UnitSuffix(unit);
        var scaled = suffix switch {
            "mm" => abs * 10f,
            "m" => abs / 100f,
            "km" => abs / 100_000f,
            "Mm" => (float)(abs / CentimetersPerMegametre),
            "AU" => (float)(abs / CentimetersPerAu),
            "µm" => abs * 10_000f,
            "mi" => abs / 2.54f / (5280f * 12f),
            _ => abs,
        };
        return sign + FormatNumber(scaled, decimals) + " " + suffix;
    }

    private static bool ShowsFeet(HeightUnit unit, bool auto) =>
        unit == HeightUnit.FeetInches || (auto && IsImperial(unit));

    private static string MetricSuffix(float centimeters) {
        var abs = Math.Abs((double)centimeters);
        if (abs >= MetricAuCentimeters) return "AU";
        if (abs >= MetricMegametreCentimeters) return "Mm";
        if (abs > 100_000d) return "km";
        if (abs > 1_000d) return "m";
        if (abs < 0.1d) return "µm";
        if (abs < 10d) return "mm";
        return "cm";
    }

    private static string UnitSuffix(HeightUnit unit) => unit switch {
        HeightUnit.Millimeters => "mm",
        HeightUnit.Meters => "m",
        HeightUnit.Kilometers => "km",
        HeightUnit.Megametres => "Mm",
        HeightUnit.AstronomicalUnits => "AU",
        HeightUnit.Miles => "mi",
        _ => "cm",
    };

    public static bool TryMeasure(GameObject* gameObject, Configuration config, out HeightSample sample) {
        sample = default;
        if (gameObject == null || !gameObject->IsCharacter()) return false;

        var character = (Character*)gameObject;
        var customize = character->DrawData.CustomizeData;
        var body = ResolveBody(gameObject);
        var height = EffectiveHeight(gameObject);
        if (!HeightTable.TryCreatorCentimeters(body.Race, body.Tribe, body.Sex, height, out var creatorCm))
            return false;

        TryReadScales(gameObject->DrawObject, out var modelScale, out var customScale);
        var local = Service.Objects.LocalPlayer;
        var isLocal = local != null && local.Address == (nint)gameObject;
        var racial = RacialFactor(modelScale, VanillaSkeletonScale(body.Race, body.Tribe, body.Sex, height), isLocal);
        var custom = CustomExtra(customScale);
        var stature = creatorCm * racial;
        var gameUnits = character->ModelContainer.CalculateHeight();
        var shown = stature * custom;
        var nameplate = character->Height;
        var young = body.Young || customize.BodyType == 4;
        var adult = !young && customize.BodyType == 1;
        if (TryRestHead(gameObject, out var restHead) && adult)
            RememberAdultHead(body, restHead);
        if (adult) {
            RememberAdult(body.Race, body.Tribe, body.Sex, stature, gameUnits, nameplate, shown, isLocal);
        } else if (young) {
            if (TrySkeletonScale(gameObject->DrawObject, out var skeletonScale)
                && TryYoungCentimeters(body, restHead, skeletonScale.Y, out var youngCm, out _, out _, out _))
                shown = youngCm;
        } else if (float.IsFinite(gameUnits) && gameUnits > 0.0001f
            && TryAdultRuler(body.Race, body.Tribe, body.Sex, out var ruler)) {
            shown = gameUnits * (ruler.Centimeters / ruler.GameUnits) * custom;
        }

        sample = new HeightSample(creatorCm, modelScale, racial, customScale, shown, gameUnits);
        return true;
    }

    public static bool TryModelRaceSex(DrawObject* drawObject, out ushort raceSexId) {
        raceSexId = 0;
        if (drawObject == null || drawObject->GetObjectType() != ObjectType.CharacterBase) return false;
        var characterBase = (CharacterBase*)drawObject;
        if (characterBase->GetModelType() != CharacterBase.ModelType.Human) return false;
        raceSexId = ((Human*)drawObject)->RaceSexId;
        return raceSexId >= 100;
    }

    // Skeleton pair (PP + 1) / 2 to race. Pair 2 is Hyur Highlander.
    private static ReadOnlySpan<byte> PairRaces => [1, 1, 2, 4, 5, 3, 6, 7, 8];

    // The skeleton id is cPPSS: odd PP is male, even PP is female, SS 04 is a young body.
    public static bool TryModelBody(ushort raceSexId, out BodyIdentity body) {
        body = default;
        var model = raceSexId / 100;
        if (model is < 1 or > 18) return false;
        var pair = (model + 1) / 2;
        var race = PairRaces[pair - 1];
        var clan = pair == 2 ? 1 : 0;
        var tribe = (byte)(race * 2 - 1 + clan);
        var sex = (byte)(model % 2 == 0 ? 1 : 0);
        body = new BodyIdentity(race, tribe, sex, raceSexId % 100 == 4, raceSexId);
        return true;
    }

    // NPC customize data can carry the wrong race and sex for the body that is actually loaded.
    public static BodyIdentity ResolveBody(GameObject* gameObject) {
        var customize = ((Character*)gameObject)->DrawData.CustomizeData;
        if (TryModelRaceSex(gameObject->DrawObject, out var raceSexId) && TryModelBody(raceSexId, out var body))
            return body;
        return new BodyIdentity(customize.Race, customize.Tribe, customize.Sex, customize.BodyType == 4, 0);
    }

    public readonly record struct HeelLift(float Units, bool Counted, string Note);

    // SimpleHeels writes its lift into DrawOffset. One game unit is one meter.
    public static bool TryHeelOffset(GameObject* gameObject, out float centimeters) {
        centimeters = 0f;
        if (!TryHeelLift(gameObject, out var lift) || !lift.Counted) return false;
        centimeters = lift.Units * 100f;
        return float.IsFinite(centimeters);
    }

    public static bool TryHeelLift(GameObject* gameObject, out HeelLift lift) {
        lift = default;
        if (gameObject == null || !gameObject->IsCharacter()) return false;
        var units = gameObject->DrawOffset.Y;
        if (!float.IsFinite(units)) return false;

        var character = (Character*)gameObject;
        var id = gameObject->EntityId;
        if (!IsStanding(character, out var skip)) {
            if (skip == "emote" && standingHeels.TryGetValue(id, out var held) && MathF.Abs(held) >= 0.05f) {
                lift = new HeelLift(held / 100f, true, "standing");
                return true;
            }

            lift = new HeelLift(units, false, skip);
            return true;
        }

        var centimeters = units * 100f;
        if (id != 0)
            standingHeels[id] = MathF.Abs(centimeters) < 0.05f ? 0f : centimeters;
        if (MathF.Abs(centimeters) < 0.05f) {
            lift = new HeelLift(units, false, "none");
            return true;
        }

        lift = new HeelLift(units, true, "counted");
        return true;
    }

    private static readonly Dictionary<uint, float> standingHeels = new();

    private static bool IsStanding(Character* character, out string skip) {
        switch (character->Mode) {
            case CharacterModes.EmoteLoop:
                skip = "emote";
                return false;
            case CharacterModes.InPositionLoop:
                skip = "sitting";
                return false;
            case CharacterModes.Mounted:
            case CharacterModes.RidingPillion:
                skip = "mounted";
                return false;
        }

        if (character->MoveController.IsSwimming || character->MoveController.IsFlying()) {
            skip = character->MoveController.IsFlying() ? "flying" : "swimming";
            return false;
        }

        skip = "";
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

    // A byte above 100 is unset NPC data. The live body scale is the slider those characters actually use.
    public static byte EffectiveHeight(GameObject* gameObject) {
        var customize = ((Character*)gameObject)->DrawData.CustomizeData;
        var body = ResolveBody(gameObject);
        if (body.Young || customize.BodyType == 4 || customize.Height <= 100) return customize.Height;

        TryReadScales(gameObject->DrawObject, out var modelScale, out _);
        var scale = modelScale;
        if (TrySkeletonScale(gameObject->DrawObject, out var skeleton)
            && float.IsFinite(skeleton.Y) && skeleton.Y > 0.0001f)
            scale = skeleton.Y;
        return SliderFromScale(body, scale);
    }

    private static byte SliderFromScale(BodyIdentity body, float scale) {
        var min = VanillaSkeletonScale(body.Race, body.Tribe, body.Sex, 0);
        var max = VanillaSkeletonScale(body.Race, body.Tribe, body.Sex, 100);
        var span = max - min;
        if (!float.IsFinite(scale) || !float.IsFinite(span) || span <= 0.0001f) return 0;
        var slider = Math.Clamp((scale - min) / span, 0f, 1f);
        return (byte)Math.Clamp((int)MathF.Round(slider * 100f), 0, 100);
    }

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
        public float Nameplate;
        public float ShownCentimeters;
    }

    private static readonly Dictionary<BodyKey, StatureRuler> adultRulers = new();
    private static StatureRuler localAdult;
    private static bool hasLocalAdult;

    private static void RememberAdult(byte race, byte tribe, byte sex, float statureCm, float gameUnits, float nameplate, float shownCm, bool isLocal) {
        if (!float.IsFinite(statureCm) || statureCm <= 1f) return;
        if (!float.IsFinite(gameUnits) || gameUnits <= 0.01f) return;
        var ruler = new StatureRuler {
            Centimeters = statureCm,
            GameUnits = gameUnits,
            ShownCentimeters = shownCm,
            Nameplate = float.IsFinite(nameplate) && nameplate > 0.01f ? nameplate : 0f,
        };
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

    private static readonly Dictionary<ushort, float> adultHeads = new();

    // Adult rest-pose j_kao height in model space by adult skeleton id, from the Debug target readout.
    private static readonly Dictionary<ushort, float> adultHeadTable = new() {
        [101] = 1.614f, [201] = 1.494f,
        [301] = 1.614f, [401] = 1.494f,
        [501] = 1.868f, [601] = 1.763f,
        [701] = 1.614f, [801] = 1.434f,
        [901] = 2.058f, [1001] = 1.763f,
        [1101] = 0.766f, [1201] = 0.766f,
        [1301] = 1.614f, [1401] = 1.434f,
        [1501] = 2.058f, [1601] = 1.763f,
        [1701] = 1.614f, [1801] = 1.494f,
    };

    // Race to skeleton pair. Hyur Highlander is the pair after Midlander.
    private static ReadOnlySpan<byte> RacePairs => [1, 3, 6, 4, 5, 7, 8, 9];

    public static bool TryAdultSkeletonId(BodyIdentity body, out ushort id) {
        id = 0;
        if (body.Race is < 1 or > 8 || body.Sex > 1) return false;
        var pair = RacePairs[body.Race - 1] + body.Clan;
        id = (ushort)((pair * 2 - 1 + body.Sex) * 100 + 1);
        return true;
    }

    private static BodyIdentity OtherSex(BodyIdentity body) =>
        body with { Sex = (byte)(body.Sex == 1 ? 0 : 1) };

    private static void RememberAdultHead(BodyIdentity body, float head) {
        if (!float.IsFinite(head) || head <= 0.01f) return;
        if (TryAdultSkeletonId(body, out var id))
            adultHeads[id] = head;
    }

    private static bool TryAdultHead(BodyIdentity body, out float head) {
        head = 0f;
        return TryAdultSkeletonId(body, out var id) && adultHeads.TryGetValue(id, out head) && head > 0.01f;
    }

    private static bool TryTableHead(BodyIdentity body, out float head) {
        head = 0f;
        return TryAdultSkeletonId(body, out var id) && adultHeadTable.TryGetValue(id, out head) && head > 0.01f;
    }

    public static bool TryBaseCentimeters(BodyIdentity body, out float centimeters) {
        centimeters = 0f;
        if (!HeightTable.TryGetRange(body.Race, body.Tribe, body.Sex, out var minCm, out _)) return false;
        var minScale = VanillaSkeletonScale(body.Race, body.Tribe, body.Sex, 0);
        if (!float.IsFinite(minScale) || minScale <= 0.0001f) return false;
        centimeters = minCm / minScale;
        return float.IsFinite(centimeters) && centimeters > 1f;
    }

    public static bool TryResolveAdultHead(BodyIdentity body, out float head, out AdultHeadSource source) {
        if (TryAdultHead(body, out head)) {
            source = AdultHeadSource.Cache;
            return true;
        }

        if (TryTableHead(body, out head)) {
            source = AdultHeadSource.Table;
            return true;
        }

        var other = OtherSex(body);
        if (TryAdultHead(other, out head)) {
            source = AdultHeadSource.OtherSexCache;
            return true;
        }

        if (TryTableHead(other, out head)) {
            source = AdultHeadSource.OtherSexTable;
            return true;
        }

        head = 0f;
        source = AdultHeadSource.None;
        return false;
    }

    public static bool TryYoungCentimeters(
        BodyIdentity body, float youngHead, float skeletonScaleY,
        out float centimeters, out float baseCm, out float adultHead, out AdultHeadSource source) {
        centimeters = 0f;
        baseCm = 0f;
        adultHead = 0f;
        source = AdultHeadSource.None;
        if (!float.IsFinite(youngHead) || youngHead <= 0.01f) return false;
        if (!float.IsFinite(skeletonScaleY) || skeletonScaleY <= 0.0001f) return false;
        if (!TryResolveAdultHead(body, out adultHead, out source)) return false;
        // A borrowed head belongs to the other sex, so use that sex's base. The two stay one cm-per-unit.
        var baseBody = source is AdultHeadSource.OtherSexCache or AdultHeadSource.OtherSexTable ? OtherSex(body) : body;
        if (!TryBaseCentimeters(baseBody, out baseCm)) return false;
        centimeters = youngHead * skeletonScaleY * (baseCm / adultHead);
        return float.IsFinite(centimeters) && centimeters > 1f;
    }

    public static bool TryRestHead(GameObject* gameObject, out float head) {
        head = 0f;
        if (gameObject == null) return false;
        var draw = gameObject->DrawObject;
        if (draw == null || draw->GetObjectType() != ObjectType.CharacterBase) return false;
        var skeleton = ((CharacterBase*)draw)->Skeleton;
        if (skeleton == null || skeleton->PartialSkeletonCount < 1 || skeleton->PartialSkeletons == null) return false;

        var pose = skeleton->PartialSkeletons[0].GetHavokPose(0);
        if (pose == null || pose->Skeleton == null) return false;
        var rig = pose->Skeleton;
        var count = rig->Bones.Length;
        if (count <= 0 || rig->ParentIndices.Length < count || rig->ReferencePose.Length < count) return false;

        var bone = -1;
        for (var i = 0; i < count; i++) {
            var name = rig->Bones[i].Name.String;
            if (string.Equals(name, "j_kao", StringComparison.Ordinal)) {
                bone = i;
                break;
            }
        }

        if (bone < 0) return false;
        var local = rig->ReferencePose[bone];
        var point = new Vector3(local.Translation.X, local.Translation.Y, local.Translation.Z);
        var parent = (int)rig->ParentIndices[bone];
        var guard = 0;
        while (parent >= 0 && parent < count && guard++ < 64) {
            var above = rig->ReferencePose[parent];
            point = new Vector3(point.X * above.Scale.X, point.Y * above.Scale.Y, point.Z * above.Scale.Z);
            point = Rotate(above.Rotation, point);
            point += new Vector3(above.Translation.X, above.Translation.Y, above.Translation.Z);
            parent = rig->ParentIndices[parent];
        }

        head = point.Y;
        return float.IsFinite(head) && head > 0.01f;
    }

    public static bool TrySkeletonScale(DrawObject* drawObject, out Vector3 scale) {
        scale = Vector3.One;
        if (drawObject == null || drawObject->GetObjectType() != ObjectType.CharacterBase) return false;
        var skeleton = ((CharacterBase*)drawObject)->Skeleton;
        if (skeleton == null) return false;
        scale = skeleton->Transform.Scale;
        return true;
    }

    private static Vector3 Rotate(hkQuaternionf rotation, Vector3 value) {
        var x = rotation.X;
        var y = rotation.Y;
        var z = rotation.Z;
        var w = rotation.W;
        var tx = 2f * (y * value.Z - z * value.Y);
        var ty = 2f * (z * value.X - x * value.Z);
        var tz = 2f * (x * value.Y - y * value.X);
        return new Vector3(
            value.X + w * tx + (y * tz - z * ty),
            value.Y + w * ty + (z * tx - x * tz),
            value.Z + w * tz + (x * ty - y * tx));
    }
}
