using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace LazyLoot;

public class RestrictionGroup
{
    public List<CustomRestriction> Items { get; set; } = [];
    public List<CustomRestriction> Duties { get; set; } = [];
}

public class CustomRestriction
{
    public uint Id { get; init; }
    public bool Enabled { get; set; }
    public RollResult RollRule { get; set; }
}

public class RestrictionPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Default";
    public bool Enabled { get; set; } = true;
    public RestrictionGroup Restrictions { get; set; } = new();
}

public class Configuration : IPluginConfiguration
{
    private const int CurrentVersion = 3;

    //Diagnostics
    public bool DiagnosticsMode = false;

    // Output
    public bool EnableChatLogMessage = true;
    public bool EnableErrorToast = false;
    public bool EnableNormalToast = false;
    public bool EnableQuestToast = true;

    public bool FulfEnabled = false;
    public float FulfMaxRollDelayInSeconds = 3f;
    public float FulfMinRollDelayInSeconds = 1.5f;
    public bool UnlockablesOnlyMode = false;

    // FulfRollOption
    public int FulfRoll = 0;
    public float MaxRollDelayInSeconds = 1f;

    // RollDelay
    public float MinRollDelayInSeconds = 0.5f;

    // Never pass on glamour items (Items that has a level and iLvl of 1)
    public bool NeverPassGlam = true;

    public bool NoPassEmergency = false;

    // AllItems - Only Untradeables
    public bool RestrictionAllUnlockablesOnlyUntradeables = false;

    // Bardings - Only Untradeables
    public bool RestrictionBardingsOnlyUntradeables = false;

    // Emote/Hairstyle - Only Untradeables
    public bool RestrictionEmoteHairstyleOnlyUntradeables = false;

    // FadedCopy - Only Untradeables
    public bool RestrictionFadedCopyOnlyUntradeables = false;

    // Bardings
    public bool RestrictionIgnoreBardings = false;

    // Emote/Hairstyle
    public bool RestrictionIgnoreEmoteHairstyle = false;

    // FadedCopy
    public bool RestrictionIgnoreFadedCopy = false;

    // Restrictions
    // ILvl
    public bool RestrictionIgnoreItemLevelBelow = false;

    public int RestrictionIgnoreItemLevelBelowValue = 0;

    // AllItems
    public bool RestrictionIgnoreItemUnlocked = false;

    // Minions
    public bool RestrictionIgnoreMinions = false;

    // Mounts        
    public bool RestrictionIgnoreMounts = false;

    // OrchestrionRolls
    public bool RestrictionIgnoreOrchestrionRolls = false;

    // TripleTriadCards
    public bool RestrictionIgnoreTripleTriadCards = false;

    // Loot is an upgrade to the current job
    public bool RestrictionLootIsJobUpgrade = false;

    public int RestrictionLootIsJobUpgradeRollState = 1;

    // Loot is below a certain treshhold for the current job ilvl
    public bool RestrictionLootLowerThanJobIlvl = false;
    public int RestrictionLootLowerThanJobIlvlRollState = 1;

    public int RestrictionLootLowerThanJobIlvlTreshold = 30;

    // Minions - Only Untradeables
    public bool RestrictionMinionsOnlyUntradeables = false;

    // Mounts - Only Untradeables
    public bool RestrictionMountsOnlyUntradeables = false;

    // OrchestrionRolls - Only Untradeables
    public bool RestrictionOrchestrionRollsOnlyUntradeables = false;

    // Items that can't use with actual class
    public bool RestrictionOtherJobItems = false;

    // Loot by Seal Worth
    public bool RestrictionSeals = false;

    public int RestrictionSealsAmnt = 1;

    // TripleTriadCards - Only Untradeables
    public bool RestrictionTripleTriadCardsOnlyUntradeables = false;

    // Weekly lockout items
    public bool RestrictionWeeklyLockoutItems = false;
    public bool ShowDtrEntry = true;
    public bool WeeklyLockoutDutyActive = false;
    public ushort WeeklyLockoutDutyTerritoryId = 0;

    // Legacy single restriction group for migration stuff for older versions
    public RestrictionGroup Restrictions { get; set; } = new();

    public List<RestrictionPreset> RestrictionPresets { get; private set; } = [];
    public Guid ActiveRestrictionPresetId { get; set; } = Guid.Empty;

    public int Version { get; set; }

    public void Save()
    {
        Svc.PluginInterface.SavePluginConfig(this);
    }

    public bool MigrateIfNeeded()
    {
        var changed = false;
        RestrictionPresets ??= [];

        if (Version < CurrentVersion)
        {
            if (RestrictionPresets.Count == 0)
            {
                var preset = new RestrictionPreset
                {
                    Name = "Default",
                    Restrictions = Restrictions ?? new RestrictionGroup()
                };
                NormalizePreset(preset);
                RestrictionPresets.Add(preset);
                ActiveRestrictionPresetId = preset.Id;
            }

            if (Version < 3)
                foreach (var preset in RestrictionPresets)
                {
                    preset.Enabled = true;
                    NormalizePreset(preset);
                }

            Version = CurrentVersion;
            changed = true;
        }

        if (EnsureActiveRestrictionPreset()) changed = true;

        return changed;
    }

    public bool EnsureActiveRestrictionPreset()
    {
        RestrictionPresets ??= [];
        if (RestrictionPresets.Count == 0)
        {
            var preset = new RestrictionPreset
            {
                Name = "Default",
                Restrictions = new RestrictionGroup()
            };
            NormalizePreset(preset);
            RestrictionPresets.Add(preset);
            ActiveRestrictionPresetId = preset.Id;
            return true;
        }

        if (ActiveRestrictionPresetId == Guid.Empty ||
            RestrictionPresets.All(p => p.Id != ActiveRestrictionPresetId))
        {
            ActiveRestrictionPresetId = RestrictionPresets[0].Id;
            return true;
        }

        return false;
    }

    public RestrictionPreset GetActiveRestrictionPreset()
    {
        RestrictionPresets ??= [];
        if (RestrictionPresets.Count == 0)
        {
            var preset = new RestrictionPreset
            {
                Name = "Default",
                Restrictions = new RestrictionGroup()
            };
            NormalizePreset(preset);
            RestrictionPresets.Add(preset);
            ActiveRestrictionPresetId = preset.Id;
            return preset;
        }

        var presetMatch = RestrictionPresets.FirstOrDefault(p => p.Id == ActiveRestrictionPresetId);
        if (presetMatch != null)
        {
            NormalizePreset(presetMatch);
            return presetMatch;
        }

        ActiveRestrictionPresetId = RestrictionPresets[0].Id;
        NormalizePreset(RestrictionPresets[0]);
        return RestrictionPresets[0];
    }

    public RestrictionGroup GetActiveRestrictionGroup()
    {
        return GetActiveRestrictionPreset().Restrictions;
    }

    public CustomRestriction? FindEnabledItemRestriction(uint itemId)
    {
        RestrictionPresets ??= [];
        for (var i = RestrictionPresets.Count - 1; i >= 0; i--)
        {
            var preset = RestrictionPresets[i];
            if (!preset.Enabled)
                continue;

            var items = preset.Restrictions?.Items;
            if (items == null)
                continue;

            var restriction = items.FirstOrDefault(x => x.Id == itemId && x.Enabled);
            if (restriction != null)
                return restriction;
        }

        return null;
    }

    public CustomRestriction? FindEnabledDutyRestriction(uint dutyId)
    {
        RestrictionPresets ??= [];
        for (var i = RestrictionPresets.Count - 1; i >= 0; i--)
        {
            var preset = RestrictionPresets[i];
            if (!preset.Enabled)
                continue;

            var duties = preset.Restrictions?.Duties;
            if (duties == null)
                continue;

            var restriction = duties.FirstOrDefault(x => x.Id == dutyId && x.Enabled);
            if (restriction != null)
                return restriction;
        }

        return null;
    }

    private static void NormalizePreset(RestrictionPreset preset)
    {
        preset.Restrictions ??= new RestrictionGroup();
        preset.Restrictions.Items ??= [];
        preset.Restrictions.Duties ??= [];
    }
}
