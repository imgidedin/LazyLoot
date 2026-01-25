using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace LazyLoot;

internal static class Roller
{
    private static RollItemRaw _rollItemRaw;

    private static uint _itemId, _index;

    public static void Clear()
    {
        _itemId = _index = 0;
    }

    public static bool RollOneItem(RollResult option, ref int need, ref int greed, ref int pass)
    {
        if (!GetNextLootItem(out var index, out var loot))
            return false;

        // Custom item/duty rules override FULF, otherwise apply player rules then clamp to max allowed.
        var maxAllowed = GetRestrictResult(loot);
        var customRule = GetPlayerCustomRestrict(loot);
        var playerRule = customRule == null
            ? GetPlayerRestrictByItemId(loot.ItemId, loot.RollState == RollState.UpToNeed)
            : RollResult.UnAwarded;
        DuoLog.Debug(
            $"Rolling {loot.ItemId} with {option} and {maxAllowed} restrictions (Custom: {customRule}, Player: {playerRule}).");
        option = ResolveFinalRoll(option, maxAllowed, customRule, playerRule);

        if (_itemId == loot.ItemId && index == _index)
        {
            if (LazyLoot.Config.DiagnosticsMode && !LazyLoot.Config.NoPassEmergency)
                DuoLog.Debug(
                    $"{Svc.Data.GetExcelSheet<Item>().GetRow(loot.ItemId).Name.ToString()} has failed to roll for some reason. Passing for safety. [Emergency pass]");

            if (!LazyLoot.Config.NoPassEmergency)
            {
                switch (option)
                {
                    case RollResult.Needed:
                        need--;
                        break;
                    case RollResult.Greeded:
                        greed--;
                        break;
                    default:
                        pass--;
                        break;
                }

                option = RollResult.Passed;
            }
        }

        RollItem(option, index);
        _itemId = loot.ItemId;
        _index = index;

        switch (option)
        {
            case RollResult.Needed:
                need++;
                break;
            case RollResult.Greeded:
                greed++;
                break;
            default:
                pass++;
                break;
        }

        return true;
    }

    private static RollResult GetRestrictResult(LootItem loot)
    {
        var item = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(loot.ItemId);
        if (item == null)
            return RollResult.Passed;

        //Checks what the max possible roll type on the item is
        var stateMax = loot.RollState switch
        {
            RollState.UpToNeed => RollResult.Needed,
            RollState.UpToGreed => RollResult.Greeded,
            _ => RollResult.Passed
        };

        if (item.Value.IsUnique && IsItemUnlocked(loot.ItemId))
            stateMax = RollResult.Passed;

        if (LazyLoot.Config.DiagnosticsMode && stateMax == RollResult.Passed)
            DuoLog.Debug($"{item.Value.Name.ToString()} can only be passed on. [RollState UpToPass]");

        //Checks what the player set loot rules are
        var ruleMax = loot.LootMode switch
        {
            LootMode.Normal => RollResult.Needed,
            LootMode.GreedOnly => RollResult.Greeded,
            _ => RollResult.Passed
        };

        return ResultMerge(stateMax, ruleMax);
    }

    private static unsafe RollResult? GetPlayerCustomRestrict(LootItem loot)
    {
        var lootItem = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(loot.ItemId);
        if (lootItem == null || (lootItem.Value.IsUnique && ItemCount(loot.ItemId) > 0))
            return RollResult.Passed;

        // Here, we will check for the specific rules for items.
        var itemCustomRestriction = LazyLoot.Config.FindEnabledItemRestriction(loot.ItemId);
        if (itemCustomRestriction is { Enabled: true })
        {
            if (!LazyLoot.Config.DiagnosticsMode) return itemCustomRestriction.RollRule;

            var action = itemCustomRestriction.RollRule == RollResult.Passed ? "passing" :
                itemCustomRestriction.RollRule == RollResult.Greeded ? "greeding" :
                itemCustomRestriction.RollRule == RollResult.Needed ? "needing" : "passing";
            Svc.Log.Debug($"{lootItem.Value.Name.ToString()} is {action}. [Item Custom Restriction]");

            return itemCustomRestriction.RollRule;
        }

        // Here, we will check for the specific rules for the Duty.
        var contentFinderInfo = Svc.Data.GetExcelSheet<ContentFinderCondition>()
            .GetRow(GameMain.Instance()->CurrentContentFinderConditionId);
        var dutyCustomRestriction = LazyLoot.Config.FindEnabledDutyRestriction(contentFinderInfo.RowId);
        if (dutyCustomRestriction is not { Enabled: true }) return null;
        {
            if (!LazyLoot.Config.DiagnosticsMode) return dutyCustomRestriction.RollRule;
            var action = dutyCustomRestriction.RollRule switch
            {
                RollResult.Passed => "passing",
                RollResult.Greeded => "greeding",
                RollResult.Needed => "needing",
                _ => "passing"
            };
            Svc.Log.Debug(
                $"{lootItem.Value.Name.ToString()} is {action} due to being in {contentFinderInfo.Name}. [Duty Custom Restriction]");

            return dutyCustomRestriction.RollRule;
        }
    }

    private static void AddDiagnostic(List<string>? diagnostics, string message, bool gated = true)
    {
        if (!gated || LazyLoot.Config.DiagnosticsMode)
            DuoLog.Debug(message);
        diagnostics?.Add(message);
    }

    private static bool ShouldPassUnlockable(bool restriction, bool onlyUntradeable, Item? item)
    {
        return restriction && (!onlyUntradeable || (onlyUntradeable && item!.Value.IsUntradable));
    }

    internal static bool IsUnlockableItem(Item item)
    {
        UpdateFadedCopy(item.RowId, out var orchId);
        return orchId.Count > 0 || LazyLoot.UnlockState.IsItemUnlockable(item);
    }

    private static RollResult GetUnlockablesOnlyResult(uint itemId, Item? lootItem, List<string>? diagnostics = null)
    {
        if (lootItem == null)
            return RollResult.Passed;

        if (!IsUnlockableItem(lootItem.Value))
        {
            AddDiagnostic(diagnostics,
                $"{lootItem.Value.Name.ToString()} has been passed due to Unlockables Only mode. [Unlockables Only - Not an Unlockable]");
            return RollResult.Passed;
        }

        // Let FULF decide how to roll on unlockables that are not unlocked yet.
        if (!IsItemUnlocked(itemId)) return RollResult.UnAwarded;

        AddDiagnostic(diagnostics,
            $"{lootItem.Value.Name.ToString()} has been passed because it is already unlocked. [Unlockables Only - Already Unlocked]");

        return RollResult.Passed;
    }

    private static unsafe RollResult GetPlayerRestrictByItemId(uint itemId, bool canNeed,
        List<string>? diagnostics = null)
    {
        var lootItem = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId);

        UpdateFadedCopy(itemId, out var orchId);

        if (lootItem == null)
        {
            AddDiagnostic(diagnostics,
                $"Passing due to unknown item? Please give this ID to the developers: {itemId} [Unknown ID]",
                gated: false);
            return RollResult.Passed;
        }

        if (LazyLoot.Config.UnlockablesOnlyMode)
            return GetUnlockablesOnlyResult(itemId, lootItem, diagnostics);

        if (lootItem.Value.IsUnique && ItemCount(itemId) > 0)
        {
            AddDiagnostic(diagnostics,
                $"{lootItem.Value.Name} has been passed due to being unique and you already possess one. [Unique Item]");
            return RollResult.Passed;
        }

        // Make sure the item is level 1, with ilv of 1 and is equipment
        if (LazyLoot.Config.NeverPassGlam && lootItem.Value is { LevelEquip: 1, LevelItem.Value.RowId: 1 } &&
            lootItem.Value.EquipSlotCategory.Value.RowId != 0)
        {
            AddDiagnostic(diagnostics,
                $"{lootItem.Value.Name} has been set to not pass if possible due to being set to never skip glamour items. [Never Pass Glam]");
            return RollResult.Needed;
        }

        // This checks for faded orchestrion rolls and if their actual orchestrion roll is unlocked, by either all or specific selection
        if (orchId.Count > 0 && orchId.All(IsItemUnlocked))
        {
            if (ShouldPassUnlockable(LazyLoot.Config.RestrictionIgnoreItemUnlocked,
                    LazyLoot.Config.RestrictionAllUnlockablesOnlyUntradeables, lootItem))
            {
                AddDiagnostic(diagnostics,
                    $@"{lootItem.Value.Name} has been passed due to being unlocked and you have ""Pass on all items already unlocked"" enabled. [Pass All Unlocked]");
                return RollResult.Passed;
            }

            if (ShouldPassUnlockable(LazyLoot.Config.RestrictionIgnoreFadedCopy,
                    LazyLoot.Config.RestrictionFadedCopyOnlyUntradeables, lootItem) &&
                lootItem.Value is { FilterGroup: 12, ItemUICategory.RowId: 94 })
            {
                AddDiagnostic(diagnostics,
                    $@"{lootItem.Value.Name} has been passed due to being unlocked and you have ""Pass on unlocked Faded Copies"" enabled. [Pass Faded Copies]");
                return RollResult.Passed;
            }
        }

        if (IsItemUnlocked(itemId))
        {
            if (ShouldPassUnlockable(LazyLoot.Config.RestrictionIgnoreItemUnlocked,
                    LazyLoot.Config.RestrictionAllUnlockablesOnlyUntradeables, lootItem))
            {
                AddDiagnostic(diagnostics,
                    $@"{lootItem.Value.Name} has been passed due to being unlocked and you have ""Pass on all items already unlocked"" enabled. [Pass All Unlocked]");
                return RollResult.Passed;
            }

            if (ShouldPassUnlockable(LazyLoot.Config.RestrictionIgnoreMounts,
                    LazyLoot.Config.RestrictionMountsOnlyUntradeables, lootItem) &&
                lootItem.Value.ItemAction.Value.Action.Value.RowId == 1322)
            {
                AddDiagnostic(diagnostics,
                    $@"{lootItem.Value.Name} has been passed due to being unlocked and you have ""Pass on unlocked mounts"" enabled. [Pass Unlocked Mounts]");
                return RollResult.Passed;
            }

            if (ShouldPassUnlockable(LazyLoot.Config.RestrictionIgnoreMinions,
                    LazyLoot.Config.RestrictionMinionsOnlyUntradeables, lootItem) &&
                lootItem.Value.ItemAction.Value.Action.Value.RowId == 853)
            {
                AddDiagnostic(diagnostics,
                    $@"{lootItem.Value.Name} has been passed due to being unlocked and you have ""Pass on unlocked minions"" enabled. [Pass Unlocked Minions]");
                return RollResult.Passed;
            }

            if (ShouldPassUnlockable(LazyLoot.Config.RestrictionIgnoreBardings,
                    LazyLoot.Config.RestrictionBardingsOnlyUntradeables, lootItem) &&
                lootItem.Value.ItemAction.Value.Action.Value.RowId == 1013)
            {
                AddDiagnostic(diagnostics,
                    $@"{lootItem.Value.Name} has been passed due to being unlocked and you have ""Pass on unlocked bardings"" enabled. [Pass Unlocked Bardings]");
                return RollResult.Passed;
            }

            if (ShouldPassUnlockable(LazyLoot.Config.RestrictionIgnoreEmoteHairstyle,
                    LazyLoot.Config.RestrictionEmoteHairstyleOnlyUntradeables, lootItem) &&
                lootItem.Value.ItemAction.Value.Action.Value.RowId == 2633)
            {
                AddDiagnostic(diagnostics,
                    $@"{lootItem.Value.Name} has been passed due to being unlocked and you have ""Pass on unlocked emotes and hairstyles"" enabled. [Pass Unlocked Emote/Hairstyle]");
                return RollResult.Passed;
            }

            if (ShouldPassUnlockable(LazyLoot.Config.RestrictionIgnoreTripleTriadCards,
                    LazyLoot.Config.RestrictionTripleTriadCardsOnlyUntradeables, lootItem) &&
                lootItem.Value.ItemAction.Value.Action.Value.RowId == 3357)
            {
                AddDiagnostic(diagnostics,
                    $@"{lootItem.Value.Name} has been passed due to being unlocked and you have ""Pass on unlocked triple triad cards"" enabled. [Pass Unlocked Cards]");
                return RollResult.Passed;
            }

            if (ShouldPassUnlockable(LazyLoot.Config.RestrictionIgnoreOrchestrionRolls,
                    LazyLoot.Config.RestrictionOrchestrionRollsOnlyUntradeables, lootItem) &&
                lootItem.Value.ItemAction.Value.Action.Value.RowId == 25183)
            {
                AddDiagnostic(diagnostics,
                    $@"{lootItem.Value.Name} has been passed due to being unlocked and you have ""Pass on unlocked orchestrion rolls"" enabled. [Pass Unlocked Orchestrions]");
                return RollResult.Passed;
            }
        }

        if (LazyLoot.Config.RestrictionSeals)
            if (lootItem.Value is { Rarity: > 1, PriceLow: > 0, ClassJobCategory.RowId: > 0 })
            {
                var gcSealValue = Svc.Data.Excel.GetSheet<GCSupplyDutyReward>()?.GetRow(lootItem.Value.LevelItem.RowId)
                    .SealsExpertDelivery;

                if (gcSealValue < LazyLoot.Config.RestrictionSealsAmnt)
                {
                    AddDiagnostic(diagnostics,
                        $@"{lootItem.Value.Name} has been passed due to not reaching your set seals amount (Set: {LazyLoot.Config.RestrictionSealsAmnt} | Item: {gcSealValue} ) [Pass Seals]");
                    return RollResult.Passed;
                }
            }

        if (lootItem.Value.EquipSlotCategory.RowId != 0)
        {
            if (LazyLoot.Config.RestrictionLootLowerThanJobIlvl)
                if (lootItem.Value.LevelItem.RowId <
                    Utils.GetPlayerIlevel() - LazyLoot.Config.RestrictionLootLowerThanJobIlvlTreshold)
                {
                    var toReturn = LazyLoot.Config.RestrictionLootLowerThanJobIlvlRollState == 0
                        ? RollResult.Greeded
                        : RollResult.Passed;
                    if (toReturn == RollResult.Greeded)
                        AddDiagnostic(diagnostics,
                            $@"{lootItem.Value.Name} has been set to greed due to its iLvl being lower than your your current Job (Your: {Utils.GetPlayerIlevel()} | Item: {lootItem.Value.LevelItem.RowId} ) [Greed Item Level Job]");
                    else if (toReturn == RollResult.Passed)
                        AddDiagnostic(diagnostics,
                            $@"{lootItem.Value.Name} has been passed due to its iLvl being lower than your your current Job (Your: {Utils.GetPlayerIlevel()} | Item: {lootItem.Value.LevelItem.RowId} ) [Pass Item Level Job]");
                    return toReturn;
                }

            if (LazyLoot.Config.RestrictionIgnoreItemLevelBelow
                && lootItem.Value.LevelItem.RowId < LazyLoot.Config.RestrictionIgnoreItemLevelBelowValue)
            {
                AddDiagnostic(diagnostics,
                    $@"{lootItem.Value.Name} has been passed due to not reaching the item level required (Set: {LazyLoot.Config.RestrictionIgnoreItemLevelBelowValue} | Item: {lootItem.Value.LevelItem.RowId} ) [Pass Item Level]");
                return RollResult.Passed;
            }

            if (LazyLoot.Config.RestrictionLootIsJobUpgrade)
            {
                // seu bloco de verificar equipped continua igual
                var lootItemSlot = lootItem.Value.EquipSlotCategory.RowId;
                var itemsToVerify = new List<uint>();
                var equippedItems =
                    InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems);

                for (var i = 0; i < equippedItems->Size; i++)
                {
                    var equippedItem = equippedItems->GetInventorySlot(i);
                    var equippedItemData = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(equippedItem->ItemId);
                    if (equippedItemData == null) continue;
                    if (equippedItemData.Value.EquipSlotCategory.RowId != lootItemSlot) continue;
                    itemsToVerify.Add(equippedItemData.Value.LevelItem.RowId);
                }

                if (itemsToVerify.Count > 0 && itemsToVerify.Min() > lootItem.Value.LevelItem.RowId)
                {
                    var toReturn = LazyLoot.Config.RestrictionLootIsJobUpgradeRollState == 0
                        ? RollResult.Greeded
                        : RollResult.Passed;
                    if (toReturn == RollResult.Greeded)
                        AddDiagnostic(diagnostics,
                            $@"{lootItem.Value.Name} has been set to greed due to its iLvl being lower than your your current equipped item (Item: {lootItem.Value.LevelItem.RowId} | Your: {itemsToVerify.Min()} ) [Greed Item Level Job]");
                    else if (toReturn == RollResult.Passed)
                        AddDiagnostic(diagnostics,
                            $@"{lootItem.Value.Name} has been passed due to its iLvl being lower than your your current equipped item (Item: {lootItem.Value.LevelItem.RowId} | Your: {itemsToVerify.Min()} ) [Pass Item Level Job]");
                    return toReturn;
                }
            }

            if (LazyLoot.Config.RestrictionOtherJobItems && !canNeed)
            {
                AddDiagnostic(diagnostics,
                    $@"{lootItem.Value.Name} has been passed due to not being an item to your current job ({Player.Object?.ClassJob.Value.Name}) [Pass Not For Job]",
                    gated: false);
                return RollResult.Passed;
            }
        }

        if (LazyLoot.Config.RestrictionOtherJobItems && lootItem.Value.ItemAction.Value.Action.Value.RowId == 29153 &&
            Player.Object?.ClassJob.RowId is not (1 or 19))
        {
            AddDiagnostic(diagnostics,
                $@"{lootItem.Value.Name} has been passed due to not being an item to your current job and is a GLA/PLD weapon set ({Player.Object?.ClassJob.Value.Name}) [Pass Not For Job]",
                gated: false);
            return RollResult.Passed;
        }

        return RollResult.UnAwarded;
    }

    internal static void UpdateFadedCopy(uint itemId, out List<uint> orchId, bool log = true)
    {
        orchId = [];
        var lumina = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId);
        if (lumina is not { FilterGroup: 12, ItemUICategory.RowId: 94 }) return;
        var recipe = Svc.Data.GetExcelSheet<Recipe>()
            ?.Where(x => x.Ingredient.Any(y => y.RowId == lumina.Value.RowId)).Select(x => x.ItemResult.Value)
            .FirstOrDefault();
        if (recipe == null) return;
        if (log && LazyLoot.Config.DiagnosticsMode)
            DuoLog.Debug(
                $"Updating Faded Copy {lumina.Value.Name} ({itemId}) to Non-Faded {recipe.Value.Name} ({recipe.Value.RowId})");
        orchId.Add(recipe.Value.RowId);
    }

    internal static bool IsUnlockableAndUnlocked(Item item)
    {
        if (item.RowId == 0)
            return false;
        return IsUnlockableItem(item) && IsItemUnlocked(item.RowId);
    }

    private static RollResult ResultMerge(params RollResult[] results)
    {
        return results.Max() switch
        {
            RollResult.Needed => RollResult.Needed,
            RollResult.Greeded => RollResult.Greeded,
            _ => RollResult.Passed
        };
    }

    private static RollResult ResolveFinalRoll(
        RollResult baseOption,
        RollResult maxAllowed,
        RollResult? customRule,
        RollResult playerRule)
    {
        if (customRule.HasValue)
            return MoreRestrictive(customRule.Value, maxAllowed);

        var desired = ApplyPlayerRule(baseOption, playerRule);
        return MoreRestrictive(desired, maxAllowed);
    }

    private static RollResult ApplyPlayerRule(RollResult baseOption, RollResult rule)
    {
        return rule == RollResult.UnAwarded ? baseOption : rule;
    }

    private static RollResult MoreRestrictive(RollResult left, RollResult right)
    {
        return (RollResult)Math.Max((int)left, (int)right);
    }

    private static RollResult MoreAggressive(RollResult left, RollResult right)
    {
        return (RollResult)Math.Min((int)left, (int)right);
    }


    private static unsafe bool GetNextLootItem(out uint i, out LootItem loot)
    {
        var span = Loot.Instance()->Items;
        for (i = 0; i < span.Length; i++)
        {
            loot = span[(int)i];

            if (loot.ItemId >= 1000000)
                loot.ItemId -= 1000000;
            if (loot.ChestObjectId is 0 or 0xE0000000)
                continue;
            if (loot.RollResult != RollResult.UnAwarded)
                continue;
            if (loot.RollState is RollState.Rolled or RollState.Unavailable or RollState.Unknown)
                continue;
            if (loot.ItemId == 0)
                continue;
            if (loot.LootMode is LootMode.LootMasterGreedOnly or LootMode.Unavailable)
                continue;

            var checkWeekly = LazyLoot.Config.RestrictionWeeklyLockoutItems &&
                              !LazyLoot.Config.UnlockablesOnlyMode;

            var lootId = loot.ItemId;
            var contentFinderInfo = Svc.Data.GetExcelSheet<ContentFinderCondition>()
                .GetRow(GameMain.Instance()->CurrentContentFinderConditionId);

            // We load the users restrictions
            var itemCustomRestriction = LazyLoot.Config.FindEnabledItemRestriction(lootId);
            var dutyCustomRestriction = LazyLoot.Config.FindEnabledDutyRestriction(contentFinderInfo.RowId);
            if (dutyCustomRestriction is not { RollRule: RollResult.UnAwarded })
                dutyCustomRestriction = null;

            Item? item = null;

            if (LazyLoot.Config.DiagnosticsMode)
                // Only load the item if diagnostic mode is on
                item = Svc.Data.GetExcelSheet<Item>().GetRow(loot.ItemId);

            if (itemCustomRestriction != null)
            {
                if (itemCustomRestriction.RollRule == RollResult.UnAwarded)
                {
                    if (LazyLoot.Config.DiagnosticsMode)
                        DuoLog.Debug(
                            $"{item?.Name.ToString()} is being ignored. [Item Custom Restriction]");
                    continue;
                }

                checkWeekly = false;
            }

            if (itemCustomRestriction == null && dutyCustomRestriction != null)
            {
                if (dutyCustomRestriction.RollRule == RollResult.UnAwarded)
                {
                    if (LazyLoot.Config.DiagnosticsMode)
                        DuoLog.Debug(
                            $"{item?.Name.ToString()} is being ignored due to being in {contentFinderInfo.Name}. [Duty Custom Restriction]");
                    continue;
                }

                checkWeekly = false;
            }

            // loot.RollValue == 20 means it can't be rolled because one was already obtained this week.
            // we ignore that so it will be passed automatically, as there is nothing the user can do other than
            // pass it
            if (loot.WeeklyLootItem && (byte)loot.RollState != 20 && checkWeekly)
                continue;

            return true;
        }

        loot = default;
        return false;
    }

    private static unsafe void RollItem(RollResult option, uint index)
    {
        try
        {
            _rollItemRaw ??=
                Marshal.GetDelegateForFunctionPointer<RollItemRaw>(
                    Svc.SigScanner.ScanText("41 83 F8 ?? 0F 83 ?? ?? ?? ?? 48 89 5C 24 08"));
            _rollItemRaw?.Invoke(Loot.Instance(), option, index);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "Warning at roll");
        }
    }

    private static unsafe int ItemCount(uint itemId)
    {
        return InventoryManager.Instance()->GetInventoryItemCount(itemId);
    }

    public static bool IsItemUnlocked(uint itemId)
    {
        UpdateFadedCopy(itemId, out var orchId, false);
        if (orchId.Count > 0) return orchId.All(IsItemUnlocked);
        var item = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId);
        return item != null && LazyLoot.UnlockState.IsItemUnlocked(item.Value);
    }

    public static uint ConvertSealsToIlvl(int sealAmnt)
    {
        var sealsSheet = Svc.Data.GetExcelSheet<GCSupplyDutyReward>();
        uint ilvl = 0;
        foreach (var row in sealsSheet.Where(row => row.SealsExpertDelivery < sealAmnt))
            ilvl = row.RowId;

        return ilvl;
    }

    internal static LlDecision WhatWouldLlDo(uint itemId, RollState rollState)
    {
        var trace = ExplainDecision(itemId, rollState);
        return ToDecision(trace.Final);
    }

    internal static LlDecisionTrace ExplainDecision(uint itemId, RollState rollState)
    {
        var trace = new LlDecisionTrace
        {
            BaseIntent = LazyLoot.Config.FulfRoll switch
            {
                0 => RollResult.Needed,
                1 => RollResult.Greeded,
                2 => RollResult.Passed,
                _ => RollResult.UnAwarded
            }
        };

        trace.CustomRule = GetCustomRuleByItemId(itemId);
        trace.MaxAllowed = GetRestrictResultAssumingUpTo(itemId, rollState);
        trace.PlayerRule = trace.CustomRule == null
            ? GetPlayerRestrictByItemId(itemId, rollState == RollState.UpToNeed, trace.Diagnostics)
            : RollResult.UnAwarded;
        trace.Final = ResolveFinalRoll(trace.BaseIntent, trace.MaxAllowed, trace.CustomRule, trace.PlayerRule);

        return trace;
    }

    private static RollResult? GetCustomRuleByItemId(uint itemId)
    {
        var lootItem = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId);
        if (lootItem == null || (lootItem.Value.IsUnique && ItemCount(itemId) > 0)) return RollResult.Passed;

        var itemCustom = LazyLoot.Config.FindEnabledItemRestriction(itemId);
        if (itemCustom != null) return itemCustom.RollRule;

        return null;
    }

    private static RollResult GetRestrictResultAssumingUpToNeedNormal(uint itemId)
    {
        return GetRestrictResultAssumingUpTo(itemId, RollState.UpToNeed);
    }

    private static RollResult GetRestrictResultAssumingUpTo(uint itemId, RollState rollState)
    {
        var item = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId);
        if (item == null) return RollResult.Passed;

        var stateMax = rollState switch
        {
            RollState.UpToNeed => RollResult.Needed,
            RollState.UpToGreed => RollResult.Greeded,
            _ => RollResult.Passed
        };
        const RollResult ruleMax = RollResult.Needed;
        if (item.Value.IsUnique && IsItemUnlocked(itemId)) stateMax = RollResult.Passed;

        return ResultMerge(stateMax, ruleMax);
    }

    private static LlDecision ToDecision(RollResult final)
    {
        return final switch
        {
            RollResult.UnAwarded => LlDecision.DoNothing,
            RollResult.Passed => LlDecision.Pass,
            RollResult.Greeded => LlDecision.Greed,
            RollResult.Needed => LlDecision.Need,
            _ => LlDecision.Pass
        };
    }

    private unsafe delegate bool RollItemRaw(Loot* lootIntPtr, RollResult option, uint lootItemIndex);

    internal sealed class LlDecisionTrace
    {
        public RollResult BaseIntent { get; init; }
        public RollResult MaxAllowed { get; set; }
        public RollResult? CustomRule { get; set; }
        public RollResult PlayerRule { get; set; }
        public RollResult Final { get; set; }
        public List<string> Diagnostics { get; } = [];
    }

    internal enum LlDecision
    {
        DoNothing = 0,
        Pass = 1,
        Greed = 2,
        Need = 3
    }
}
