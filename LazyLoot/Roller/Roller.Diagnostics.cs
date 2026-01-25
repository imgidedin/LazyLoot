using System.Collections.Generic;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace LazyLoot;

internal static partial class Roller
{
    private static void AddDiagnostic(List<string>? diagnostics, string message, bool gated = true)
    {
        if (!gated || LazyLoot.Config.DiagnosticsMode)
            DuoLog.Debug(message);
        diagnostics?.Add(message);
    }

    internal static LlDecisionTrace ExplainDecision(uint itemId, RollState rollState, uint? dutyId)
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

        var fadedCopyIds = GetFadedCopyOrchIds(itemId);
        var context = new RollContext(itemId, rollState, LootMode.Normal, dutyId, fadedCopyIds);
        trace.Final = ResolveRollOption(context, trace.BaseIntent, trace.Diagnostics,
            out var maxAllowed, out var customRule, out var customSource, out var playerRule);
        trace.MaxAllowed = maxAllowed;
        trace.CustomRule = customRule;
        trace.PlayerRule = playerRule;

        if (customRule == RollResult.UnAwarded)
            trace.Diagnostics.Add($"Ignored due to {customSource} custom restriction set to Do Nothing.");

        return trace;
    }
}
