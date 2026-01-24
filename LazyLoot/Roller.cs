using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.Exd;
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

        // Make sure we ignore the fulf state if the user has custom item rules
        // Otherwise, we make sure the option is a valid roll
        var userRules = GetPlayerCustomRestrict(loot);
        option = userRules != null
            ? ResultMerge(GetRestrictResult(loot), (RollResult)userRules)
            : ResultMerge(option, GetRestrictResult(loot),
                GetPlayerRestrictByItemId(loot.ItemId, loot.RollState == RollState.UpToNeed));

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
            if (LazyLoot.Config.DiagnosticsMode)
            {
                var action = itemCustomRestriction.RollRule == RollResult.Passed ? "passing" :
                    itemCustomRestriction.RollRule == RollResult.Greeded ? "greeding" :
                    itemCustomRestriction.RollRule == RollResult.Needed ? "needing" : "passing";
                Svc.Log.Debug($"{lootItem.Value.Name.ToString()} is {action}. [Item Custom Restriction]");
            }

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

    private static bool ShouldPassUnlockable(bool restriction, bool onlyUntradeable, Item? item)
    {
        return restriction && (!onlyUntradeable || (onlyUntradeable && item!.Value.IsUntradable));
    }

    private static bool IsUnlockableItem(Item item, IReadOnlyCollection<uint> orchId)
    {
        if (orchId.Count > 0)
            return true;

        return item.ItemAction.Value.Action.Value.RowId is 853 /* Minion */ or 1013 or 1322 or 2633 or 3357
            or 20086 /* Parasols and stuff */ or 25183 /* Orchestrations */ or 37312 /* Faces we wear */;
    }

    private static bool IsUnlockableUnlocked(uint itemId, IReadOnlyCollection<uint> orchId)
    {
        return orchId.Count > 0 ? orchId.All(IsItemUnlocked) : IsItemUnlocked(itemId);
    }

    private static RollResult GetUnlockablesOnlyResult(uint itemId, Item? lootItem, List<uint> orchId)
    {
        if (lootItem == null)
            return RollResult.Passed;

        if (!IsUnlockableItem(lootItem.Value, orchId))
        {
            if (LazyLoot.Config.DiagnosticsMode)
                DuoLog.Debug(
                    $"{lootItem.Value.Name.ToString()} has been passed due to Unlockables Only mode. [Unlockables Only - Not an Unlockable]");
            return RollResult.Passed;
        }

        // Returns Needed to allow fulf to reduce it to whatever it is set to
        if (!IsUnlockableUnlocked(itemId, orchId)) return RollResult.Needed;

        if (LazyLoot.Config.DiagnosticsMode)
            DuoLog.Debug(
                $"{lootItem.Value.Name.ToString()} has been passed because it is already unlocked. [Unlockables Only - Already Unlocked]");

        return RollResult.Passed;
    }

    private static unsafe RollResult GetPlayerRestrictByItemId(uint itemId, bool canNeed)
    {
        var lootItem = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId);

        UpdateFadedCopy(itemId, out var orchId);

        if (lootItem == null)
        {
            DuoLog.Debug($"Passing due to unknown item? Please give this ID to the developers: {itemId} [Unknown ID]");
            return RollResult.Passed;
        }

        if (LazyLoot.Config.UnlockablesOnlyMode)
            return GetUnlockablesOnlyResult(itemId, lootItem, orchId);

        if (lootItem.Value.IsUnique && ItemCount(itemId) > 0)
        {
            if (LazyLoot.Config.DiagnosticsMode)
                DuoLog.Debug(
                    $"{lootItem.Value.Name} has been passed due to being unique and you already possess one. [Unique Item]");
            return RollResult.Passed;
        }

        // Make sure the item is level 1, with ilv of 1 and is equipment
        if (LazyLoot.Config.NeverPassGlam && lootItem.Value is { LevelEquip: 1, LevelItem.Value.RowId: 1 } &&
            lootItem.Value.EquipSlotCategory.Value.RowId != 0)
        {
            if (LazyLoot.Config.DiagnosticsMode)
                DuoLog.Debug(
                    $"{lootItem.Value.Name} has been set to not pass if possible due to being set to never skip glamour items. [Never Pass Glam]");
            return RollResult.Needed;
        }

        // This checks for faded orchestrion rolls and if their actual orchestrion roll is unlocked, by either all or specific selection
        if (orchId.Count > 0 && orchId.All(IsItemUnlocked))
        {
            if (ShouldPassUnlockable(LazyLoot.Config.RestrictionIgnoreItemUnlocked,
                    LazyLoot.Config.RestrictionAllUnlockablesOnlyUntradeables, lootItem))
            {
                if (LazyLoot.Config.DiagnosticsMode)
                    DuoLog.Debug(
                        $@"{lootItem.Value.Name} has been passed due to being unlocked and you have ""Pass on all items already unlocked"" enabled. [Pass All Unlocked]");
                return RollResult.Passed;
            }

            if (ShouldPassUnlockable(LazyLoot.Config.RestrictionIgnoreFadedCopy,
                    LazyLoot.Config.RestrictionFadedCopyOnlyUntradeables, lootItem) &&
                lootItem.Value is { FilterGroup: 12, ItemUICategory.RowId: 94 })
            {
                if (LazyLoot.Config.DiagnosticsMode)
                    DuoLog.Debug(
                        $@"{lootItem.Value.Name} has been passed due to being unlocked and you have ""Pass on unlocked Faded Copies"" enabled. [Pass Faded Copies]");
                return RollResult.Passed;
            }
        }

        if (IsItemUnlocked(itemId))
        {
            if (ShouldPassUnlockable(LazyLoot.Config.RestrictionIgnoreItemUnlocked,
                    LazyLoot.Config.RestrictionAllUnlockablesOnlyUntradeables, lootItem))
            {
                if (LazyLoot.Config.DiagnosticsMode)
                    DuoLog.Debug(
                        $@"{lootItem.Value.Name} has been passed due to being unlocked and you have ""Pass on all items already unlocked"" enabled. [Pass All Unlocked]");
                return RollResult.Passed;
            }

            if (ShouldPassUnlockable(LazyLoot.Config.RestrictionIgnoreMounts,
                    LazyLoot.Config.RestrictionMountsOnlyUntradeables, lootItem) &&
                lootItem.Value.ItemAction.Value.Action.Value.RowId == 1322)
            {
                if (LazyLoot.Config.DiagnosticsMode)
                    DuoLog.Debug(
                        $@"{lootItem.Value.Name} has been passed due to being unlocked and you have ""Pass on unlocked mounts"" enabled. [Pass Unlocked Mounts]");
                return RollResult.Passed;
            }

            if (ShouldPassUnlockable(LazyLoot.Config.RestrictionIgnoreMinions,
                    LazyLoot.Config.RestrictionMinionsOnlyUntradeables, lootItem) &&
                lootItem.Value.ItemAction.Value.Action.Value.RowId == 853)
            {
                if (LazyLoot.Config.DiagnosticsMode)
                    DuoLog.Debug(
                        $@"{lootItem.Value.Name} has been passed due to being unlocked and you have ""Pass on unlocked minions"" enabled. [Pass Unlocked Minions]");
                return RollResult.Passed;
            }

            if (ShouldPassUnlockable(LazyLoot.Config.RestrictionIgnoreBardings,
                    LazyLoot.Config.RestrictionBardingsOnlyUntradeables, lootItem) &&
                lootItem.Value.ItemAction.Value.Action.Value.RowId == 1013)
            {
                if (LazyLoot.Config.DiagnosticsMode)
                    DuoLog.Debug(
                        $@"{lootItem.Value.Name} has been passed due to being unlocked and you have ""Pass on unlocked bardings"" enabled. [Pass Unlocked Bardings]");
                return RollResult.Passed;
            }

            if (ShouldPassUnlockable(LazyLoot.Config.RestrictionIgnoreEmoteHairstyle,
                    LazyLoot.Config.RestrictionEmoteHairstyleOnlyUntradeables, lootItem) &&
                lootItem.Value.ItemAction.Value.Action.Value.RowId == 2633)
            {
                if (LazyLoot.Config.DiagnosticsMode)
                    DuoLog.Debug(
                        $@"{lootItem.Value.Name} has been passed due to being unlocked and you have ""Pass on unlocked emotes and hairstyles"" enabled. [Pass Unlocked Emote/Hairstyle]");
                return RollResult.Passed;
            }

            if (ShouldPassUnlockable(LazyLoot.Config.RestrictionIgnoreTripleTriadCards,
                    LazyLoot.Config.RestrictionTripleTriadCardsOnlyUntradeables, lootItem) &&
                lootItem.Value.ItemAction.Value.Action.Value.RowId == 3357)
            {
                if (LazyLoot.Config.DiagnosticsMode)
                    DuoLog.Debug(
                        $@"{lootItem.Value.Name} has been passed due to being unlocked and you have ""Pass on unlocked triple triad cards"" enabled. [Pass Unlocked Cards]");
                return RollResult.Passed;
            }

            if (ShouldPassUnlockable(LazyLoot.Config.RestrictionIgnoreOrchestrionRolls,
                    LazyLoot.Config.RestrictionOrchestrionRollsOnlyUntradeables, lootItem) &&
                lootItem.Value.ItemAction.Value.Action.Value.RowId == 25183)
            {
                if (LazyLoot.Config.DiagnosticsMode)
                    DuoLog.Debug(
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
                    if (LazyLoot.Config.DiagnosticsMode)
                        DuoLog.Debug(
                            $@"{lootItem.Value.Name} has been passed due to not reaching your set seals amount (Set: {LazyLoot.Config.RestrictionSealsAmnt} | Item: {gcSealValue} ) [Pass Seals]");
                    return RollResult.Passed;
                }
            }

        if (lootItem.Value.EquipSlotCategory.RowId != 0)
        {
            if (LazyLoot.Config.RestrictionLootLowerThanJobIlvl && canNeed)
                if (lootItem.Value.LevelItem.RowId <
                    Utils.GetPlayerIlevel() - LazyLoot.Config.RestrictionLootLowerThanJobIlvlTreshold)
                {
                    var toReturn = LazyLoot.Config.RestrictionLootLowerThanJobIlvlRollState == 0
                        ? RollResult.Greeded
                        : RollResult.Passed;
                    if (toReturn == RollResult.Passed && LazyLoot.Config.DiagnosticsMode)
                        DuoLog.Debug(
                            $@"{lootItem.Value.Name} has been passed due to its iLvl being lower than your your current Job (Your: {Utils.GetPlayerIlevel()} | Item: {lootItem.Value.LevelItem.RowId} ) [Pass Item Level Job]");
                    return toReturn;
                }

            if (LazyLoot.Config.RestrictionIgnoreItemLevelBelow
                && lootItem.Value.LevelItem.RowId < LazyLoot.Config.RestrictionIgnoreItemLevelBelowValue)
            {
                if (LazyLoot.Config.DiagnosticsMode)
                    DuoLog.Debug(
                        $@"{lootItem.Value.Name} has been passed due to not reaching the item level required (Set: {LazyLoot.Config.RestrictionIgnoreItemLevelBelowValue} | Item: {lootItem.Value.LevelItem.RowId} ) [Pass Item Level]");
                return RollResult.Passed;
            }

            if (LazyLoot.Config.RestrictionLootIsJobUpgrade && canNeed)
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
                    if (toReturn == RollResult.Passed && LazyLoot.Config.DiagnosticsMode)
                        DuoLog.Debug(
                            $@"{lootItem.Value.Name} has been passed due to its iLvl being lower than your your current equipped item (Item: {lootItem.Value.LevelItem.RowId} | Your: {itemsToVerify.Min()} ) [Pass Item Level Job]");
                    return toReturn;
                }
            }

            if (LazyLoot.Config.RestrictionOtherJobItems && !canNeed)
            {
                DuoLog.Debug(
                    $@"{lootItem.Value.Name} has been passed due to not being an item to your current job ({Player.Object?.ClassJob.Value.Name}) [Pass Not For Job]");
                return RollResult.Passed;
            }
        }

        if (LazyLoot.Config.RestrictionOtherJobItems && lootItem.Value.ItemAction.Value.Action.Value.RowId == 29153 &&
            Player.Object?.ClassJob.RowId is not (1 or 19))
        {
            DuoLog.Debug(
                $@"{lootItem.Value.Name} has been passed due to not being an item to your current job and is a GLA/PLD weapon set ({Player.Object?.ClassJob.Value.Name}) [Pass Not For Job]");
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
        UpdateFadedCopy(item.RowId, out var orchId, false);
        return IsUnlockableItem(item, orchId) && IsUnlockableUnlocked(item.RowId, orchId);
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

    public static unsafe bool IsItemUnlocked(uint itemId)
    {
        var exdItem = ExdModule.GetItemRowById(itemId);
        return exdItem is null || UIState.Instance()->IsItemActionUnlocked(exdItem) is 1;
    }

    public static uint ConvertSealsToIlvl(int sealAmnt)
    {
        var sealsSheet = Svc.Data.GetExcelSheet<GCSupplyDutyReward>();
        uint ilvl = 0;
        foreach (var row in sealsSheet.Where(row => row.SealsExpertDelivery < sealAmnt))
            ilvl = row.RowId;

        return ilvl;
    }

    internal static LlDecision WhatWouldLlDo(uint itemId)
    {
        var baseIntent = LazyLoot.Config.FulfRoll switch
        {
            0 => RollResult.Needed,
            1 => RollResult.Greeded,
            2 => RollResult.Passed,
            _ => RollResult.UnAwarded
        };

        var custom = GetCustomRuleByItemId(itemId);
        var chosen = custom ?? baseIntent;
        var maxAllowed = GetRestrictResultAssumingUpToNeedNormal(itemId);
        var userRestrict = GetPlayerRestrictByItemId(itemId, true);
        var final = MergeWithUnAwarded(chosen, maxAllowed, userRestrict);

        return final switch
        {
            RollResult.UnAwarded => LlDecision.DoNothing,
            RollResult.Passed => LlDecision.Pass,
            RollResult.Greeded => LlDecision.Greed,
            RollResult.Needed => LlDecision.Need,
            _ => LlDecision.Pass
        };
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
        var item = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId);
        if (item == null) return RollResult.Passed;

        var stateMax = RollResult.Needed;
        const RollResult ruleMax = RollResult.Needed;
        if (item.Value.IsUnique && IsItemUnlocked(itemId)) stateMax = RollResult.Passed;

        return ResultMerge(stateMax, ruleMax);
    }

    private static RollResult MergeWithUnAwarded(params RollResult[] results)
    {
        if (results.All(r => r == RollResult.UnAwarded)) return RollResult.UnAwarded;

        var filtered = results.Where(r => r != RollResult.UnAwarded).ToArray();
        return filtered.Length == 0 ? RollResult.UnAwarded : ResultMerge(filtered);
    }

    private unsafe delegate bool RollItemRaw(Loot* lootIntPtr, RollResult option, uint lootItemIndex);

    internal enum LlDecision
    {
        DoNothing = 0,
        Pass = 1,
        Greed = 2,
        Need = 3
    }
}