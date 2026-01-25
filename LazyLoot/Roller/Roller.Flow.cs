using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ECommons.DalamudServices;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace LazyLoot;

internal static partial class Roller
{
    public static bool RollOneItem(RollResult option, ref int need, ref int greed, ref int pass)
    {
        uint startIndex = 0;
        while (GetNextLootItem(startIndex, out var index, out var loot))
        {
            startIndex = index + 1;
            var context = CreateContext(loot);
            var customRule = GetCustomRuleForContext(context, out var customSource);

            if (ShouldSkipLootItem(context, loot, customRule, customSource))
                continue;

            // Flow: resolve constraints -> apply overrides -> handle emergency pass -> execute roll -> update counters.
            var resolved = ResolveRollOption(context, option, customRule, null,
                out var maxAllowed, out var playerRule);
            var customLabel = customRule?.ToString() ?? "None";
            DuoLog.Debug(
                $"Rolling {loot.ItemId} with {option} and {maxAllowed} restrictions (Custom: {customLabel} via {customSource}, Player: {playerRule}).");

            option = ApplyEmergencyPassIfNeeded(loot, index, resolved, ref need, ref greed, ref pass);

            RollItem(option, index);
            _itemId = loot.ItemId;
            _index = index;

            IncrementRollCounters(option, ref need, ref greed, ref pass);

            return true;
        }

        return false;
    }

    private static RollResult ResolveRollOption(RollContext context, RollResult baseOption, List<string>? diagnostics,
        out RollResult maxAllowed, out RollResult? customRule, out string customSource, out RollResult playerRule)
    {
        customRule = GetCustomRuleForContext(context, out customSource);
        return ResolveRollOption(context, baseOption, customRule, diagnostics, out maxAllowed, out playerRule);
    }

    private static RollResult ResolveRollOption(RollContext context, RollResult baseOption, RollResult? customRule,
        List<string>? diagnostics, out RollResult maxAllowed, out RollResult playerRule)
    {
        maxAllowed = GetRestrictResult(context);
        if (customRule == RollResult.UnAwarded)
        {
            playerRule = RollResult.UnAwarded;
            return RollResult.UnAwarded;
        }

        playerRule = customRule == null
            ? GetPlayerRestrictByItemId(context.ItemId, context.CanNeed, context.FadedCopyOrchIds, diagnostics)
            : RollResult.UnAwarded;

        return ResolveFinalRoll(baseOption, maxAllowed, customRule, playerRule);
    }

    private static unsafe RollContext CreateContext(LootItem loot)
    {
        var dutyId = GameMain.Instance()->CurrentContentFinderConditionId;
        var fadedCopyIds = GetFadedCopyOrchIds(loot.ItemId);
        return new RollContext(loot.ItemId, loot.RollState, loot.LootMode, dutyId == 0 ? null : dutyId, fadedCopyIds);
    }

    private static RollResult ApplyEmergencyPassIfNeeded(LootItem loot, uint index, RollResult option,
        ref int need, ref int greed, ref int pass)
    {
        if (_itemId != loot.ItemId || index != _index)
            return option;

        if (LazyLoot.Config.DiagnosticsMode && !LazyLoot.Config.NoPassEmergency)
            DuoLog.Debug(
                $"{Svc.Data.GetExcelSheet<Item>().GetRow(loot.ItemId).Name.ToString()} has failed to roll for some reason. Passing for safety. [Emergency pass]");

        if (!LazyLoot.Config.NoPassEmergency)
        {
            DecrementRollCounters(option, ref need, ref greed, ref pass);
            return RollResult.Passed;
        }

        return option;
    }

    private static void IncrementRollCounters(RollResult option, ref int need, ref int greed, ref int pass)
    {
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
    }

    private static void DecrementRollCounters(RollResult option, ref int need, ref int greed, ref int pass)
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

    private static unsafe bool GetNextLootItem(uint startIndex, out uint i, out LootItem loot)
    {
        var span = Loot.Instance()->Items;
        for (i = startIndex; i < span.Length; i++)
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

            return true;
        }

        loot = default;
        return false;
    }

    private static bool ShouldSkipLootItem(RollContext context, LootItem loot, RollResult? customRule,
        string customSource)
    {
        if (customRule == RollResult.UnAwarded)
        {
            LogCustomSkip(context, customSource);
            return true;
        }

        if (!LazyLoot.Config.RestrictionWeeklyLockoutItems || LazyLoot.Config.UnlockablesOnlyMode)
            return false;

        var ignoreWeekly = customSource == "Item" && customRule.HasValue;
        if (ignoreWeekly)
            return false;

        // loot.RollValue == 20 means it can't be rolled because one was already obtained this week.
        // we ignore that so it will be passed automatically, as there is nothing the user can do other than
        // pass it
        return loot.WeeklyLootItem && (byte)loot.RollState != 20;
    }

    private static void LogCustomSkip(RollContext context, string customSource)
    {
        if (!LazyLoot.Config.DiagnosticsMode)
            return;

        var item = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(context.ItemId);
        var itemName = item?.Name.ToString() ?? context.ItemId.ToString();

        if (customSource == "Duty" && context.DutyId.HasValue)
        {
            var duty = Svc.Data.GetExcelSheet<ContentFinderCondition>().GetRowOrDefault(context.DutyId.Value);
            var dutyName = duty?.Name.ToString() ?? context.DutyId.Value.ToString();
            DuoLog.Debug($"{itemName} is being ignored due to being in {dutyName}. [Duty Custom Restriction]");
            return;
        }

        DuoLog.Debug($"{itemName} is being ignored. [Item Custom Restriction]");
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

    private unsafe delegate bool RollItemRaw(Loot* lootIntPtr, RollResult option, uint lootItemIndex);
}
