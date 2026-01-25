using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace LazyLoot.Integrations.BisBuddy;

public static class BisBuddy
{
    internal static List<BisBuddyGearset> GetPlayerGearsets(ulong playerId)
    {
        var gearsetsFile = GetGearsetsFile(playerId);
        if (gearsetsFile == null || !File.Exists(gearsetsFile))
            return [];

        return TryLoadGearsets(gearsetsFile, out var gearsets, out _)
            ? gearsets
            : [];
    }

    public static List<uint> GetGearsetItems(Guid gearsetId, bool includeRequirements)
        => GetGearsetItems(Svc.PlayerState.ContentId, gearsetId, includeRequirements);

    internal static List<uint> GetGearsetItems(ulong playerId, Guid gearsetId, bool includeRequirements)
    {
        var gearsets = GetPlayerGearsets(playerId);
        var gearset = gearsets.FirstOrDefault(x => x.Id == gearsetId);
        if (gearset?.Gearpieces == null)
            return [];

        var items = new List<uint>();
        var seen = new HashSet<uint>();

        foreach (var piece in gearset.Gearpieces)
        {
            AddItem(piece.ItemId, seen, items);

            if (includeRequirements && piece.PrerequisiteTree != null)
                CollectRequirementItems(piece.PrerequisiteTree, seen, items);
        }

        return items;
    }

    internal static List<Item> GetGearsetItemRows(Guid gearsetId, bool includeRequirements)
        => GetGearsetItemRows(Svc.PlayerState.ContentId, gearsetId, includeRequirements);

    internal static List<Item> GetGearsetItemRows(ulong playerId, Guid gearsetId, bool includeRequirements)
    {
        var itemSheet = Svc.Data.GetExcelSheet<Item>();

        var itemIds = GetGearsetItems(playerId, gearsetId, includeRequirements);
        if (itemIds.Count == 0)
            return [];

        var items = new List<Item>(itemIds.Count);
        items.AddRange(itemIds.Select(itemId => itemSheet.GetRowOrDefault(itemId)).Where(item => item?.RowId != 0).Select(item => (Item)item!));

        return items;
    }

    public static void ShowDebugInformation()
    {
        ImGui.TextWrapped("Checking BiSBuddy...");

        var contentId = Svc.PlayerState.ContentId;
        var gearsetsDir = GetGearsetsDirectory();
        var gearsetsFile = gearsetsDir == null ? null : Path.Combine(gearsetsDir, $"{contentId}.json");

        ImGui.TextUnformatted($"PluginConfigs root: {GetPluginConfigsRoot() ?? "(unknown)"}");
        ImGui.TextUnformatted($"BisBuddy gearsets dir: {gearsetsDir ?? "(unknown)"}");
        ImGui.TextUnformatted($"ContentId: {contentId}");
        ImGui.TextUnformatted($"BisBuddy gearsets file: {gearsetsFile ?? "(unknown)"}");
        if (gearsetsDir != null)
            ImGui.TextUnformatted($"Directory exists: {Directory.Exists(gearsetsDir)}");
        if (gearsetsFile != null)
            ImGui.TextUnformatted($"File exists: {File.Exists(gearsetsFile)}");

        if (gearsetsFile != null && File.Exists(gearsetsFile))
        {
            if (TryLoadGearsets(gearsetsFile, out var gearsets, out var error))
            {
                ImGui.Separator();
                ImGui.TextUnformatted($"Loaded gearsets: {gearsets.Count}");

                foreach (var set in from set in gearsets let header = $"{set.Name ?? "(unnamed)"} (Job {set.ClassJobId}, Active {set.IsActive})" where ImGui.CollapsingHeader(header) select set)
                {
                    if (set.Gearpieces == null || set.Gearpieces.Count == 0)
                    {
                        ImGui.TextUnformatted("No gearpieces found.");
                        continue;
                    }

                    foreach (var piece in set.Gearpieces)
                    {
                        ImGui.TextUnformatted($"ItemId: {piece.ItemId} Collected: {piece.IsCollected}");
                        var reqs = GetRequirementItems(piece.PrerequisiteTree);
                        if (reqs.Count > 0)
                            ImGui.TextUnformatted($"Requirements: {string.Join(", ", reqs)}");
                    }

                    var uniqueItems = GetGearsetItemRows(contentId, set.Id, includeRequirements: true);
                    if (uniqueItems.Count <= 0) continue;
                    ImGui.Separator();
                    ImGui.TextUnformatted("Unique gearset items:");
                    foreach (var item in uniqueItems)
                    {
                        var icon = GetItemIcon(item.Icon);
                        if (icon != null)
                        {
                            ImGui.Image(icon.Handle, new Vector2(16, 16));
                            ImGui.SameLine();
                        }

                        ImGui.TextUnformatted($"{item.Name.ExtractText()} (Id {item.RowId})");
                    }
                }
            }
            else
            {
                ImGui.Separator();
                ImGui.TextUnformatted($"Failed to read BisBuddy gearsets: {error}");
            }
        }

        ImGui.Separator();
    }

    private static bool TryLoadGearsets(string path, out List<BisBuddyGearset> gearsets, out string error)
    {
        gearsets = [];
        error = string.Empty;

        try
        {
            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            gearsets = JsonSerializer.Deserialize<List<BisBuddyGearset>>(json, options) ?? [];
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string? GetGearsetsFile(ulong playerId)
    {
        var dir = GetGearsetsDirectory();
        return dir == null ? null : Path.Combine(dir, $"{playerId}.json");
    }

    private static string? GetGearsetsDirectory()
    {
        var root = GetPluginConfigsRoot();
        return root == null ? null : Path.Combine(root, "BisBuddy", "gearsets");
    }

    private static string? GetPluginConfigsRoot()
    {
        var currentPluginConfig = Svc.PluginInterface.GetPluginConfigDirectory();
        return Path.GetDirectoryName(
            currentPluginConfig.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private static void AddItem(uint itemId, HashSet<uint> seen, List<uint> items)
    {
        if (itemId == 0 || !seen.Add(itemId))
            return;

        items.Add(itemId);
    }

    private static List<uint> GetRequirementItems(BisBuddyPrerequisiteNode? node)
    {
        var items = new List<uint>();
        if (node == null)
            return items;

        var seen = new HashSet<uint>();
        CollectRequirementItems(node, seen, items);
        return items;
    }

    private static void CollectRequirementItems(
        BisBuddyPrerequisiteNode node,
        HashSet<uint> seen,
        List<uint> items)
    {
        AddItem(node.ItemId, seen, items);

        if (node.PrerequisiteTree == null)
            return;

        foreach (var child in node.PrerequisiteTree)
            CollectRequirementItems(child, seen, items);
    }

    private static IDalamudTextureWrap? GetItemIcon(uint id)
    {
        return Svc.Texture.GetFromGameIcon(new GameIconLookup
        {
            IconId = id
        }).GetWrapOrDefault();
    }

    public sealed class BisBuddyGearset
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public int ClassJobId { get; set; }
        public bool IsActive { get; set; }
        public List<BisBuddyGearpiece>? Gearpieces { get; set; }
    }

    public sealed class BisBuddyGearpiece
    {
        public uint ItemId { get; set; }
        public bool IsCollected { get; set; }
        public List<BisBuddyMateria>? ItemMateria { get; set; }
        public BisBuddyPrerequisiteNode? PrerequisiteTree { get; set; }
    }

    public sealed class BisBuddyMateria
    {
        public uint ItemId { get; set; }
        public bool IsCollected { get; set; }
    }

    public sealed class BisBuddyPrerequisiteNode
    {
        [JsonPropertyName("$type")]
        public string? Type { get; set; }
        public uint ItemId { get; set; }
        public List<BisBuddyPrerequisiteNode>? PrerequisiteTree { get; set; }
    }
}
