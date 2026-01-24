using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using PunishLib.ImGuiMethods;

namespace LazyLoot;

public class ConfigUi : Window, IDisposable
{
    private static int _debugValue;
    private readonly WindowSystem _windowSystem = new();

    public ConfigUi() : base("Lazy Loot Config")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(680, 400),
            MaximumSize = new Vector2(99999, 99999)
        };
        _windowSystem.AddWindow(this);
        Svc.PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
    }

    public void Dispose()
    {
        Svc.PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        GC.SuppressFinalize(this);
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("config"))
        {
            if (ImGui.BeginTabItem("Features"))
            {
                DrawFeatures();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Restrictions"))
            {
                DrawRestrictions();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("About"))
            {
                AboutTab.Draw("LazyLoot");
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Commands"))
            {
                DrawCommands();
                ImGui.EndTabItem();
            }

#if DEBUG
            if (ImGui.BeginTabItem("Debug"))
            {
                DrawDebug();
                ImGui.EndTabItem();
            }
#endif

            ImGui.EndTabBar();
        }
    }

    private static IDalamudTextureWrap? GetItemIcon(uint id)
    {
        return Svc.Texture.GetFromGameIcon(new GameIconLookup
        {
            IconId = id
        }).GetWrapOrDefault();
    }

    private unsafe void DrawDebug()
    {
        if (ImGui.CollapsingHeader("Is Item Unlocked?"))
        {
            ImGui.InputInt("Debug Value Tester", ref _debugValue);
            ImGui.Text($"Is Unlocked: {Roller.IsItemUnlocked((uint)_debugValue)}");
        }

        if (ImGui.CollapsingHeader("Loot"))
        {
            var loot = Loot.Instance();
            if (loot != null)
                foreach (var item in loot->Items)
                {
                    if (item.ItemId == 0) continue;
                    var casted = (DebugLootItem*)&item;
                    ImGui.PushID($"{casted->ItemId}");
                    Util.ShowStruct(casted);
                }
        }

        // This is here in case we ever need to debug faded copies again.
        // Please do not delete <3

        //if (ImGui.Button("Faded Copy Converter Check?"))
        //{
        //    Roller.UpdateFadedCopy((uint)debugValue, out uint nonfaded);
        //    Svc.Log.Debug($"Non-Faded is {nonfaded}");\
        //}

        //if (ImGui.Button("Check all Faded Copies"))
        //{
        //    foreach (var i in Svc.Data.GetExcelSheet<Item>().Where(x => x.FilterGroup == 12 && x.ItemUICategory.Row == 94))
        //    {
        //        Roller.UpdateFadedCopy((uint)i.RowId, out uint nonfaded);
        //        Svc.Log.Debug($"{i.Name}");
        //    }
        //}
    }

    private static void DrawDiagnostics()
    {
        if (ImGui.Checkbox("Diagnostics Mode", ref LazyLoot.Config.DiagnosticsMode))
            LazyLoot.Config.Save();

        ImGuiComponents.HelpMarker(
            "Outputs additional messages to chat whenever an item is passed, with reasons. This is useful for helping to diagnose issues with the developers or for understanding why LazyLoot makes decisions to pass on items.\r\n\r\nThese messages will only be displayed to you, nobody else in-game can see them.");

        if (ImGui.Checkbox("Don't pass on items that fail to roll.", ref LazyLoot.Config.NoPassEmergency))
            LazyLoot.Config.Save();

        ImGuiComponents.HelpMarker(
            "Normally LazyLoot will pass on items that fail to roll. Enabling this option will prevent it from passing in those situations. Be warned there could be weird side effects doing this and should only be used if you're running into issues with emergency passing appearing.");
    }

    public override void OnClose()
    {
        LazyLoot.Config.Save();
        base.OnClose();
    }

    private static void DrawSectionHeader(string title)
    {
        var style = ImGui.GetStyle();
        var cursor = ImGui.GetCursorPos();
        var cursorScreen = ImGui.GetCursorScreenPos();
        var size = new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight());
        var bgColor = ImGui.GetColorU32(ImGuiColors.ParsedGrey);

        ImGui.GetWindowDrawList().AddRectFilled(
            cursorScreen,
            new Vector2(cursorScreen.X + size.X, cursorScreen.Y + size.Y),
            bgColor,
            4f,
            ImDrawFlags.RoundCornersAll);
        ImGui.SetCursorPos(new Vector2(cursor.X + style.FramePadding.X, cursor.Y + style.FramePadding.Y));
        ImGui.TextColored(new Vector4(0f, 0f, 0f, 1f), title);
        ImGui.SetCursorPos(cursor with { Y = cursor.Y + size.Y + style.ItemSpacing.Y });
    }

    private static void DrawFeatures()
    {
        const float sectionIndent = 16f;

        DrawSectionHeader("FULF Settings");
        ImGui.Dummy(new Vector2(0, 4));

        ImGui.Indent(sectionIndent);
        DrawFulf();
        ImGui.Unindent(sectionIndent);

        ImGui.Dummy(new Vector2(0, 4));
        DrawSectionHeader("Roll Delay Settings");
        ImGui.Dummy(new Vector2(0, 4));

        ImGui.Indent(sectionIndent);
        DrawRollingDelay();
        ImGui.Unindent(sectionIndent);

        ImGui.Dummy(new Vector2(0, 4));
        DrawSectionHeader("Display Settings");
        ImGui.Dummy(new Vector2(0, 4));

        ImGui.Indent(sectionIndent);
        DrawChatAndToast();
        ImGui.Unindent(sectionIndent);

        ImGui.Dummy(new Vector2(0, 4));
        DrawSectionHeader("DTR Bar Settings");
        ImGui.Dummy(new Vector2(0, 4));

        ImGui.Indent(sectionIndent);
        DrawDtrToggle();
        ImGui.Unindent(sectionIndent);

        ImGui.Dummy(new Vector2(0, 4));
        DrawSectionHeader("Diagnostics Settings");
        ImGui.Dummy(new Vector2(0, 4));

        ImGui.Indent(sectionIndent);
        DrawDiagnostics();
        ImGui.Unindent(sectionIndent);
    }

    private static void DrawRollingDelay()
    {
        ImGui.SetNextItemWidth(100);

        if (ImGui.DragFloatRange2("Rolling delay between items", ref LazyLoot.Config.MinRollDelayInSeconds,
                ref LazyLoot.Config.MaxRollDelayInSeconds, 0.1f))
        {
            LazyLoot.Config.MinRollDelayInSeconds = Math.Max(LazyLoot.Config.MinRollDelayInSeconds, 0.5f);

            LazyLoot.Config.MaxRollDelayInSeconds = Math.Max(LazyLoot.Config.MaxRollDelayInSeconds,
                LazyLoot.Config.MinRollDelayInSeconds + 0.1f);
        }
    }

    private static void DrawCommands()
    {
        ImGui.TextWrapped("All commands can be entered in chat.");
        ImGui.Separator();

        if (ImGui.BeginTable("CommandList", 2, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Command", ImGuiTableColumnFlags.WidthFixed, 180f);
            ImGui.TableSetupColumn("Description", ImGuiTableColumnFlags.WidthStretch);
            DrawCenteredTableHeaders(2);

            DrawCommandRow("/lazyloot", "Open LazyLoot config.");
            DrawCommandRow("/lazy", "Open LazyLoot config.");
            DrawCommandRow("/lazy need", "Roll need for all eligible items (greed/pass fallback).");
            DrawCommandRow("/lazy greed", "Roll greed for all eligible items (pass fallback).");
            DrawCommandRow("/lazy pass", "Pass on items you have not rolled for yet.");
            DrawCommandRow("/lazy test <item id or name>", "Preview what LazyLoot would do for an item.");
            DrawCommandRow("/fulf on", "Enable FULF.");
            DrawCommandRow("/fulf off", "Disable FULF.");
            DrawCommandRow("/fulf", "Toggle FULF on or off.");
            DrawCommandRow("/fulf need|greed|pass", "Set the FULF roll mode.");

            ImGui.EndTable();
        }
    }

    private static void DrawCommandRow(string command, string description)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(command);
        ImGui.TableNextColumn();
        ImGui.TextWrapped(description);
    }

    private static void TrySendItemLinkOnRightClick(Item item)
    {
        if (!ImGui.IsItemHovered() || !ImGui.IsMouseClicked(ImGuiMouseButton.Right) || !ImGui.GetIO().KeyShift)
            return;

        var link = new SeString(new ItemPayload(item.RowId, false), new TextPayload(item.Name.ExtractText()),
            RawPayload.LinkTerminator);
        Svc.Chat.Print(link);
    }

    private static void DrawCenteredTableHeaders(int columnCount)
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        for (var i = 0; i < columnCount; i++)
        {
            ImGui.TableSetColumnIndex(i);
            var name = ImGui.TableGetColumnName(i);
            var colTextWidth = ImGui.CalcTextSize(name).X;
            var columnWidth = ImGui.GetColumnWidth();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (columnWidth - colTextWidth) * 0.5f);
            ImGui.TextUnformatted(name);
        }
    }

    private static void DrawOnlyUntradeableCheckbox(string id, ref bool parentRestriction, ref bool thisRestriction)
    {
        if (!parentRestriction) return;
        ImGui.PushID(id);
        ImGui.Indent(20f);
        ImGui.Checkbox(
            "Only Untradeables",
            ref thisRestriction);
        ImGui.Unindent(20f);
        ImGui.PopID();
    }

    private static void DrawUserRestrictionEverywhere()
    {
        if (ImGui.Checkbox("Unlockables Only", ref LazyLoot.Config.UnlockablesOnlyMode))
            LazyLoot.Config.Save();
        var unlockablesOnly = LazyLoot.Config.UnlockablesOnlyMode;
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Only roll on unlockables you do not own and pass on everything else.\nCustom restrictions still apply.\n\nThis option will still obey FULF settings. For this to work, FULF must be set to NEED (or GREED if you want to roll GREED).");
        if (unlockablesOnly)
        {
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
            ImGui.TextWrapped("Unlockables Only is enabled. These settings are ignored until you disable it.");
            ImGui.PopStyleColor();
        }

        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 4));
        ImGuiEx.LineCentered("EverywhereRestrictionWarning",
            () =>
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey2);
                ImGui.TextWrapped(
                    "Settings in this page will apply to every single item, even if they are tradeable or not.");
                ImGui.PopStyleColor();
            });
        ImGuiEx.LineCentered("EverywhereRestrictionWarning2",
            () =>
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
                ImGui.TextWrapped("Some options may overrule the tradeable status.");
                ImGui.PopStyleColor();
            });

        ImGui.Dummy(new Vector2(0, 4));
        ImGui.Separator();
        ImGui.BeginDisabled(unlockablesOnly);
        ImGui.Checkbox("Pass on items with an item level below",
            ref LazyLoot.Config.RestrictionIgnoreItemLevelBelow);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(50);
        ImGui.DragInt("###RestrictionIgnoreItemLevelBelowValue",
            ref LazyLoot.Config.RestrictionIgnoreItemLevelBelowValue);
        if (LazyLoot.Config.RestrictionIgnoreItemLevelBelowValue < 0)
            LazyLoot.Config.RestrictionIgnoreItemLevelBelowValue = 0;

        Utils.CheckboxTextWrapped(
            "Pass on all items already unlocked. (Triple Triad Cards, Orchestrions, Faded Copies, Minions, Mounts, Emotes, Hairstyles)",
            ref LazyLoot.Config.RestrictionIgnoreItemUnlocked);

        if (!LazyLoot.Config.RestrictionIgnoreItemUnlocked)
        {
            ImGui.Checkbox("Pass on unlocked Mounts.", ref LazyLoot.Config.RestrictionIgnoreMounts);
            DrawOnlyUntradeableCheckbox(
                "RestrictionMountsOnlyUntradeables",
                ref LazyLoot.Config.RestrictionIgnoreMounts,
                ref LazyLoot.Config.RestrictionMountsOnlyUntradeables
            );

            ImGui.Checkbox("Pass on unlocked Minions.", ref LazyLoot.Config.RestrictionIgnoreMinions);
            DrawOnlyUntradeableCheckbox(
                "RestrictionMinionsOnlyUntradeables",
                ref LazyLoot.Config.RestrictionIgnoreMinions,
                ref LazyLoot.Config.RestrictionMinionsOnlyUntradeables
            );

            ImGui.Checkbox("Pass on unlocked Bardings.", ref LazyLoot.Config.RestrictionIgnoreBardings);
            DrawOnlyUntradeableCheckbox(
                "RestrictionBardingsOnlyUntradeables",
                ref LazyLoot.Config.RestrictionIgnoreBardings,
                ref LazyLoot.Config.RestrictionBardingsOnlyUntradeables
            );

            ImGui.Checkbox("Pass on unlocked Triple Triad cards.",
                ref LazyLoot.Config.RestrictionIgnoreTripleTriadCards);
            DrawOnlyUntradeableCheckbox(
                "RestrictionTripleTriadCardsOnlyUntradeables",
                ref LazyLoot.Config.RestrictionIgnoreTripleTriadCards,
                ref LazyLoot.Config.RestrictionTripleTriadCardsOnlyUntradeables
            );

            ImGui.Checkbox("Pass on unlocked Emotes and Hairstyle.",
                ref LazyLoot.Config.RestrictionIgnoreEmoteHairstyle);
            DrawOnlyUntradeableCheckbox(
                "RestrictionEmoteHairstyleOnlyUntradeables",
                ref LazyLoot.Config.RestrictionIgnoreEmoteHairstyle,
                ref LazyLoot.Config.RestrictionEmoteHairstyleOnlyUntradeables
            );

            ImGui.Checkbox("Pass on unlocked Orchestrion Rolls.",
                ref LazyLoot.Config.RestrictionIgnoreOrchestrionRolls);
            DrawOnlyUntradeableCheckbox(
                "RestrictionOrchestrionRollsOnlyUntradeables",
                ref LazyLoot.Config.RestrictionIgnoreOrchestrionRolls,
                ref LazyLoot.Config.RestrictionOrchestrionRollsOnlyUntradeables
            );

            ImGui.Checkbox("Pass on unlocked Faded Copies.", ref LazyLoot.Config.RestrictionIgnoreFadedCopy);
            DrawOnlyUntradeableCheckbox(
                "RestrictionFadedCopyOnlyUntradeables",
                ref LazyLoot.Config.RestrictionIgnoreFadedCopy,
                ref LazyLoot.Config.RestrictionFadedCopyOnlyUntradeables
            );
        }

        DrawOnlyUntradeableCheckbox(
            "RestrictionAllUnlockablesOnlyUntradeables",
            ref LazyLoot.Config.RestrictionIgnoreItemUnlocked,
            ref LazyLoot.Config.RestrictionAllUnlockablesOnlyUntradeables
        );

        ImGui.Checkbox("Pass on items I can't use with current job.",
            ref LazyLoot.Config.RestrictionOtherJobItems);

        ImGui.EndDisabled();
        ImGui.Checkbox("Don't roll on items or duties with a weekly lockout.",
            ref LazyLoot.Config.RestrictionWeeklyLockoutItems);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "On duties that have a specific lockout on a per-item basis, Lazy Loot will only roll on items without said flag (e.g., minions, orchestrations, cards, etc.).\nDuties like that are, for example, recent released Alliance Raids.\n\nFor duties with a server-side lockout, like current patch raids, Lazy Loot will check the server message stating that loot has certain weekly restrictions tied to it, and will disable its function until you leave the duty.");
        ImGui.BeginDisabled(unlockablesOnly);

        ImGui.Checkbox("###RestrictionWeeklyLockoutItems", ref LazyLoot.Config.RestrictionLootLowerThanJobIlvl);
        ImGui.SameLine();
        ImGui.Text("Roll");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        ImGui.Combo("###RestrictionLootLowerThanJobIlvlRollState",
            ref LazyLoot.Config.RestrictionLootLowerThanJobIlvlRollState, new[] { "Greed", "Pass" }, 2);
        ImGui.SameLine();
        ImGui.Text("on items that are");
        ImGui.SetNextItemWidth(50);
        ImGui.SameLine();
        ImGui.DragInt("###RestrictionLootLowerThanJobIlvlTreshold",
            ref LazyLoot.Config.RestrictionLootLowerThanJobIlvlTreshold);
        if (LazyLoot.Config.RestrictionLootLowerThanJobIlvlTreshold < 0)
            LazyLoot.Config.RestrictionLootLowerThanJobIlvlTreshold = 0;
        ImGui.SameLine();
        ImGui.Text($"item levels lower than your current job item level (\u2605 {Utils.GetPlayerIlevel()}).");
        ImGuiComponents.HelpMarker("This setting will only apply to gear you can need on.");

        ImGui.Checkbox("###RestrictionLootIsJobUpgrade", ref LazyLoot.Config.RestrictionLootIsJobUpgrade);
        ImGui.SameLine();
        ImGui.Text("Roll");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        ImGui.Combo("###RestrictionLootIsJobUpgradeRollState",
            ref LazyLoot.Config.RestrictionLootIsJobUpgradeRollState,
            new[] { "Greed", "Pass" }, 2);
        ImGui.SameLine();
        ImGui.Text("on items if the current equipped item of the same type has a higher item level.");
        ImGuiComponents.HelpMarker("This setting will only apply to gear you can need on.");

        ImGui.Checkbox("###RestrictionSeals", ref LazyLoot.Config.RestrictionSeals);
        ImGui.SameLine();
        ImGui.Text("Pass on items with an expert delivery seal value of less than");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.DragInt("###RestrictionSealsAmnt", ref LazyLoot.Config.RestrictionSealsAmnt);
        ImGui.SameLine();
        ImGui.Text($"(item level {Roller.ConvertSealsToIlvl(LazyLoot.Config.RestrictionSealsAmnt)} and below)");
        ImGuiComponents.HelpMarker(
            "This setting will only apply to gear able to be turned in for expert delivery.");

        ImGui.Checkbox("###NeverPassGlam", ref LazyLoot.Config.NeverPassGlam);
        ImGui.SameLine();
        ImGui.TextWrapped("Never pass on glamour items (Items that have an item and iLvl of 1)");
        ImGui.EndDisabled();
    }

    private static void CenterText()
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetColumnWidth() - ImGui.GetFrameHeight()) * 0.5f);
    }

    private static ImTextureID GetDutyIcon(ContentFinderCondition duty)
    {
        var icon = duty is { HighEndDuty: true, ContentType.Value.RowId: 5 }
            ? Svc.Data.GetExcelSheet<ContentType>()
                .FirstOrDefault(x => x.RowId == 28).Icon
            : duty.ContentType.Value.Icon;
        if (icon == 0) return 0;

        var itemIcon = GetItemIcon(icon);
        return itemIcon?.Handle ?? default;
    }

    private static void DrawUserRestrictionItems(RestrictionGroup restrictions)
    {
        ImGui.Dummy(new Vector2(0, 6));
        ImGuiEx.LineCentered("ItemRestrictionWarning",
            () =>
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                ImGui.TextWrapped("These rules override any other restriction settings, but the weekly lockout.");
                ImGui.PopStyleColor();
            });
        ImGui.Dummy(new Vector2(0, 6));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 6));

        var items = restrictions.Items;

        if (items.Count == 0)
        {
            ImGui.Dummy(new Vector2(0, 6));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 12));
            if (ImGui.BeginChild("##UserRestrictionEmptyState", new Vector2(-1, 60), true,
                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                ImGuiEx.TextCentered("No items added.");
                ImGuiEx.TextCentered("Click the Add item below to start adding items.");
                ImGui.EndChild();
            }

            ImGui.PopStyleVar();
            ImGui.Dummy(new Vector2(0, 6));
        }
        else
        {
            if (ImGui.BeginTable("UserRestrictionItemsTable", 8, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.WidthFixed, 50f);
                ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed, 32f);
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 50f);
                ImGui.TableSetupColumn("Greed", ImGuiTableColumnFlags.WidthFixed, 50f);
                ImGui.TableSetupColumn("Pass", ImGuiTableColumnFlags.WidthFixed, 50f);
                ImGui.TableSetupColumn("Nothing", ImGuiTableColumnFlags.WidthFixed, 50f);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 70f);
                DrawCenteredTableHeaders(8);

                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var restrictedItem = Svc.Data.GetExcelSheet<Item>().GetRow(item.Id);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    var enabled = item.Enabled;
                    CenterText();
                    if (ImGui.Checkbox($"##{item.Id}", ref enabled))
                    {
                        item.Enabled = enabled;
                        LazyLoot.Config.Save();
                    }

                    ImGui.TableNextColumn();
                    CenterText();

                    var icon = GetItemIcon(restrictedItem.Icon);
                    if (icon != null)
                        ImGui.Image(icon.Handle, new Vector2(24, 24));
                    else
                        ImGui.Text("-");
                    TrySendItemLinkOnRightClick(restrictedItem);

                    ImGui.TableNextColumn();
                    ImGui.Text(restrictedItem.Name.ToString());
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(restrictedItem.Name.ToString());
                    TrySendItemLinkOnRightClick(restrictedItem);

                    ImGui.TableNextColumn();
                    CenterText();
                    if (ImGui.RadioButton($"##need{item.Id}", item.RollRule == RollResult.Needed))
                    {
                        item.RollRule = RollResult.Needed;
                        LazyLoot.Config.Save();
                    }

                    ImGui.TableNextColumn();
                    CenterText();
                    if (ImGui.RadioButton($"##greed{item.Id}", item.RollRule == RollResult.Greeded))
                    {
                        item.RollRule = RollResult.Greeded;
                        LazyLoot.Config.Save();
                    }

                    ImGui.TableNextColumn();
                    CenterText();
                    if (ImGui.RadioButton($"##pass{item.Id}", item.RollRule == RollResult.Passed))
                    {
                        item.RollRule = RollResult.Passed;
                        LazyLoot.Config.Save();
                    }

                    ImGui.TableNextColumn();
                    CenterText();
                    if (ImGui.RadioButton($"##doNothing{item.Id}", item.RollRule == RollResult.UnAwarded))
                    {
                        item.RollRule = RollResult.UnAwarded;
                        LazyLoot.Config.Save();
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Remove##{item.Id}"))
                    {
                        restrictions.Items.RemoveAt(i);
                        LazyLoot.Config.Save();
                        break;
                    }
                }

                ImGui.EndTable();
            }
        }

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2, 0));

        var itemSheet = Svc.Data.GetExcelSheet<Item>();
        Utils.PopupListButton(
            "Add item...",
            "item_search_add",
            "Search for item:",
            q =>
            {
                if (uint.TryParse(q, out var searchId))
                    return itemSheet
                        .Where(x => x.RowId == searchId
                                    && x is { RowId: > 0, Name.IsEmpty: false }
                                    && items.All(d => d.Id != x.RowId));

                return itemSheet
                    .Where(x =>
                        x.Name.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)
                        && x is { RowId: > 0, Name.IsEmpty: false }
                        && items.All(d => d.Id != x.RowId));
            },
            item => $" {item.Name} (ID: {item.RowId})",
            renderItem: item =>
            {
                var icon = GetItemIcon(item.Icon);
                if (icon != null)
                {
                    ImGui.Image(icon.Handle, new Vector2(16, 16));
                    ImGui.SameLine();
                }

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(item.Name.ToString());
                ImGui.SameLine();
            },
            onSelect: duty =>
            {
                restrictions.Items.Add(new CustomRestriction
                {
                    Id = duty.RowId,
                    Enabled = true,
                    RollRule = RollResult.UnAwarded
                });
                LazyLoot.Config.Save();
            }
        );

        ImGui.PopStyleVar();
    }

    private static bool ValidateImport<T>(List<CustomRestriction>? imported, ExcelSheet<T> sheet, string idLabel)
        where T : struct, IExcelRow<T>
    {
        if (imported == null)
            return false;

        var bail = false;
        foreach (var item in imported)
        {
            if (sheet.Any(x => x.RowId == item.Id)) continue;
            bail = true;
            Notify.Error($"Imported restriction contains invalid {idLabel} ID: {item.Id}. Import cancelled.");
        }

        if (bail)
            return false;

        return true;
    }

    private static void ImportPresetFromClipboard(List<RestrictionPreset> presets)
    {
        var clipboardText = ImGui.GetClipboardText();
        if (string.IsNullOrEmpty(clipboardText))
        {
            Notify.Error("Nothing to import on your clipboard");
            return;
        }

        RestrictionPresetExport? imported;
        try
        {
            imported = JsonSerializer.Deserialize<RestrictionPresetExport>(clipboardText);
        }
        catch (Exception e)
        {
            e.Log();
            Notify.Error("Failed to import preset - invalid format");
            return;
        }

        if (imported == null)
        {
            Notify.Error("Failed to import preset - invalid format");
            return;
        }

        var restrictions = imported.Restrictions;
        var itemSheet = Svc.Data.GetExcelSheet<Item>();
        var dutySheet = Svc.Data.GetExcelSheet<ContentFinderCondition>();
        if (!ValidateImport(restrictions.Items, itemSheet, "item") ||
            !ValidateImport(restrictions.Duties, dutySheet, "duty"))
            return;

        var name = MakeUniquePresetName(imported.Name, presets);
        var preset = new RestrictionPreset
        {
            Name = name,
            Enabled = imported.Enabled,
            Restrictions = restrictions
        };
        presets.Add(preset);
        LazyLoot.Config.ActiveRestrictionPresetId = preset.Id;
        LazyLoot.Config.Save();
        Notify.Success($"Imported preset \"{name}\" successfully!");
    }

    private static string MakeUniquePresetName(string name, List<RestrictionPreset> presets)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "Imported Preset" : name.Trim();
        if (presets.All(p => !string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            return trimmed;

        var suffix = 1;
        string candidate;
        do
        {
            candidate = $"{trimmed} ({suffix})";
            suffix++;
        } while (presets.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)));

        return candidate;
    }

    private static void DrawUserRestrictionDuties(RestrictionGroup restrictions)
    {
        ImGui.Dummy(new Vector2(0, 6));
        ImGuiEx.LineCentered("DutyRestrictionWarning",
            () =>
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                ImGui.TextWrapped(
                    "These rules override the main restriction settings, but is overriden by the item restriction settings if they happen to collide.");
                ImGui.PopStyleColor();
            });
        ImGui.Dummy(new Vector2(0, 6));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 6));

        var duties = restrictions.Duties;

        if (duties.Count == 0)
        {
            ImGui.Dummy(new Vector2(0, 6));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 12));
            if (ImGui.BeginChild("##UserRestrictionDutyEmptyState", new Vector2(-1, 60), true,
                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                ImGuiEx.TextCentered("No duties added.");
                ImGuiEx.TextCentered("Click the Add duty below to start adding duties.");
                ImGui.EndChild();
            }

            ImGui.PopStyleVar();
            ImGui.Dummy(new Vector2(0, 6));
        }
        else
        {
            if (ImGui.BeginTable("UserRestrictionDutiesTable", 8,
                    ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.WidthFixed, 50f);
                ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 32f);
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 50f);
                ImGui.TableSetupColumn("Greed", ImGuiTableColumnFlags.WidthFixed, 50f);
                ImGui.TableSetupColumn("Pass", ImGuiTableColumnFlags.WidthFixed, 50f);
                ImGui.TableSetupColumn("Nothing", ImGuiTableColumnFlags.WidthFixed, 50f);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 60f);
                DrawCenteredTableHeaders(8);

                for (var i = 0; i < duties.Count; i++)
                {
                    var duty = duties[i];
                    var restrictedDuty = Svc.Data.GetExcelSheet<ContentFinderCondition>().GetRow(duty.Id);
                    var enabled = duty.Enabled;
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    CenterText();
                    if (ImGui.Checkbox($"##{duty.Id}", ref enabled))
                    {
                        duty.Enabled = enabled;
                        LazyLoot.Config.Save();
                    }

                    ImGui.TableNextColumn();
                    CenterText();

                    ImGui.Image(GetDutyIcon(restrictedDuty), new Vector2(24, 24));
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip((restrictedDuty is { HighEndDuty: true, ContentType.Value.RowId: 5 }
                            ? Svc.Data.GetExcelSheet<ContentType>()
                                .FirstOrDefault(x => x.RowId == 28).Name
                            : restrictedDuty.ContentType.Value.Name).ToString());

                    ImGui.TableNextColumn();
                    ImGui.Text(restrictedDuty.Name.ToString());
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(restrictedDuty.Name.ToString());

                    ImGui.TableNextColumn();
                    CenterText();
                    if (ImGui.RadioButton($"##need{duty.Id}", duty.RollRule == RollResult.Needed))
                    {
                        duty.RollRule = RollResult.Needed;
                        LazyLoot.Config.Save();
                    }

                    ImGui.TableNextColumn();
                    CenterText();
                    if (ImGui.RadioButton($"##greed{duty.Id}", duty.RollRule == RollResult.Greeded))
                    {
                        duty.RollRule = RollResult.Greeded;
                        LazyLoot.Config.Save();
                    }

                    ImGui.TableNextColumn();
                    CenterText();
                    if (ImGui.RadioButton($"##pass{duty.Id}", duty.RollRule == RollResult.Passed))
                    {
                        duty.RollRule = RollResult.Passed;
                        LazyLoot.Config.Save();
                    }

                    ImGui.TableNextColumn();
                    CenterText();
                    if (ImGui.RadioButton($"##doNothing{duty.Id}", duty.RollRule == RollResult.UnAwarded))
                    {
                        duty.RollRule = RollResult.UnAwarded;
                        LazyLoot.Config.Save();
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Remove##{duty.Id}"))
                    {
                        restrictions.Duties.RemoveAt(i);
                        LazyLoot.Config.Save();
                        break;
                    }
                }

                ImGui.EndTable();
            }
        }

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2, 0));
        var dutySheet = Svc.Data.GetExcelSheet<ContentFinderCondition>();
        Utils.PopupListButton(
            "Add duty...",
            "duty_search_add",
            "Search for duty:",
            q =>
            {
                if (uint.TryParse(q, out var searchId))
                    return dutySheet
                        .Where(x => x.RowId == searchId
                                    && x is { RowId: > 0, Name.IsEmpty: false }
                                    && duties.All(d => d.Id != x.RowId));

                return dutySheet
                    .Where(x =>
                        x.Name.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)
                        && x is { RowId: > 0, Name.IsEmpty: false }
                        && duties.All(d => d.Id != x.RowId));
            },
            duty => $" {duty.Name} (ID: {duty.RowId})",
            renderItem: duty =>
            {
                ImGui.Image(GetDutyIcon(duty), new Vector2(16, 16));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(duty.Name.ToString());
                ImGui.SameLine();
            },
            onSelect: duty =>
            {
                restrictions.Duties.Add(new CustomRestriction
                {
                    Id = duty.RowId,
                    Enabled = true,
                    RollRule = RollResult.UnAwarded
                });
                LazyLoot.Config.Save();
            }
        );

        ImGui.PopStyleVar();
    }

    private static void DrawRestrictions()
    {
        if (!ImGui.BeginTabBar("RestrictionTabs")) return;

        if (ImGui.BeginTabItem("Everywhere"))
        {
            DrawUserRestrictionEverywhere();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Custom Restrictions"))
        {
            DrawCustomRestrictions();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private static void DrawCustomRestrictions()
    {
        var config = LazyLoot.Config;
        if (config.EnsureActiveRestrictionPreset())
            config.Save();

        var presets = config.RestrictionPresets;
        var activePreset = config.GetActiveRestrictionPreset();

        if (!ImGui.BeginTable("CustomRestrictionsLayout", 2)) return;

        ImGui.TableSetupColumn("PresetList", ImGuiTableColumnFlags.WidthFixed, 200f);
        ImGui.TableSetupColumn("PresetEditor", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        DrawPresetList(presets, activePreset);

        ImGui.TableSetColumnIndex(1);
        DrawPresetEditor(activePreset);

        ImGui.EndTable();
    }

    private static void DrawPresetList(List<RestrictionPreset> presets, RestrictionPreset activePreset)
    {
        var footerHeight = ImGui.GetFrameHeightWithSpacing() * 1.2;
        ImGui.BeginChild("PresetList", new Vector2(0, (float)-footerHeight), true);
        foreach (var preset in presets)
        {
            ImGui.PushID(preset.Id.ToString());
            var enabled = preset.Enabled;
            if (ImGui.Checkbox("##PresetEnabled", ref enabled))
            {
                preset.Enabled = enabled;
                LazyLoot.Config.Save();
            }

            ImGui.SameLine();
            var isDisabled = !preset.Enabled;
            if (isDisabled)
                ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            if (ImGui.Selectable(preset.Name, preset.Id == activePreset.Id))
            {
                LazyLoot.Config.ActiveRestrictionPresetId = preset.Id;
                LazyLoot.Config.Save();
            }

            if (isDisabled)
                ImGui.PopStyleColor();

            ImGui.PopID();
        }

        ImGui.EndChild();

        if (ImGui.Button("New##PresetNew", new Vector2(60f, 0f)))
        {
            var name = MakeUniquePresetName("New Preset", presets);
            var preset = new RestrictionPreset { Name = name };
            presets.Add(preset);
            LazyLoot.Config.ActiveRestrictionPresetId = preset.Id;
            LazyLoot.Config.Save();
        }

        ImGui.SameLine();
        var ctrlHeld = ImGui.GetIO().KeyCtrl;
        ImGui.BeginDisabled(!ctrlHeld);
        if (ImGui.Button("Delete##PresetDelete", new Vector2(60f, 0f)))
        {
            if (presets.Count <= 1)
            {
                activePreset.Name = "Default";
                activePreset.Enabled = true;
                activePreset.Restrictions = new RestrictionGroup();
                LazyLoot.Config.ActiveRestrictionPresetId = activePreset.Id;
            }
            else
            {
                presets.RemoveAll(p => p.Id == activePreset.Id);
                LazyLoot.Config.EnsureActiveRestrictionPreset();
            }

            LazyLoot.Config.Save();
        }

        ImGui.EndDisabled();
        if (!ctrlHeld && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Hold CTRL to allow preset deletion.");

        ImGui.SameLine();
        if (ImGui.Button("Import##PresetImport", new Vector2(60f, 0f))) ImportPresetFromClipboard(presets);
    }

    private static void DrawPresetEditor(RestrictionPreset activePreset)
    {
        var presetName = activePreset.Name;
        ImGui.SetNextItemWidth(260f);
        if (ImGui.InputText("Preset Name", ref presetName, 64))
        {
            activePreset.Name = string.IsNullOrWhiteSpace(presetName) ? "Preset" : presetName.Trim();
            LazyLoot.Config.Save();
        }

        ImGui.SameLine();
        if (ImGui.Button("Export Preset", new Vector2(110f, 0f)))
        {
            var json = JsonSerializer.Serialize(new RestrictionPresetExport
            {
                Name = activePreset.Name,
                Enabled = activePreset.Enabled,
                Restrictions = activePreset.Restrictions
            });
            ImGui.SetClipboardText(json);
            Notify.Success("Preset copied to clipboard!");
        }

        ImGui.Separator();

        if (ImGui.BeginTabBar("PresetRestrictionsTabs"))
        {
            if (ImGui.BeginTabItem("Items"))
            {
                DrawUserRestrictionItems(activePreset.Restrictions);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Duties"))
            {
                DrawUserRestrictionDuties(activePreset.Restrictions);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private static void DrawChatAndToast()
    {
        ImGui.Text("Roll Result Information");
        ImGui.Checkbox("Display roll information in chat.", ref LazyLoot.Config.EnableChatLogMessage);
        ImGui.Spacing();
        ImGui.Text("Display as Toasts");
        ImGuiComponents.HelpMarker("Show your roll information as a pop-up toast, using the various styles below.");
        ImGui.Checkbox("Quest", ref LazyLoot.Config.EnableQuestToast);
        ImGui.SameLine();
        ImGui.Checkbox("Normal", ref LazyLoot.Config.EnableNormalToast);
        ImGui.SameLine();
        ImGui.Checkbox("Error", ref LazyLoot.Config.EnableErrorToast);
    }

    private static void DrawDtrToggle()
    {
        ImGui.Text("Server Info Bar (DTR)");
        ImGui.Checkbox("###LazyLootDtrEnabled", ref LazyLoot.Config.ShowDtrEntry);
        ImGui.SameLine();
        ImGui.TextColored(
            LazyLoot.Config.ShowDtrEntry ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed,
            LazyLoot.Config.ShowDtrEntry ? "DTR Enabled" : "DTR Disabled"
        );
        ImGui.TextWrapped("Show/hide LazyLoot in the Dalamud Server Info Bar (DTR).");
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 4));
        ImGui.TextWrapped(
            "Depending on your settings, sometimes the DTR entry text will show some extra information, like:");
        ImGui.BulletText("WLD: Weekly Lockout Duty");
        ImGui.Indent();
        ImGui.TextWrapped(
            "The currenty duty was detected as being restrict duty rolls for the week in a way that can't be resolved on a per item basis, so Lazy Loot is being disabled until you leave this duty;");
        ImGui.Unindent();
        ImGui.BulletText("Unlockables Only Mode");
        ImGui.Indent();
        ImGui.TextWrapped(
            "Lazy Loot is only checking for items you can unlock and will roll what FULF is set to on unlockables, but will always pass on anything that isn't an unlockable, with the exception of Custom Restrictions. So, beware with this mode.");
        ImGui.Unindent();
    }

    private static void DrawFulf()
    {
        ImGui.TextWrapped(
            "Fancy Ultimate Lazy Feature (FULF) is a set and forget feature that will automatically roll on items for you instead of having to use the commands above.");
        ImGui.Separator();
        ImGui.Checkbox("###FulfEnabled", ref LazyLoot.Config.FulfEnabled);
        ImGui.SameLine();
        ImGui.TextColored(LazyLoot.Config.FulfEnabled ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed,
            LazyLoot.Config.FulfEnabled ? "FULF Enabled" : "FULF Disabled");
        if (LazyLoot.Config.RestrictionWeeklyLockoutItems && LazyLoot.Config.WeeklyLockoutDutyActive)
            ImGui.TextColored(ImGuiColors.DalamudYellow,
                "Weekly Lockout Duty detected: FULF and /lazy rolls are temporarily disabled until you leave this duty or disable the weekly lockout setting.");

        ImGui.SetNextItemWidth(100);

        if (ImGui.Combo("Roll options", ref LazyLoot.Config.FulfRoll, new[] { "Need", "Greed", "Pass" }, 3))
            LazyLoot.Config.Save();

        ImGui.Text("First Roll Delay Range (In seconds)");
        ImGui.SetNextItemWidth(100);
        ImGui.DragFloat("Minimum Delay in seconds. ", ref LazyLoot.Config.FulfMinRollDelayInSeconds, 0.1F);

        if (LazyLoot.Config.FulfMinRollDelayInSeconds >= LazyLoot.Config.FulfMaxRollDelayInSeconds)
            LazyLoot.Config.FulfMinRollDelayInSeconds = LazyLoot.Config.FulfMaxRollDelayInSeconds - 0.1f;

        if (LazyLoot.Config.FulfMinRollDelayInSeconds < 1.5f) LazyLoot.Config.FulfMinRollDelayInSeconds = 1.5f;

        ImGui.SetNextItemWidth(100);
        ImGui.DragFloat("Maximum Delay in seconds. ", ref LazyLoot.Config.FulfMaxRollDelayInSeconds, 0.1F);

        if (LazyLoot.Config.FulfMaxRollDelayInSeconds <= LazyLoot.Config.FulfMinRollDelayInSeconds)
            LazyLoot.Config.FulfMaxRollDelayInSeconds = LazyLoot.Config.FulfMinRollDelayInSeconds + 0.1f;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x40)]
    private struct DebugLootItem
    {
        [FieldOffset(0x00)] public uint ChestObjectId;
        [FieldOffset(0x04)] public uint ChestItemIndex; // This loot item's index in the chest it came from
        [FieldOffset(0x08)] public uint ItemId;
        [FieldOffset(0x0C)] public ushort ItemCount;

        [FieldOffset(0x1C)] public uint GlamourItemId;
        [FieldOffset(0x20)] public RollState RollState;
        [FieldOffset(0x24)] public RollResult RollResult;
        [FieldOffset(0x28)] public byte RollValue;
        [FieldOffset(0x34)] public byte Unk1;
        [FieldOffset(0x38)] public byte Unk2;
        [FieldOffset(0x2C)] public float Time;
        [FieldOffset(0x30)] public float MaxTime;

        [FieldOffset(0x38)] public LootMode LootMode;
    }

    private sealed class RestrictionPresetExport
    {
        public string Name { get; init; } = string.Empty;
        public bool Enabled { get; init; } = true;
        public RestrictionGroup Restrictions { get; init; } = new();
    }
}