using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

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

        var unit = (int)config.Unit;
        if (ImGui.Combo("Unit", ref unit, "Centimeters\0Meters\0Feet / inches\0")) {
            config.Unit = (HeightUnit)unit;
            changed = true;
        }

        var decimals = config.Decimals;
        if (ImGui.SliderInt("Decimals", ref decimals, 0, 2)) {
            config.Decimals = decimals;
            changed = true;
        }

        var side = (int)config.Side;
        if (ImGui.Combo("Party list side", ref side, "Follow the list\0Always left\0Always right\0")) {
            config.Side = (PartyLabelSide)side;
            changed = true;
        }

        changed |= Checkbox("Show party", config.ShowParty, value => config.ShowParty = value);
        changed |= Checkbox("Show target", config.ShowTarget, value => config.ShowTarget = value);
        changed |= Checkbox("Flash height changes", config.FlashChanges, value => config.FlashChanges = value);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When a height goes up or down, the number juts out and glows green or red, then settles as the change slows.");
        changed |= Checkbox("Your height on the world bar", config.ShowOwnOnWorldBar, value => config.ShowOwnOnWorldBar = value);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Shows your height on the server info bar and hides it on your party row. Move that bar to the top right in Dalamud's settings.");
        changed |= Checkbox("Hide in combat", config.HideInCombat, value => config.HideInCombat = value);
        changed |= Checkbox("Hide in cutscenes", config.HideInCutscene, value => config.HideInCutscene = value);

        ImGui.Separator();
        ImGui.TextDisabled("Racial scale mods apply as the change from the vanilla skeleton scale. An unmodded model scale is already in the creator height.");

        changed |= Checkbox("Apply custom scale", config.ApplyCustomScale, value => config.ApplyCustomScale = value);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("DrawObject scale Z. Special Snowflakes, SizeChange, and similar plugins write this.");

        ImGui.Separator();
        DrawLocalReadout();

        if (changed)
            config.Save();
    }

    private static bool Checkbox(string label, bool value, Action<bool> set) {
        if (!ImGui.Checkbox(label, ref value)) return false;
        set(value);
        return true;
    }

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

        ImGui.Text($"Shown: {HeightCalculator.Format(sample.ShownCentimeters, config.Unit, config.Decimals)}");
        ImGui.Text($"Creator: {sample.CreatorCentimeters:0.0} cm");
        ImGui.Text($"Model scale: {sample.ModelScale:0.000}    Racial factor: {sample.RacialScale:0.000}    Custom scale: {sample.CustomScale:0.000}");
        ImGui.Text($"Game CalculateHeight: {sample.GameUnits:0.000}");
        ImGui.TextDisabled("CalculateHeight is the game's own float, not meters. Use it to check the scales above.");
    }
}
