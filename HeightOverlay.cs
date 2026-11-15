using System;
using System.Collections.Generic;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.ManagedFontAtlas;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Bounds = FFXIVClientStructs.FFXIV.Common.Math.Bounds;

namespace CharacterHeight;

public sealed unsafe class HeightOverlay : IDisposable {
    private const double HoldSeconds = 0.22;
    private const float JutSeconds = 0.15f;
    private const float RestSeconds = 1.05f;
    private const string OwnHeightTitle = "Character height";
    private static readonly string[] TargetAddonNames = ["_TargetInfoMainTarget", "_TargetInfo"];

    private readonly Configuration config;
    private readonly IFontHandle axisFont;
    private readonly HashSet<string> partyNamesDrawn = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HeightMemory> heights = new(StringComparer.OrdinalIgnoreCase);
    private IDtrBarEntry? ownHeightEntry;
    private string ownLabel = string.Empty;
    private ActiveFlash ownFlash;

    public HeightOverlay(Configuration config, IFontHandle axisFont) {
        this.config = config;
        this.axisFont = axisFont;
    }

    public void Dispose() {
        if (ownHeightEntry == null) return;
        ownHeightEntry.Remove();
        ownHeightEntry = null;
    }

    public void Draw() {
        if (Service.Objects.LocalPlayer == null) {
            HideOwnHeightBar();
            return;
        }

        UpdateOwnHeightBar();

        if (!axisFont.Available
            || Service.GameGui.GameUiHidden
            || (config.HideInCombat && Service.Condition[ConditionFlag.InCombat])
            || (config.HideInCutscene && Service.Condition.Any(ConditionFlag.WatchingCutscene, ConditionFlag.WatchingCutscene78))) {
            RestoreOwnHeightText();
            return;
        }

        using var locked = axisFont.Lock();
        var font = locked.ImFont;
        if (font.IsNull) {
            RestoreOwnHeightText();
            return;
        }

        partyNamesDrawn.Clear();
        if (config.ShowParty)
            DrawParty(font);

        if (config.ShowTarget)
            DrawTarget(font);

        DrawOwnHeightFlash(font);
        PruneHeights(ImGui.GetTime());
    }

    private void DrawParty(ImFontPtr font) {
        var addon = Service.GameGui.GetAddonByName<AddonPartyList>("_PartyList");
        if (addon == null || !addon->IsVisible || !addon->IsReady) return;

        Bounds rootBounds;
        addon->GetRootBounds(&rootBounds);
        if (rootBounds.Width <= 0 || rootBounds.Height <= 0) return;

        var drawLeft = config.Side switch {
            PartyLabelSide.Left => true,
            PartyLabelSide.Right => false,
            _ => rootBounds.CenterX > ImGui.GetIO().DisplaySize.X * 0.5f,
        };

        var members = addon->PartyMembers;
        var memberCount = Math.Clamp(addon->MemberCount, 0, members.Length);
        for (var i = 0; i < memberCount; i++)
            DrawPartyRow(font, members[i].Name, members[i].NameAndBarsContainer, rootBounds, drawLeft, i);

        var trusts = addon->TrustMembers;
        var trustCount = Math.Clamp(addon->TrustCount, 0, trusts.Length);
        for (var i = 0; i < trustCount; i++)
            DrawPartyRow(font, trusts[i].Name, trusts[i].NameAndBarsContainer, rootBounds, drawLeft, null);
    }

    private void DrawPartyRow(ImFontPtr font, AtkTextNode* nameNode, AtkResNode* rowNode, Bounds rootBounds, bool drawLeft, int? partyIndex) {
        if (!IsRowVisible(nameNode, rowNode)) return;

        var rowName = ReadText(nameNode);
        if (string.IsNullOrWhiteSpace(rowName)) return;

        var label = LabelForRow(rowName, partyIndex, out var loaded, out var actorName, out var centimeters);
        if (label == null) return;
        if (loaded && config.ShowOwnOnWorldBar && IsLocalPlayer(actorName)) return;
        if (loaded) {
            partyNamesDrawn.Add(PlainName(rowName));
            if (!string.IsNullOrWhiteSpace(actorName))
                partyNamesDrawn.Add(actorName);
        }

        var fill = ToColor(nameNode->TextColor);
        var edge = ToEdge(nameNode->EdgeColor);
        if (!loaded) {
            fill = Dim(fill);
            edge = Dim(edge);
        }

        var flash = loaded ? NoteHeight(actorName, centimeters) : default;
        DrawBeside(font, label, (AtkResNode*)nameNode, rootBounds.Pos1.X, rootBounds.Pos2.X, drawLeft, fill, edge, besideNode: false, flash: flash);
    }

    private void DrawTarget(ImFontPtr font) {
        var target = Service.Targets.Target;
        if (target == null) return;

        var targetName = target.Name.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(PlainName(targetName))) return;
        if (partyNamesDrawn.Contains(PlainName(targetName))) return;

        if (!TryLabel(target, out var label, out var centimeters)) return;
        var flash = NoteHeight(PlainName(targetName), centimeters);

        foreach (var addonName in TargetAddonNames) {
            var addon = Service.GameGui.GetAddonByName<AtkUnitBase>(addonName);
            if (addon == null || !addon->IsVisible || !addon->IsReady || addon->RootNode == null) continue;

            var nameNode = FindText(addon->RootNode, targetName);
            if (nameNode == null) continue;

            Bounds rootBounds;
            addon->GetRootBounds(&rootBounds);
            var pastTag = CompanyTagOverhang(addon->RootNode, nameNode, ReadCompanyTag(target));
            DrawBeside(
                font,
                label,
                (AtkResNode*)nameNode,
                rootBounds.Pos1.X,
                rootBounds.Pos2.X,
                drawLeft: true,
                ToColor(nameNode->TextColor),
                ToEdge(nameNode->EdgeColor),
                besideNode: true,
                extendRight: pastTag,
                flash: flash);
            return;
        }
    }

    private string? LabelForRow(string rowName, int? partyIndex, out bool loaded, out string actorName, out float centimeters) {
        loaded = false;
        actorName = string.Empty;
        centimeters = 0f;
        if (!TryResolve(rowName, partyIndex, out var actor, out var unloadedPartyMember))
            return null;
        if (unloadedPartyMember || actor == null)
            return "—";

        if (!TryLabel(actor, out var label, out centimeters)) return null;
        loaded = true;
        actorName = PlainName(actor.Name.ToString());
        return label;
    }

    private bool TryLabel(IGameObject actor, out string label, out float centimeters) {
        label = string.Empty;
        centimeters = 0f;
        if (actor.Address == nint.Zero) return false;
        if (!HeightCalculator.TryMeasure((GameObject*)actor.Address, config, out var sample)) return false;
        centimeters = sample.ShownCentimeters;
        label = HeightCalculator.Format(centimeters, config.Unit, config.Decimals);
        return true;
    }

    private ActiveFlash NoteHeight(string name, float centimeters) {
        if (string.IsNullOrWhiteSpace(name) || !float.IsFinite(centimeters)) return default;

        var now = ImGui.GetTime();
        if (!heights.TryGetValue(name, out var memory)) {
            heights[name] = new HeightMemory { Centimeters = centimeters, SeenAt = now, SampledAt = now, ChangedAt = now };
            return default;
        }

        var step = memory.SampledAt > 0d ? (float)Math.Min(Math.Max(0d, now - memory.SampledAt), 0.1d) : 0f;
        memory.SampledAt = now;
        memory.SeenAt = now;
        var previous = HeightCalculator.Format(memory.Centimeters, config.Unit, config.Decimals);
        var next = HeightCalculator.Format(centimeters, config.Unit, config.Decimals);
        if (!string.Equals(previous, next, StringComparison.Ordinal)) {
            var delta = centimeters - memory.Centimeters;
            if (delta != 0f) {
                memory.Direction = delta > 0f ? 1 : -1;
                memory.ChangedAt = now;
            }
        }

        memory.Centimeters = centimeters;
        if (!config.FlashChanges || memory.Direction == 0) {
            memory.Energy = 0f;
            return default;
        }

        if (now - memory.ChangedAt <= HoldSeconds)
            memory.Energy = MathF.Min(1f, memory.Energy + step / JutSeconds);
        else
            memory.Energy = MathF.Max(0f, memory.Energy - step / RestSeconds);

        if (memory.Energy <= 0.001f) return default;
        return new ActiveFlash(memory.Direction, memory.Energy);
    }

    private void UpdateOwnHeightBar() {
        if (!config.ShowOwnOnWorldBar) {
            HideOwnHeightBar();
            return;
        }

        var player = Service.Objects.LocalPlayer;
        if (player == null || !TryLabel(player, out var label, out var centimeters)) {
            HideOwnHeightBar();
            return;
        }

        var entry = EnsureOwnHeightEntry(label);
        ownLabel = label;
        ownFlash = NoteHeight(PlainName(player.Name.ToString()), centimeters);
        entry.Text = ownFlash.Active ? new string(' ', Math.Max(label.Length, 1)) : label;
        entry.Shown = true;
    }

    private void DrawOwnHeightFlash(ImFontPtr font) {
        if (ownHeightEntry == null || !ownHeightEntry.Shown || string.IsNullOrEmpty(ownLabel)) return;

        var bounds = ownHeightEntry.ScreenBounds;
        var box = bounds.Max - bounds.Min;
        var idlePx = box.Y >= 8f ? Math.Clamp(box.Y * 0.92f, 12f, 28f) : 16f;
        var idleSize = Measure(font, idlePx, ownLabel);
        var reserve = config.FlashChanges ? idlePx * 0.55f : 0f;
        ownHeightEntry.MinimumWidth = (ushort)Math.Clamp((int)MathF.Ceiling(idleSize.X + reserve), 1, 400);
        if (!ownFlash.Active || box.X < 2f || box.Y < 2f) return;

        // Keep the stand-in near the bar's own size, and let the plain text show through as the glow fades out.
        var handoff = ownFlash.Fade < 0.4f;
        ownHeightEntry.Text = handoff ? ownLabel : new string(' ', Math.Max(ownLabel.Length, 1));
        var presence = handoff ? ownFlash.Fade / 0.4f : 1f;
        var fontPx = idlePx * (1f + 0.06f * ownFlash.Fade);
        var textSize = Measure(font, fontPx, ownLabel);
        var pos = new Vector2(bounds.Min.X, bounds.Min.Y + (box.Y - textSize.Y) * 0.5f);
        var color = FlashColor(ownFlash.Direction);
        var fill = WithAlpha(LerpColor(ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f)), color, ownFlash.Fade), presence);
        var edge = WithAlpha(ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 1f)), presence);
        DrawGlow(font, fontPx, pos, textSize, ownLabel, color, ownFlash.Fade * presence);
        DrawOutlined(font, fontPx, pos, MathF.Max(1f, MathF.Round(fontPx / 16f)), fill, edge, ownLabel);
        DrawChangeArrow(pos, textSize, fontPx, false, ownFlash.Direction, color, ownFlash.Fade * presence);
    }

    private IDtrBarEntry EnsureOwnHeightEntry(string text) {
        if (ownHeightEntry != null) return ownHeightEntry;

        try {
            ownHeightEntry = Service.DtrBar.Get(OwnHeightTitle, text);
        } catch (ArgumentException) {
            Service.DtrBar.Remove(OwnHeightTitle);
            ownHeightEntry = Service.DtrBar.Get(OwnHeightTitle, text);
        }

        ownHeightEntry.Tooltip = "Your character height.";
        return ownHeightEntry;
    }

    private void RestoreOwnHeightText() {
        if (ownHeightEntry == null || string.IsNullOrEmpty(ownLabel)) return;
        ownHeightEntry.Text = ownLabel;
    }

    private void HideOwnHeightBar() {
        ownLabel = string.Empty;
        ownFlash = default;
        if (ownHeightEntry == null) return;
        ownHeightEntry.Shown = false;
    }

    private static bool IsLocalPlayer(string actorName) {
        var player = Service.Objects.LocalPlayer;
        if (player == null || string.IsNullOrWhiteSpace(actorName)) return false;
        return string.Equals(PlainName(player.Name.ToString()), actorName, StringComparison.OrdinalIgnoreCase);
    }

    private void PruneHeights(double now) {
        List<string>? stale = null;
        foreach (var pair in heights) {
            if (now - pair.Value.SeenAt <= 5d) continue;
            stale ??= [];
            stale.Add(pair.Key);
        }

        if (stale == null) return;
        foreach (var name in stale)
            heights.Remove(name);
    }

    private static bool TryResolve(string rowName, int? partyIndex, out IGameObject? actor, out bool unloadedPartyMember) {
        actor = null;
        unloadedPartyMember = false;

        Dalamud.Game.ClientState.Party.IPartyMember? indexed = null;
        if (partyIndex is int index && (uint)index < (uint)Service.PartyList.Length)
            indexed = Service.PartyList[index];

        Dalamud.Game.ClientState.Party.IPartyMember? best = null;
        var bestScore = 0;
        if (indexed != null)
            bestScore = MatchScore(rowName, indexed.Name.ToString());
        if (bestScore > 0)
            best = indexed;

        foreach (var member in Service.PartyList) {
            if (member == null) continue;
            var score = MatchScore(rowName, member.Name.ToString());
            if (score <= bestScore) continue;
            best = member;
            bestScore = score;
        }

        if (best != null)
            return ResolvePartyMember(best, out actor, out unloadedPartyMember);

        // The party list draws slot i from PartyList[i]. A truncated or decorated
        // name that matches nobody is still that slot.
        if (indexed != null)
            return ResolvePartyMember(indexed, out actor, out unloadedPartyMember);

        actor = FindSpawned(rowName);
        return actor != null;
    }

    private static bool ResolvePartyMember(Dalamud.Game.ClientState.Party.IPartyMember member, out IGameObject? actor, out bool unloadedPartyMember) {
        actor = member.GameObject ?? FindSpawned(member.Name.ToString() ?? string.Empty);
        unloadedPartyMember = actor == null;
        return true;
    }

    private static IGameObject? FindSpawned(string name) {
        IGameObject? best = null;
        var bestScore = 0;
        foreach (var candidate in Service.Objects) {
            if (candidate == null) continue;
            var score = MatchScore(name, candidate.Name.ToString());
            if (score <= bestScore) continue;
            best = candidate;
            bestScore = score;
        }

        return best;
    }

    private static int MatchScore(string? rowText, string? actorName) {
        var row = NormalizeName(rowText);
        var actor = NormalizeName(actorName);
        if (row.Length == 0 || actor.Length == 0) return 0;
        if (row.Equals(actor, StringComparison.OrdinalIgnoreCase)) return 1000;

        // The party list clips long names ("Mireya Dragons…").
        if (row.Length >= 4 && actor.StartsWith(row, StringComparison.OrdinalIgnoreCase))
            return 800 - (actor.Length - row.Length);
        if (actor.Length >= 4 && row.StartsWith(actor, StringComparison.OrdinalIgnoreCase))
            return 700 - (row.Length - actor.Length);
        if (actor.Length >= 4 && ContainsAtBoundary(row, actor))
            return 600 - (row.Length - actor.Length);
        return 0;
    }

    private static bool ContainsAtBoundary(string text, string part) {
        var index = 0;
        while (index < text.Length) {
            index = text.IndexOf(part, index, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return false;
            var before = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var afterIndex = index + part.Length;
            var after = afterIndex >= text.Length || !char.IsLetterOrDigit(text[afterIndex]);
            if (before && after) return true;
            index++;
        }

        return false;
    }

    private static string NormalizeName(string? text) {
        text = StripPayloads(text);
        text = PlainName(text);
        if (text.Length == 0) return string.Empty;

        while (text.Length > 0) {
            if (text.EndsWith("...", StringComparison.Ordinal)) {
                text = text[..^3].TrimEnd();
                continue;
            }

            var last = text[^1];
            if (last is '.' or '\u2026' or '\u2025' or '\u22EF') {
                text = text[..^1].TrimEnd();
                continue;
            }

            break;
        }

        var separator = text.IndexOfAny(['\u30FB', '\u00B7', '\uFF65']);
        if (separator > 0)
            text = text[..separator];

        return StripLeadingLevel(text.Trim());
    }

    private static string StripPayloads(string? text) {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var chars = new char[text.Length];
        var count = 0;
        for (var i = 0; i < text.Length; i++) {
            var character = text[i];
            if (character == '\u0002') {
                var end = text.IndexOf('\u0003', i + 1);
                if (end < 0) break;
                i = end;
                continue;
            }

            if (character < ' ' || character is '\u200B' or '\u200C' or '\u200D' or '\uFEFF' or '\u2060')
                continue;

            chars[count++] = character;
        }

        return new string(chars, 0, count);
    }

    private static string StripLeadingLevel(string name) {
        var index = 0;
        if (name.Length >= 2
            && (name[0] == 'L' || name[0] == 'l')
            && (name[1] == 'V' || name[1] == 'v')) {
            index = 2;
            if (index < name.Length && name[index] == '.') index++;
        }

        var digits = index;
        while (digits < name.Length && digits - index < 3 && char.IsDigit(name[digits]))
            digits++;

        if (digits == index || digits >= name.Length || !char.IsWhiteSpace(name[digits]))
            return name.Trim();

        return name[(digits + 1)..].Trim();
    }

    private static void DrawBeside(
        ImFontPtr font,
        string text,
        AtkResNode* anchor,
        int frameLeft,
        int frameRight,
        bool drawLeft,
        uint fill,
        uint edge,
        bool besideNode,
        float extendRight = 0f,
        ActiveFlash flash = default) {
        if (anchor == null || string.IsNullOrEmpty(text)) return;

        Bounds anchorBounds;
        anchor->GetBounds(&anchorBounds);
        var fontPx = FontPixels(anchor, anchorBounds);
        if (flash.Active)
            fontPx *= 1f + 0.12f * flash.Fade;
        var textSize = Measure(font, fontPx, text);
        var thickness = MathF.Max(1f, MathF.Round(fontPx / 16f));
        var pad = 6f * MathF.Max(1f, fontPx / 16f);
        var y = anchorBounds.Pos1.Y + (anchorBounds.Height - textSize.Y) * 0.5f;

        float x;
        if (besideNode) {
            x = anchorBounds.Pos1.X + DrawnNameWidth(anchor, anchorBounds) + MathF.Max(0f, extendRight) + pad;
        } else if (drawLeft) {
            x = frameLeft - textSize.X - pad;
        } else {
            x = frameRight + pad;
        }

        var pos = new Vector2(x, y);
        if (flash.Active) {
            var flashColor = FlashColor(flash.Direction);
            DrawGlow(font, fontPx, pos, textSize, text, flashColor, flash.Fade);
            fill = LerpColor(fill, flashColor, flash.Fade);
            DrawOutlined(font, fontPx, pos, thickness, fill, edge, text);
            DrawChangeArrow(pos, textSize, fontPx, !besideNode && drawLeft, flash.Direction, flashColor, flash.Fade);
            return;
        }

        DrawOutlined(font, fontPx, pos, thickness, fill, edge, text);
    }

    private static float DrawnNameWidth(AtkResNode* anchor, Bounds screenBounds) {
        var text = anchor->GetAsAtkTextNode();
        if (text == null) return screenBounds.Width;

        ushort width = 0;
        ushort height = 0;
        text->GetTextDrawSize(&width, &height, considerScale: false);
        var localHeight = anchor->Height;
        var scale = localHeight > 0 && screenBounds.Height > 0 ? screenBounds.Height / (float)localHeight : 1f;
        if (!float.IsFinite(scale) || scale <= 0.1f) scale = 1f;
        return width * scale;
    }

    private static Vector2 Measure(ImFontPtr font, float fontPx, string text) {
        var baked = font.FontSize;
        var scale = baked > 0.01f ? fontPx / baked : 1f;
        var width = 0f;
        foreach (var character in text)
            width += font.GetCharAdvance(character);

        var height = (font.Ascent - font.Descent) * scale;
        if (height <= 0f) height = fontPx;
        return new Vector2(width * scale, height);
    }

    private static float FontPixels(AtkResNode* node, Bounds screenBounds) {
        var text = node->GetAsAtkTextNode();
        var fontSize = text != null && text->FontSize > 0 ? text->FontSize : 14f;
        var localHeight = node->Height;
        var scale = localHeight > 0 && screenBounds.Height > 0 ? screenBounds.Height / (float)localHeight : 1f;
        if (!float.IsFinite(scale) || scale <= 0.1f) scale = 1f;
        return Math.Clamp(fontSize * scale, 10f, 48f);
    }

    private static void DrawOutlined(ImFontPtr font, float fontPx, Vector2 pos, float thickness, uint fill, uint edge, string text) {
        var drawList = ImGui.GetForegroundDrawList();
        ReadOnlySpan<Vector2> offsets = [
            new(-1f, 0f), new(1f, 0f), new(0f, -1f), new(0f, 1f),
            new(-1f, -1f), new(1f, -1f), new(-1f, 1f), new(1f, 1f),
        ];

        foreach (var offset in offsets)
            drawList.AddText(font, fontPx, pos + offset * thickness, edge, text);

        drawList.AddText(font, fontPx, pos, fill, text);
    }

    private static void DrawGlow(ImFontPtr font, float fontPx, Vector2 pos, Vector2 textSize, string text, uint color, float fade) {
        ReadOnlySpan<float> scales = [1.08f, 1.16f];
        ReadOnlySpan<float> alphas = [0.40f, 0.18f];
        var drawList = ImGui.GetForegroundDrawList();
        for (var i = 0; i < scales.Length; i++) {
            var glowPx = fontPx * scales[i];
            var glowSize = Measure(font, glowPx, text);
            var glowPos = pos + (textSize - glowSize) * 0.5f;
            drawList.AddText(font, glowPx, glowPos, WithAlpha(color, alphas[i] * fade), text);
        }
    }

    private static void DrawChangeArrow(Vector2 pos, Vector2 textSize, float fontPx, bool onLeft, int direction, uint color, float fade) {
        var height = MathF.Max(6f, fontPx * 0.42f);
        var width = height * 0.85f;
        var gap = MathF.Max(2f, fontPx * 0.14f);
        var midY = pos.Y + textSize.Y * 0.5f;
        var centerX = onLeft
            ? pos.X - gap - width * 0.5f
            : pos.X + textSize.X + gap + width * 0.5f;
        var top = midY - height * 0.5f;
        var bottom = midY + height * 0.5f;
        var left = centerX - width * 0.5f;
        var right = centerX + width * 0.5f;
        Vector2 tip;
        Vector2 baseLeft;
        Vector2 baseRight;
        if (direction > 0) {
            tip = new Vector2(centerX, top);
            baseLeft = new Vector2(left, bottom);
            baseRight = new Vector2(right, bottom);
        } else {
            tip = new Vector2(centerX, bottom);
            baseLeft = new Vector2(left, top);
            baseRight = new Vector2(right, top);
        }

        ImGui.GetForegroundDrawList().AddTriangleFilled(tip, baseLeft, baseRight, WithAlpha(color, fade));
    }

    private static uint FlashColor(int direction) {
        return direction > 0
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(140f / 255f, 255f / 255f, 170f / 255f, 1f))
            : ImGui.ColorConvertFloat4ToU32(new Vector4(255f / 255f, 140f / 255f, 140f / 255f, 1f));
    }

    private static uint LerpColor(uint from, uint to, float amount) {
        var start = ImGui.ColorConvertU32ToFloat4(from);
        var end = ImGui.ColorConvertU32ToFloat4(to);
        return ImGui.ColorConvertFloat4ToU32(start + (end - start) * amount);
    }

    private static uint WithAlpha(uint color, float alpha) {
        var unpacked = ImGui.ColorConvertU32ToFloat4(color);
        unpacked.W = alpha;
        return ImGui.ColorConvertFloat4ToU32(unpacked);
    }

    private static AtkTextNode* FindText(AtkResNode* node, string name) {
        AtkTextNode* found = null;
        var best = 0;
        Visit(node, name, ref found, ref best);
        return found;
    }

    private static void Visit(AtkResNode* node, string name, ref AtkTextNode* found, ref int best) {
        if (node == null || !node->IsVisible()) return;

        if (node->GetNodeType() == NodeType.Text) {
            var textNode = node->GetAsAtkTextNode();
            if (textNode != null) {
                var score = MatchScore(ReadText(textNode), name);
                if (score > best) {
                    found = textNode;
                    best = score;
                }
            }
        }

        if (node->GetNodeType() == NodeType.Component) {
            var component = node->GetComponent();
            if (component != null && component->UldManager.NodeList != null) {
                var list = component->UldManager.NodeList;
                var count = component->UldManager.NodeListCount;
                for (var i = 0; i < count; i++)
                    Visit(list[i], name, ref found, ref best);
            }
        }

        for (var child = node->ChildNode; child != null; child = child->PrevSiblingNode)
            Visit(child, name, ref found, ref best);
    }

    private static string ReadCompanyTag(IGameObject actor) {
        if (actor.Address == nint.Zero) return string.Empty;
        var gameObject = (GameObject*)actor.Address;
        if (gameObject->ObjectKind != ObjectKind.Pc) return string.Empty;

        var tag = ((Character*)gameObject)->FreeCompanyTagString.Trim('\0').Trim();
        return tag.Length is >= 1 and <= 8 ? tag : string.Empty;
    }

    private static float CompanyTagOverhang(AtkResNode* root, AtkTextNode* nameNode, string fcTag) {
        Bounds nameBounds;
        ((AtkResNode*)nameNode)->GetBounds(&nameBounds);
        var nameRight = nameBounds.Pos1.X + DrawnNameWidth((AtkResNode*)nameNode, nameBounds);
        var overhang = 0f;
        ConsiderCompanyTag(root, nameNode, nameBounds, nameRight, fcTag, ref overhang);
        return overhang;
    }

    private static void ConsiderCompanyTag(
        AtkResNode* node,
        AtkTextNode* nameNode,
        Bounds nameBounds,
        float nameRight,
        string fcTag,
        ref float overhang) {
        if (node == null || !node->IsVisible()) return;

        if (node->GetNodeType() == NodeType.Text) {
            var textNode = node->GetAsAtkTextNode();
            if (textNode != null && textNode != nameNode && IsCompanyTag(ReadText(textNode), fcTag)) {
                Bounds tagBounds;
                node->GetBounds(&tagBounds);
                var sameLine = tagBounds.Pos2.Y > nameBounds.Pos1.Y && tagBounds.Pos1.Y < nameBounds.Pos2.Y;
                var besideName = tagBounds.Pos1.X > nameBounds.Pos1.X && tagBounds.Pos1.X <= nameRight + 80f;
                if (sameLine && besideName) {
                    var tagRight = tagBounds.Pos1.X + DrawnNameWidth(node, tagBounds);
                    overhang = MathF.Max(overhang, tagRight - nameRight);
                }
            }
        }

        if (node->GetNodeType() == NodeType.Component) {
            var component = node->GetComponent();
            if (component != null && component->UldManager.NodeList != null) {
                var list = component->UldManager.NodeList;
                var count = component->UldManager.NodeListCount;
                for (var i = 0; i < count; i++)
                    ConsiderCompanyTag(list[i], nameNode, nameBounds, nameRight, fcTag, ref overhang);
            }
        }

        for (var child = node->ChildNode; child != null; child = child->PrevSiblingNode)
            ConsiderCompanyTag(child, nameNode, nameBounds, nameRight, fcTag, ref overhang);
    }

    private static bool IsCompanyTag(string? text, string fcTag) {
        var plain = PlainName(text).Trim();
        if (plain.Length is < 1 or > 12) return false;
        if (plain.Contains('«') && plain.Contains('»')) return true;
        if (plain.Contains('《') && plain.Contains('》')) return true;
        if (plain.Contains('<') && plain.Contains('>')) return true;
        if (fcTag.Length == 0) return false;

        var inner = plain.Trim('«', '»', '《', '》', '<', '>', ' ');
        return inner.Equals(fcTag, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRowVisible(AtkTextNode* nameNode, AtkResNode* rowNode) {
        if (nameNode == null || !nameNode->IsVisible()) return false;
        return rowNode == null || rowNode->IsVisible();
    }

    private static string ReadText(AtkTextNode* node) {
        if (node == null) return string.Empty;
        return node->NodeText.ToString()?.Trim() ?? string.Empty;
    }

    private static string PlainName(string? text) {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var start = 0;
        while (start < text.Length && (text[start] == '\0' || text[start] >= '\uE000'))
            start++;

        var end = start;
        while (end < text.Length && text[end] != '\0' && text[end] < '\uE000')
            end++;

        return text[start..end].Trim();
    }

    private static uint ToColor(ByteColor color) {
        var alpha = color.A == 0 ? 1f : color.A / 255f;
        return ImGui.ColorConvertFloat4ToU32(new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, alpha));
    }

    private static uint ToEdge(ByteColor color) {
        if (color.A == 0)
            color = new ByteColor { R = 0, G = 0, B = 0, A = 255 };
        return ToColor(color);
    }

    private static uint Dim(uint packed) {
        var color = ImGui.ColorConvertU32ToFloat4(packed);
        color.W *= 0.45f;
        return ImGui.ColorConvertFloat4ToU32(color);
    }

    private sealed class HeightMemory {
        public float Centimeters;
        public double ChangedAt;
        public double SeenAt;
        public double SampledAt;
        public float Energy;
        public int Direction;
    }

    private readonly struct ActiveFlash {
        public ActiveFlash(int direction, float fade) {
            Direction = direction;
            Fade = fade;
        }

        public int Direction { get; }
        public float Fade { get; }
        public bool Active => Direction != 0 && Fade > 0.001f;
    }
}
