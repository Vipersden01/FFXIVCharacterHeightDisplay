using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
#if DEBUG
using Dalamud.Game.ClientState.Objects.Types;
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
#endif

        if (ImGui.CollapsingHeader("Changelog")) {
            ImGui.TextWrapped("1.1.8.3: Party heights follow the name on each row, so the leader's slot and a local reorder no longer swap them.");
            ImGui.TextWrapped("1.1.8.2: Party list height labels can be nudged, and a cutscene name no longer replaces that member's height.");
            ImGui.TextWrapped("1.1.7: Up to 3 decimal places. Tiny custom scales still count. Child bodies are measured from an adult. Target height text sits on top of the HP bar. Auto conversion is metric or imperial, and fixed units include mm, cm, m, km, feet, and miles.");
            ImGui.TextWrapped("1.1.6: Party heights stay up while a member is casting.");
            ImGui.TextWrapped("1.1.5: Auto unit conversion, the updated config, and /displayheight.");
        }

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
#endif
}
