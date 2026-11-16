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

        var unit = (int)config.Unit;
        if (ImGui.Combo("Unit", ref unit, "Centimeters\0Meters\0Feet / inches\0")) {
            config.Unit = (HeightUnit)unit;
            changed = true;
        }

        changed |= Checkbox("Auto unit conversion", config.CondenseHugeSizes, value => config.CondenseHugeSizes = value);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Huge sizes become km, Mm, and miles. Tiny sizes become mm, μm, inches, and mils.");

        var decimals = config.Decimals;
        if (ImGui.SliderInt("Decimals", ref decimals, 0, 2)) {
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

        if (changed)
            config.Save();
    }

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
