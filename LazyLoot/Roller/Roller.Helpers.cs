using System;
using System.Linq;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace LazyLoot;

internal static partial class Roller
{
    private static RollResult ResultMerge(params RollResult[] results)
    {
        return results.Max() switch
        {
            RollResult.Needed => RollResult.Needed,
            RollResult.Greeded => RollResult.Greeded,
            _ => RollResult.Passed
        };
    }

    private static RollResult MoreRestrictive(RollResult left, RollResult right)
    {
        return (RollResult)Math.Max((int)left, (int)right);
    }


    private static unsafe int ItemCount(uint itemId)
    {
        return InventoryManager.Instance()->GetInventoryItemCount(itemId);
    }

    public static uint ConvertSealsToIlvl(int sealAmnt)
    {
        var sealsSheet = Svc.Data.GetExcelSheet<GCSupplyDutyReward>();
        uint ilvl = 0;
        foreach (var row in sealsSheet.Where(row => row.SealsExpertDelivery < sealAmnt))
            ilvl = row.RowId;

        return ilvl;
    }
}