using System.Collections.Generic;
using System.Linq;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace LazyLoot;

internal static partial class Roller
{
    private static RollResult GetRestrictResult(RollContext context)
    {
        var item = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(context.ItemId);
        if (item == null)
            return RollResult.Passed;

        //Checks what the max possible roll type on the item is
        var stateMax = context.RollState switch
        {
            RollState.UpToNeed => RollResult.Needed,
            RollState.UpToGreed => RollResult.Greeded,
            _ => RollResult.Passed
        };

        if (item.Value.IsUnique && IsItemUnlocked(context.ItemId))
            stateMax = RollResult.Passed;

        if (LazyLoot.Config.DiagnosticsMode && stateMax == RollResult.Passed)
            DuoLog.Debug($"{item.Value.Name.ToString()} can only be passed on. [RollState UpToPass]");

        //Checks what the player set loot rules are
        var ruleMax = context.LootMode switch
        {
            LootMode.Normal => RollResult.Needed,
            LootMode.GreedOnly => RollResult.Greeded,
            _ => RollResult.Passed
        };

        return ResultMerge(stateMax, ruleMax);
    }

    private static unsafe RollResult? GetCustomRuleForContext(RollContext context, out string source)
    {
        source = "None";
        var lootItem = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(context.ItemId);
        if (lootItem == null || (lootItem.Value.IsUnique && ItemCount(context.ItemId) > 0))
        {
            source = "System";
            return RollResult.Passed;
        }

        // Here, we will check for the specific rules for items.
        var itemCustomRestriction = LazyLoot.Config.FindEnabledItemRestriction(context.ItemId);
        if (itemCustomRestriction is { Enabled: true })
        {
            source = "Item";
            if (!LazyLoot.Config.DiagnosticsMode) return itemCustomRestriction.RollRule;

            var action = itemCustomRestriction.RollRule == RollResult.Passed ? "passing" :
                itemCustomRestriction.RollRule == RollResult.Greeded ? "greeding" :
                itemCustomRestriction.RollRule == RollResult.Needed ? "needing" : "passing";
            Svc.Log.Debug($"{lootItem.Value.Name.ToString()} is {action}. [Item Custom Restriction]");

            return itemCustomRestriction.RollRule;
        }

        // Here, we will check for the specific rules for the Duty.
        if (context.DutyId is null or 0)
            return null;

        var contentFinderInfo = Svc.Data.GetExcelSheet<ContentFinderCondition>()
            .GetRow(context.DutyId.Value);
        var dutyCustomRestriction = LazyLoot.Config.FindEnabledDutyRestriction(contentFinderInfo.RowId);
        if (dutyCustomRestriction is not { Enabled: true }) return null;
        {
            source = "Duty";
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

    private static unsafe RollResult GetPlayerRestrictByItemId(uint itemId, bool canNeed,
        IReadOnlyList<uint> fadedCopyOrchIds, List<string>? diagnostics = null)
    {
        var lootItem = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId);

        if (lootItem == null)
        {
            AddDiagnostic(diagnostics,
                $"Passing due to unknown item? Please give this ID to the developers: {itemId} [Unknown ID]",
                gated: false);
            return RollResult.Passed;
        }

        if (LazyLoot.Config.UnlockablesOnlyMode)
            return GetUnlockablesOnlyResult(itemId, lootItem, fadedCopyOrchIds, diagnostics);

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
        if (fadedCopyOrchIds.Count > 0 && fadedCopyOrchIds.All(IsItemUnlocked))
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

        if (IsItemUnlocked(itemId, fadedCopyOrchIds))
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
}
