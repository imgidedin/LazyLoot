using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace LazyLoot;

internal static class Utils
{
    private static readonly Dictionary<string, object> States = new();

    public static Vector4 MeleeRed { get; } = new(99 / 255f, 28 / 255f, 28 / 255f, 1f);
    public static Vector4 HealerGreen { get; } = new(90 / 255f, 112 / 255f, 83 / 255f, 1f);
    public static Vector4 TankBlue { get; } = new(43 / 255f, 57 / 255f, 150 / 255f, 1f);
    public static Vector4 PhysRangedYellow { get; } = new(152 / 255f, 120 / 255f, 31 / 255f, 1f);
    public static Vector4 White { get; } = new(255 / 255f, 255 / 255f, 255 / 255f, 1f);
    public static Vector4 Black { get; } = new(0 / 255f, 0 / 255f, 0 / 255f, 1f);

    public static unsafe int GetPlayerIlevel()
    {
        return UIState.Instance()->CurrentItemLevel;
    }


    public static bool CheckboxTextWrapped(string text, ref bool v)
    {
        ImGui.PushID(text);

        var changed = ImGui.Checkbox("##chk", ref v);
        ImGui.SameLine();

        var wrapEndX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        ImGui.PushTextWrapPos(wrapEndX);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();

        if (ImGui.IsItemClicked())
        {
            v = !v;
            changed = true;
        }

        ImGui.PopID();
        return changed;
    }

    private static PopupListState<T> GetState<T>(string popupId)
    {
        if (States.TryGetValue(popupId, out var obj) && obj is PopupListState<T> typed)
            return typed;

        var created = new PopupListState<T>();
        States[popupId] = created;
        return created;
    }

    public static void PopupListButton<T>(
        string buttonLabel,
        string popupId,
        string popupTitle,
        Func<string, IEnumerable<T>> getResults,
        Func<T, string> getItemLabel,
        Action<T> onSelect,
        Action<T>? renderItem = null,
        Func<T, string?>? getTooltip = null,
        float minWidth = 300f,
        Vector2? listSize = null,
        int maxResults = 100,
        float debounceSeconds = 0.1f,
        int inputMaxLength = 100,
        string? noteText = null)
    {
        var state = GetState<T>(popupId);

        if (ImGui.Button(buttonLabel))
        {
            state.FocusOnOpen = true;
            state.LastSearchTime = 0;
            ImGui.OpenPopup(popupId);
        }

        if (!ImGui.BeginPopup(popupId))
            return;

        if (!string.IsNullOrEmpty(popupTitle))
        {
            ImGuiEx.TextCentered(popupTitle);
            ImGui.Dummy(new Vector2(0, 4));
        }

        var now = ImGui.GetTime();
        var shouldRefresh =
            now > state.LastSearchTime + debounceSeconds;

        if (shouldRefresh)
        {
            state.LastSearchTime = now;

            var q = state.Query.Trim();
            var results = getResults(q);

            state.CachedResults = results
                .Take(maxResults)
                .ToList();
        }

        var widest = state.CachedResults.Count == 0
            ? 200f
            : state.CachedResults
                .Select(x => ImGui.CalcTextSize(getItemLabel(x)).X)
                .DefaultIfEmpty(200f)
                .Max();

        var width = MathF.Max(minWidth, widest + 30f);
        if (listSize is { X: > 0 })
            width = listSize.Value.X;

        if (!string.IsNullOrEmpty(noteText))
        {
            var wrapPos = ImGui.GetCursorPosX() + width;
            ImGui.PushTextWrapPos(wrapPos);
            ImGui.TextUnformatted(noteText);
            ImGui.PopTextWrapPos();
            ImGui.Dummy(new Vector2(0, 4));
        }

        ImGui.SetNextItemWidth(width);

        if (state.FocusOnOpen)
        {
            ImGui.SetKeyboardFocusHere();
            state.FocusOnOpen = false;
        }

        ImGui.InputText("##popupListSearch", ref state.Query, inputMaxLength);
        ImGui.Dummy(new Vector2(0, 4));

        var childSize = listSize ?? new Vector2(width, 200);
        try
        {
            var style = ImGui.GetStyle();
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, style.ItemSpacing with { Y = 6f });
            if (ImGui.BeginChild("##popupListResults", childSize, true))
            {
                foreach (var item in state.CachedResults)
                {
                    renderItem?.Invoke(item);

                    if (getTooltip != null && ImGui.IsItemHovered())
                    {
                        var tip = getTooltip(item);
                        if (!string.IsNullOrEmpty(tip))
                            ImGui.SetTooltip(tip);
                    }

                    var label = getItemLabel(item);
                    if (!ImGui.Selectable(label)) continue;
                    onSelect(item);
                    ImGui.CloseCurrentPopup();
                    break;
                }

                ImGui.EndChild();
            }
        }
        finally
        {
            ImGui.PopStyleVar();
        }

        ImGui.EndPopup();
    }

    private sealed class PopupListState<T>
    {
        public List<T> CachedResults = [];
        public bool FocusOnOpen;
        public double LastSearchTime;
        public string Query = "";
    }
}