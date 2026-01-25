using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace LazyLoot;

internal static partial class Roller
{
    private static RollItemRaw _rollItemRaw;

    private static uint _itemId, _index;

    public static void Clear()
    {
        _itemId = _index = 0;
    }

    private readonly struct RollContext(uint itemId, RollState rollState, LootMode lootMode, uint? dutyId,
        List<uint> fadedCopyOrchIds)
    {
        public uint ItemId { get; } = itemId;
        public RollState RollState { get; } = rollState;
        public LootMode LootMode { get; } = lootMode;
        public uint? DutyId { get; } = dutyId;
        public bool CanNeed => RollState == RollState.UpToNeed;
        public IReadOnlyList<uint> FadedCopyOrchIds { get; } = fadedCopyOrchIds;
    }

    internal sealed class LlDecisionTrace
    {
        public RollResult BaseIntent { get; init; }
        public RollResult MaxAllowed { get; set; }
        public RollResult? CustomRule { get; set; }
        public RollResult PlayerRule { get; set; }
        public RollResult Final { get; set; }
        public List<string> Diagnostics { get; } = [];
    }
}
