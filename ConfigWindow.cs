using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
#if DEBUG
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
#endif

namespace CharacterHeight;

public sealed class ConfigWindow : Window {
    private readonly Configuration config;

    public ConfigWindow(Configuration config) : base("Height Display###HeightDisplayConfig") {
        this.config = config;
        RespectCloseHotkey = true;
        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(460, 420);
    }

    public override void Draw() {
        var changed = false;

        if (config.CondenseHugeSizes) {
            var family = config.Unit is HeightUnit.FeetInches or HeightUnit.Miles ? 1 : 0;
            if (ImGui.Combo("Unit", ref family, "Metric\0Imperial\0")) {
                var imperial = family == 1;
                var wasImperial = config.Unit is HeightUnit.FeetInches or HeightUnit.Miles;
                if (imperial != wasImperial) {
                    config.Unit = imperial ? HeightUnit.FeetInches : HeightUnit.Centimeters;
                    changed = true;
                }
            }
        } else {
            var unit = UnitIndex(config.Unit);
            if (ImGui.Combo("Unit", ref unit, "Millimeters\0Centimeters\0Meters\0Kilometers\0Feet / inches\0Miles\0")) {
                config.Unit = UnitFromIndex(unit);
                changed = true;
            }
        }

        changed |= Checkbox("Auto unit conversion", config.CondenseHugeSizes, value => config.CondenseHugeSizes = value);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Metric steps through mm, cm, m, and km. Imperial steps through inches, feet, and miles.");

        var decimals = config.Decimals;
        if (ImGui.SliderInt("Decimals", ref decimals, 0, 3)) {
            config.Decimals = decimals;
            changed = true;
        }

        var textScale = config.TextScale <= 0f ? 1f : config.TextScale;
        if (ImGui.SliderFloat("Text size", ref textScale, 0.75f, 2f, "%.2fx")) {
            config.TextScale = textScale;
            changed = true;
        }

        var side = config.Side == PartyLabelSide.Left ? 0 : 1;
        if (ImGui.Combo("Party list side", ref side, "Always left\0Always right\0")) {
            config.Side = side == 0 ? PartyLabelSide.Left : PartyLabelSide.Right;
            changed = true;
        }

        var partyOffsetX = config.PartyOffsetX;
        if (ImGui.SliderFloat("Party offset X", ref partyOffsetX, -80f, 80f, "%.0f")) {
            config.PartyOffsetX = partyOffsetX;
            changed = true;
        }

        var partyOffsetY = config.PartyOffsetY;
        if (ImGui.SliderFloat("Party offset Y", ref partyOffsetY, -80f, 80f, "%.0f")) {
            config.PartyOffsetY = partyOffsetY;
            changed = true;
        }

        changed |= Checkbox("Show party sizes", config.ShowParty, value => config.ShowParty = value);
        changed |= Checkbox("Show target size", config.ShowTarget, value => config.ShowTarget = value);
        changed |= Checkbox("Flashy Height Text", config.FlashChanges, value => config.FlashChanges = value);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Special text effects when increasing / decreasing size");
        changed |= Checkbox("World Bar Height Display", config.ShowOwnOnWorldBar, value => config.ShowOwnOnWorldBar = value);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Shows your height on the server info bar and hides it on your party row.");
        changed |= Checkbox("Hide in combat", config.HideInCombat, value => config.HideInCombat = value);
        changed |= Checkbox("Hide in cutscenes", config.HideInCutscene, value => config.HideInCutscene = value);

#if DEBUG
        ImGui.Separator();
        DrawLocalReadout();
        DrawTargetReadout();
#endif

        if (ImGui.CollapsingHeader("Changelog"))
            ImGui.TextWrapped("1.2: UI refresh, updated how UI draws on the screen to behave nicer with other elements and fixed a few small visual bugs. Release Prep.");

        if (changed)
            config.Save();
    }

    private static int UnitIndex(HeightUnit unit) => unit switch {
        HeightUnit.Millimeters => 0,
        HeightUnit.Centimeters => 1,
        HeightUnit.Meters => 2,
        HeightUnit.Kilometers => 3,
        HeightUnit.FeetInches => 4,
        _ => 5,
    };

    private static HeightUnit UnitFromIndex(int index) => index switch {
        0 => HeightUnit.Millimeters,
        1 => HeightUnit.Centimeters,
        2 => HeightUnit.Meters,
        3 => HeightUnit.Kilometers,
        4 => HeightUnit.FeetInches,
        _ => HeightUnit.Miles,
    };

    private static bool Checkbox(string label, bool value, Action<bool> set) {
        if (!ImGui.Checkbox(label, ref value)) return false;
        set(value);
        return true;
    }

#if DEBUG
    private unsafe void DrawLocalReadout() {
        ImGui.Text("Local character");
        var player = Service.Objects.LocalPlayer;
        if (player == null) {
            ImGui.TextDisabled("Log in to compare against the character creator.");
            return;
        }

        if (!HeightCalculator.TryMeasure((GameObject*)player.Address, config, out var sample)) {
            ImGui.TextDisabled("No playable race height on the current model.");
            return;
        }

        ImGui.Text($"Shown: {HeightCalculator.Format(sample.ShownCentimeters, config.Unit, config.Decimals, config.CondenseHugeSizes)}");
        ImGui.Text($"Creator: {sample.CreatorCentimeters:0.0} cm");
        ImGui.Text($"Model scale: {sample.ModelScale:0.000}    Racial factor: {sample.RacialScale:0.000}    Custom scale: {sample.CustomScale:0.000}");
        ImGui.Text($"Game CalculateHeight: {sample.GameUnits:0.000}");
        ImGui.TextDisabled("CalculateHeight is the game's own float, not meters. Use it to check the scales above.");
    }

    private unsafe void DrawTargetReadout() {
        ImGui.Separator();
        ImGui.Text("Target");
        var target = Service.Targets.Target;
        if (target == null) {
            ImGui.TextDisabled("Target someone to read their skeleton.");
            return;
        }

        var gameObject = (GameObject*)target.Address;
        if (gameObject == null || !gameObject->IsCharacter()) {
            ImGui.TextDisabled("Target is not a character.");
            return;
        }

        var character = (Character*)gameObject;
        var customize = character->DrawData.CustomizeData;
        var body = HeightCalculator.ResolveBody(gameObject);
        var isYoung = body.Young || customize.BodyType == 4;
        var model = body.RaceSexId != 0 ? $"c{body.RaceSexId:D4}" : "none";
        ImGui.Text(target.Name.TextValue);
        ImGui.Text($"Body {customize.BodyType}  height byte {customize.Height}");
        ImGui.Text($"Race {body.Race} (data {customize.Race})  clan {body.Clan} (data tribe {customize.Tribe})  sex {body.Sex} (data {customize.Sex})");
        ImGui.Text($"Model: {model}  {(isYoung ? "young" : "adult")}");
        if (!HeightCalculator.TryMeasure(gameObject, config, out var sample)) {
            ImGui.TextDisabled("No playable race height on the target.");
            return;
        }

        var hasSkeleton = HeightCalculator.TrySkeletonScale(gameObject->DrawObject, out var scale);
        var skeletonScale = hasSkeleton ? $"{scale.X:0.000}, {scale.Y:0.000}, {scale.Z:0.000}" : "none";
        var hasRestHead = HeightCalculator.TryRestHead(gameObject, out var head);
        var restHead = hasRestHead ? $"{head:0.000}" : "none";
        float youngCm = 0f, usedBase = 0f, usedHead = 0f;
        var usedSource = AdultHeadSource.None;
        var youngMeasured = isYoung && hasSkeleton && hasRestHead
            && HeightCalculator.TryYoungCentimeters(
                body, head, scale.Y,
                out youngCm, out usedBase, out usedHead, out usedSource);
        var baseCm = youngMeasured
            ? $"{usedBase:0.0} cm"
            : HeightCalculator.TryBaseCentimeters(body, out var ownBase)
                ? $"{ownBase:0.0} cm"
                : "none";
        var adultHead = youngMeasured
            ? $"{usedHead:0.000} ({AdultHeadSourceLabel(usedSource)})"
            : HeightCalculator.TryResolveAdultHead(body, out var resolvedHead, out var source)
                ? $"{resolvedHead:0.000} ({AdultHeadSourceLabel(source)})"
                : "none";
        var young = youngMeasured ? $"{youngCm:0.00} cm" : "none";

        ImGui.Text($"Creator: {sample.CreatorCentimeters:0.0} cm    Shown: {sample.ShownCentimeters:0.00} cm");
        ImGui.Text($"Model scale: {sample.ModelScale:0.000}    Racial factor: {sample.RacialScale:0.000}    Custom scale: {sample.CustomScale:0.000}");
        ImGui.Text($"Skeleton scale: {skeletonScale}");
        ImGui.Text($"Nameplate: {character->Height:0.000}    CalculateHeight: {sample.GameUnits:0.000}");
        ImGui.Text($"Rest head: {restHead}    Adult head: {adultHead}");
        ImGui.Text($"Base: {baseCm}    Young result: {young}");
        if (!isYoung && customize.BodyType == 1 && hasRestHead) {
            var tableLine = $"Table: race {body.Race} sex {body.Sex} clan {body.Clan} head {head:0.000} model {model}";
            ImGui.Text(tableLine);
            ImGui.SameLine();
            if (ImGui.SmallButton("Copy##AdultHeadTableLine"))
                ImGui.SetClipboardText(tableLine);
        }
    }

    private static string AdultHeadSourceLabel(AdultHeadSource source) => source switch {
        AdultHeadSource.Cache => "cache",
        AdultHeadSource.Table => "table",
        AdultHeadSource.OtherSexCache => "other sex, cache",
        AdultHeadSource.OtherSexTable => "other sex, table",
        _ => "none",
    };
#endif
}
