using System.Collections.Generic;
using System.Linq;
using ECommons.DalamudServices;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace LazyLoot;

internal static partial class Roller
{
    private static bool ShouldPassUnlockable(bool restriction, bool onlyUntradeable, Item? item)
    {
        return restriction && (!onlyUntradeable || (onlyUntradeable && item!.Value.IsUntradable));
    }

    internal static bool IsUnlockableItem(Item item)
    {
        UpdateFadedCopy(item.RowId, out var orchId);
        return orchId.Count > 0 || LazyLoot.UnlockState.IsItemUnlockable(item);
    }

    private static bool IsUnlockableItem(Item item, IReadOnlyList<uint> fadedCopyOrchIds)
    {
        return fadedCopyOrchIds.Count > 0 || LazyLoot.UnlockState.IsItemUnlockable(item);
    }

    private static RollResult GetUnlockablesOnlyResult(uint itemId, Item? lootItem,
        IReadOnlyList<uint> fadedCopyOrchIds, List<string>? diagnostics = null)
    {
        if (lootItem == null)
            return RollResult.Passed;

        if (!IsUnlockableItem(lootItem.Value, fadedCopyOrchIds))
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

    private static void UpdateFadedCopy(uint itemId, out List<uint> orchId, bool log = true)
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

    public static bool IsItemUnlocked(uint itemId)
    {
        UpdateFadedCopy(itemId, out var orchId, false);
        if (orchId.Count > 0) return orchId.All(IsItemUnlocked);
        var item = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId);
        return item != null && LazyLoot.UnlockState.IsItemUnlocked(item.Value);
    }

    private static bool IsItemUnlocked(uint itemId, IReadOnlyList<uint> fadedCopyOrchIds)
    {
        if (fadedCopyOrchIds.Count > 0) return fadedCopyOrchIds.All(IsItemUnlocked);
        var item = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId);
        return item != null && LazyLoot.UnlockState.IsItemUnlocked(item.Value);
    }

    private static List<uint> GetFadedCopyOrchIds(uint itemId, bool log = true)
    {
        UpdateFadedCopy(itemId, out var orchId, log);
        return orchId;
    }
}
