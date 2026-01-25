using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using PunishLib;

namespace LazyLoot;

public class LazyLoot : IDalamudPlugin, IDisposable
{
    private const uint CastYourLotMessage = 5194;
    private const uint WeeklyLockoutMessage = 4234;
    private const string LazyTestUsage =
        "Usage: /lazytest item:[9999|\"item name\"] duty:[9999|\"duty name\"] upto:[need|greed|pass]";

    private static readonly RollResult[] RollArray =
    [
        RollResult.Needed,
        RollResult.Greeded,
        RollResult.Passed
    ];

    internal static Configuration Config;
    private static ConfigUi _configUi;
    private static IDtrBarEntry _dtrEntry;

    private static DateTime _nextRollTime = DateTime.Now;
    private static RollResult _rollOption = RollResult.UnAwarded;
    private static int _need, _greed, _pass;

    public LazyLoot(IDalamudPluginInterface pluginInterface)
    {
        ECommonsMain.Init(pluginInterface, this, Module.All);
        PunishLibMain.Init(pluginInterface, "LazyLoot",
            new AboutPlugin { Developer = "Gid, Taurenkey and NightmareXIV", Sponsor = "https://ko-fi.com/gidedin" });

        Config = Svc.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (Config.MigrateIfNeeded())
            Config.Save();
        _configUi = new ConfigUi();
        _dtrEntry = Svc.DtrBar.Get("LazyLoot");
        _dtrEntry.OnClick = OnDtrClick;


        Svc.PluginInterface.UiBuilder.OpenMainUi += OnOpenConfigUi;
        Svc.PluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;
        Svc.Chat.CheckMessageHandled += NoticeLoot;
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
        SyncWeeklyLockoutDutyState(Svc.ClientState.TerritoryType);

        Svc.Commands.AddHandler("/lazyloot", new CommandInfo(LazyCommand)
        {
            HelpMessage = "Open Lazy Loot config.",
            ShowInHelp = true
        });

        Svc.Commands.AddHandler("/lazy", new CommandInfo(LazyCommand)
        {
            HelpMessage = "Open Lazy Loot config by default. Add need | greed | pass to roll on current items.",
            ShowInHelp = true
        });

        Svc.Commands.AddHandler("/lazytest", new CommandInfo(LazyTestCommand)
        {
            HelpMessage = "Preview what LazyLoot would do for a specific item and duty.",
            ShowInHelp = true
        });

        Svc.Commands.AddHandler("/fulf", new CommandInfo(FulfCommand)
        {
            HelpMessage =
                "Enable/Disable FULF with /fulf [on|off] or change the loot rule with /fulf need | greed | pass.",
            ShowInHelp = true
        });

        Svc.Framework.Update += OnFrameworkUpdate;
    }

    [PluginService] public static IUnlockState UnlockState { get; set; } = null!;

    public string Name => "LazyLoot";

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private static void OnDtrClick(DtrInteractionEvent ev)
    {
        if (ev.ModifierKeys.HasFlag(ClickModifierKeys.Ctrl))
        {
            _configUi.IsOpen = true;
            return;
        }

        switch (ev.ClickType)
        {
            case MouseClickType.Left:
                CycleFulf(true);
                break;

            case MouseClickType.Right:
                CycleFulf(false);
                break;
        }
    }

    private void LazyCommand(string command, string arguments)
    {
        var args = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length == 0)
        {
            OnOpenConfigUi();
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "test":
                LazyTestCommand("/lazytest", string.Join(" ", args.Skip(1)));
                return;
            default:
                RollingCommand(arguments);
                return;
        }
    }

    private static void CycleFulf(bool forward)
    {
        if (!Config.FulfEnabled)
        {
            Config.FulfEnabled = true;
            Config.FulfRoll = forward ? 2 : 0;
            Config.Save();
            return;
        }

        if (forward)
            switch (Config.FulfRoll)
            {
                case 2:
                    Config.FulfRoll = 1;
                    break;
                case 1:
                    Config.FulfRoll = 0;
                    break;
                default:
                    Config.FulfEnabled = false;
                    break;
            }
        else
            switch (Config.FulfRoll)
            {
                case 0:
                    Config.FulfRoll = 1;
                    break;
                case 1:
                    Config.FulfRoll = 2;
                    break;
                default:
                    Config.FulfEnabled = false;
                    break;
            }

        Config.Save();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        _configUi.Dispose();

        Svc.PluginInterface.UiBuilder.OpenMainUi -= OnOpenConfigUi;
        Svc.PluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi;
        Svc.Chat.CheckMessageHandled -= NoticeLoot;
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;

        Svc.Commands.RemoveHandler("/lazyloot");
        Svc.Commands.RemoveHandler("/lazy");
        Svc.Commands.RemoveHandler("/lazytest");
        Svc.Commands.RemoveHandler("/fulf");

        ECommonsMain.Dispose();
        PunishLibMain.Dispose();
        Svc.Log.Information(">>Stop LazyLoot<<");
        _dtrEntry.Remove();

        Svc.Framework.Update -= OnFrameworkUpdate;
        Config.Save();
    }

    private static void FulfCommand(string command, string arguments)
    {
        var res = GetResult(arguments);
        if (res.HasValue)
            Config.FulfRoll = res.Value;
        else if (arguments.Contains("off", StringComparison.CurrentCultureIgnoreCase))
            Config.FulfEnabled = false;
        else if (arguments.Contains("on", StringComparison.CurrentCultureIgnoreCase))
            Config.FulfEnabled = true;
        else
            Config.FulfEnabled = !Config.FulfEnabled;
    }

    private static void RollingCommand(string arguments)
    {
        var res = GetResult(arguments);
        if (res.HasValue) _rollOption = RollArray[res.Value % 3];
    }

    private static int? GetResult(string str)
    {
        if (str.Contains("need", StringComparison.OrdinalIgnoreCase)) return 0;

        if (str.Contains("greed", StringComparison.OrdinalIgnoreCase)) return 1;

        if (str.Contains("pass", StringComparison.OrdinalIgnoreCase)) return 2;

        return null;
    }

    private static void OnOpenConfigUi()
    {
        _configUi.Toggle();
    }

    private static void OnFrameworkUpdate(IFramework framework)
    {
        string dtrText;
        if (Config.FulfEnabled)
            dtrText = Config.FulfRoll switch
            {
                0 => "Needing",
                1 => "Greeding",
                2 => "Passing",
                _ => throw new ArgumentOutOfRangeException(nameof(Config.FulfRoll))
            };
        else
            dtrText = "FULF Disabled";

        if (Config.UnlockablesOnlyMode)
            dtrText += " | Unlockables Only Mode";

        var isWeeklyLockedDutyActive = Config is { RestrictionWeeklyLockoutItems: true, WeeklyLockoutDutyActive: true };

        if (isWeeklyLockedDutyActive) dtrText += " (Disabled | WLD)";

        _dtrEntry.Text = new SeString(
            new IconPayload(isWeeklyLockedDutyActive ? BitmapFontIcon.NoCircle : BitmapFontIcon.Dice),
            new TextPayload(dtrText));

        _dtrEntry.Shown = Config.ShowDtrEntry;
        
        if (isWeeklyLockedDutyActive) return;

        RollLoot();
    }

    private static void RollLoot()
    {
        if (_rollOption == RollResult.UnAwarded) return;
        if (DateTime.Now < _nextRollTime) return;

        //No rolling in cutscene.
        if (Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent]) return;

        _nextRollTime = DateTime.Now.AddMilliseconds(Math.Max(1500, new Random()
            .Next((int)(Config.MinRollDelayInSeconds * 1000),
                (int)(Config.MaxRollDelayInSeconds * 1000))));

        try
        {
            if (Roller.RollOneItem(_rollOption, ref _need, ref _greed, ref _pass)) return; //Finish the loot
            ShowResult(_need, _greed, _pass);
            _need = _greed = _pass = 0;
            _rollOption = RollResult.UnAwarded;
            Roller.Clear();
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "Something Wrong with rolling!");
        }
    }

    private static void ShowResult(int need, int greed, int pass)
    {
        SeString seString = new(new List<Payload>
        {
            new TextPayload("Need "),
            new UIForegroundPayload(575),
            new TextPayload(need.ToString()),
            new UIForegroundPayload(0),
            new TextPayload(" item" + (need == 1 ? "" : "s") + ", greed "),
            new UIForegroundPayload(575),
            new TextPayload(greed.ToString()),
            new UIForegroundPayload(0),
            new TextPayload(" item" + (greed == 1 ? "" : "s") + ", pass "),
            new UIForegroundPayload(575),
            new TextPayload(pass.ToString()),
            new UIForegroundPayload(0),
            new TextPayload(" item" + (pass == 1 ? "" : "s") + ".")
        });

        if (Config.EnableChatLogMessage) Svc.Chat.Print(seString);

        if (Config.EnableErrorToast) Svc.Toasts.ShowError(seString);

        if (Config.EnableNormalToast) Svc.Toasts.ShowNormal(seString);

        if (Config.EnableQuestToast) Svc.Toasts.ShowQuest(seString);
    }

    private void NoticeLoot(XivChatType type, int senderId, ref SeString sender, ref SeString message,
        ref bool isHandled)
    {
        if (!Config.FulfEnabled || type != (XivChatType)2105) return;
        // do a few checks to see if the message is the weekly lockout message the game sends
        if (CheckAndUpdateWeeklyLockoutDutyFlag(message)) return;
        // if not Cast your lot, then just ignore
        if (message.TextValue !=
            Svc.Data.GetExcelSheet<LogMessage>().First(x => x.RowId == CastYourLotMessage).Text) return;
        _nextRollTime = DateTime.Now.AddMilliseconds(new Random()
            .Next((int)(Config.FulfMinRollDelayInSeconds * 1000),
                (int)(Config.FulfMaxRollDelayInSeconds * 1000)));
        _rollOption = RollArray[Config.FulfRoll];
    }

    private static bool CheckAndUpdateWeeklyLockoutDutyFlag(SeString message)
    {
        if (!Config.RestrictionWeeklyLockoutItems || Config.WeeklyLockoutDutyActive)
            return false;

        if (!IsHighEndDutyTerritory(Svc.ClientState.TerritoryType))
        {
            ClearWeeklyLockoutDutyState();
            return false;
        }

        var weeklyLockoutMessage = Svc.Data.GetExcelSheet<LogMessage>().GetRowOrDefault(WeeklyLockoutMessage);
        if (weeklyLockoutMessage == null)
            return false;

        if (message.TextValue != weeklyLockoutMessage.Value.Text) return false;

        Config.WeeklyLockoutDutyActive = true;
        Config.WeeklyLockoutDutyTerritoryId = Svc.ClientState.TerritoryType;
        Config.Save();
        DuoLog.Debug("Weekly lockout duty detected! Rolling is temporarily suspended.");

        return true;
    }

    private static void OnTerritoryChanged(ushort territoryId)
    {
        if (!IsHighEndDutyTerritory(territoryId))
        {
            ClearWeeklyLockoutDutyState();
            return;
        }

        if (!Config.WeeklyLockoutDutyActive)
            return;

        if (Config.WeeklyLockoutDutyTerritoryId == territoryId)
            return;

        ClearWeeklyLockoutDutyState();
    }

    private static void SyncWeeklyLockoutDutyState(ushort territoryId)
    {
        if (!Config.WeeklyLockoutDutyActive)
            return;

        if (!IsHighEndDutyTerritory(territoryId) || Config.WeeklyLockoutDutyTerritoryId != territoryId)
            ClearWeeklyLockoutDutyState();
    }

    private static void ClearWeeklyLockoutDutyState()
    {
        if (Config is { WeeklyLockoutDutyActive: false, WeeklyLockoutDutyTerritoryId: 0 })
            return;

        Config.WeeklyLockoutDutyActive = false;
        Config.WeeklyLockoutDutyTerritoryId = 0;
        Config.Save();
        DuoLog.Debug("Weekly lockout duty suspension cleared.");
    }

    private static bool IsHighEndDutyTerritory(ushort territoryId)
    {
        var territory = Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId);
        var contentFinder = territory?.ContentFinderCondition.Value;
        return contentFinder is { HighEndDuty: true };
    }

    private static void LazyTestCommand(string command, string arguments)
    {
        if (!TryParseLazyTestArgs(arguments, out var request, out var error))
        {
            DuoLog.Debug(error);
            return;
        }

        if (!TryResolveItem(request.ItemQuery, out var item))
            return;

        ContentFinderCondition? duty = null;
        if (!string.IsNullOrWhiteSpace(request.DutyQuery))
        {
            duty = ResolveDuty(request.DutyQuery);
            if (duty == null)
                return;
        }

        RunLazyTest(item, duty, request.UpTo);
    }

    internal static LazyTestResult RunLazyTest(Item item, ContentFinderCondition? duty, RollState upTo)
    {
        var trace = Roller.ExplainDecision(item.RowId, upTo, duty?.RowId);

        var decisionText = trace.Final switch
        {
            RollResult.UnAwarded => "DO NOTHING",
            RollResult.Passed => "PASS",
            RollResult.Greeded => "GREED",
            RollResult.Needed => "NEED",
            _ => $"UNKNOWN ({trace.Final})"
        };
        ushort decisionColor = trace.Final switch
        {
            RollResult.UnAwarded => 8, // Grey
            RollResult.Passed => 14, // Red
            RollResult.Greeded => 500, // Yellow
            RollResult.Needed => 45, // Green
            _ => 0
        };

        Svc.Chat.Print(new SeString(new List<Payload>
            {
                new TextPayload($"[LazyLoot Item Test] :: ID {item.RowId} :: "),
                new UIForegroundPayload((ushort)(0x223 + item.Rarity * 2)),
                new UIGlowPayload((ushort)(0x224 + item.Rarity * 2)),
                new ItemPayload(item.RowId, true),
                new TextPayload(item.Name.ExtractText()),
                RawPayload.LinkTerminator,
                new UIForegroundPayload(0),
                new UIGlowPayload(0),
                new TextPayload(" :: "),
                new UIForegroundPayload(decisionColor),
                new TextPayload($"{decisionText}"),
                new UIForegroundPayload(0)
            })
        );

        var customText = trace.CustomRule.HasValue ? trace.CustomRule.Value.ToString() : "None";
        var dutyText = duty != null ? $" | Duty {duty.Value.Name.ToString()} ({duty.Value.RowId})" : string.Empty;
        var summary =
            $"[LazyLoot Item Test] :: Trace :: FULF {trace.BaseIntent} | UpTo {upTo} | Max {trace.MaxAllowed} | Custom {customText} | Player {trace.PlayerRule}{dutyText} | Final {trace.Final}";
        Svc.Chat.Print(summary);

        if (trace.Diagnostics.Count == 0)
        {
            Svc.Chat.Print("[LazyLoot Item Test] :: Reason :: None (FULF/base choice or max constraint only)");
        }
        else
        {
            foreach (var reason in trace.Diagnostics)
                Svc.Chat.Print($"[LazyLoot Item Test] :: Reason :: {reason}");
        }

        var dutyName = duty != null ? duty.Value.Name.ToString() : "None";
        return new LazyTestResult(DateTime.Now, item.RowId, item.Name.ExtractText(), duty?.RowId, dutyName, upTo,
            trace.Final);
    }

    private static bool TryParseLazyTestArgs(string arguments, out LazyTestRequest request, out string error)
    {
        request = default;
        error = LazyTestUsage;

        if (string.IsNullOrWhiteSpace(arguments))
            return false;

        if (!TryParseBracketArguments(arguments, out var args, out error))
            return false;

        var unknownKeys = args.Keys
            .Where(key => !key.Equals("item", StringComparison.OrdinalIgnoreCase)
                          && !key.Equals("duty", StringComparison.OrdinalIgnoreCase)
                          && !key.Equals("upto", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (unknownKeys.Count > 0)
        {
            error = $"Unknown argument(s): {string.Join(", ", unknownKeys)}. {LazyTestUsage}";
            return false;
        }

        if (!args.TryGetValue("item", out var itemQuery) || string.IsNullOrWhiteSpace(itemQuery))
        {
            error = $"Missing item argument. {LazyTestUsage}";
            return false;
        }

        string? dutyQuery = null;
        if (args.TryGetValue("duty", out var dutyValue))
        {
            if (string.IsNullOrWhiteSpace(dutyValue))
            {
                error = $"Duty value cannot be empty. {LazyTestUsage}";
                return false;
            }

            dutyQuery = dutyValue;
        }

        var upTo = RollState.UpToNeed;
        if (args.TryGetValue("upto", out var upToValue))
        {
            if (string.IsNullOrWhiteSpace(upToValue))
            {
                error = $"UpTo value cannot be empty. {LazyTestUsage}";
                return false;
            }

            if (!TryParseUpToToken(upToValue, out upTo))
            {
                error = $"Invalid UpTo value: '{upToValue}'. Use need|greed|pass.";
                return false;
            }
        }

        request = new LazyTestRequest(itemQuery, dutyQuery, upTo);
        return true;
    }

    private static bool TryResolveItem(string itemQuery, out Item item)
    {
        item = default;
        var itemSheet = Svc.Data.GetExcelSheet<Item>();

        if (uint.TryParse(itemQuery, out var itemId))
        {
            var itemRow = itemSheet.GetRowOrDefault(itemId);
            if (itemRow == null || itemRow.Value.RowId == 0)
            {
                DuoLog.Debug($"No item found matching id '{itemId}'.");
                return false;
            }

            item = itemRow.Value;
            return true;
        }

        var search = itemQuery.Trim();
        var matches = itemSheet
            .Where(x => x.RowId > 0 && !x.Name.IsEmpty &&
                        x.Name.ToString().Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();

        switch (matches.Count)
        {
            case 0:
                DuoLog.Debug($"No item found matching your search '{search}'.");
                return false;
            case > 1:
            {
                Svc.Chat.Print(new SeString(new List<Payload>
                {
                    new TextPayload(
                        $"Found {matches.Count} entries for search '{search}'. Showing the first 5:")
                }));

                foreach (var match in matches.Take(5))
                    Svc.Chat.Print(new SeString(new List<Payload>
                        {
                            new TextPayload($"[LazyLoot Item Test] :: ID {match.RowId} :: "),
                            new UIForegroundPayload((ushort)(0x223 + match.Rarity * 2)),
                            new UIGlowPayload((ushort)(0x224 + match.Rarity * 2)),
                            new ItemPayload(match.RowId, false),
                            new TextPayload(match.Name.ExtractText()),
                            RawPayload.LinkTerminator,
                            new UIForegroundPayload(0),
                            new UIGlowPayload(0)
                        })
                    );

                return false;
            }
            default:
                item = matches[0];
                return true;
        }
    }

    private static bool TryParseBracketArguments(string input, out Dictionary<string, string> args, out string error)
    {
        args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        error = string.Empty;

        var i = 0;
        while (i < input.Length)
        {
            while (i < input.Length && char.IsWhiteSpace(input[i]))
                i++;

            if (i >= input.Length)
                break;

            var keyStart = i;
            while (i < input.Length && input[i] != ':' && !char.IsWhiteSpace(input[i]))
                i++;

            if (i >= input.Length || input[i] != ':')
            {
                error = $"Expected key:[value] format. {LazyTestUsage}";
                return false;
            }

            var key = input.Substring(keyStart, i - keyStart).Trim();
            if (key.Length == 0)
            {
                error = $"Missing argument name. {LazyTestUsage}";
                return false;
            }

            i++; // skip ':'
            while (i < input.Length && char.IsWhiteSpace(input[i]))
                i++;

            if (i >= input.Length || input[i] != '[')
            {
                error = $"Expected '[' after '{key}:'. {LazyTestUsage}";
                return false;
            }

            i++; // skip '['
            var valueBuilder = new StringBuilder();
            var inQuotes = false;
            var closed = false;

            while (i < input.Length)
            {
                var c = input[i];
                if (c == '\\' && i + 1 < input.Length && input[i + 1] == '"')
                {
                    valueBuilder.Append('"');
                    i += 2;
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    i++;
                    continue;
                }

                if (c == ']' && !inQuotes)
                {
                    closed = true;
                    i++;
                    break;
                }

                valueBuilder.Append(c);
                i++;
            }

            if (!closed)
            {
                error = $"Missing closing ']' for '{key}'. {LazyTestUsage}";
                return false;
            }

            var value = valueBuilder.ToString().Trim();
            if (value.Length == 0)
            {
                error = $"Empty value for '{key}'. {LazyTestUsage}";
                return false;
            }

            if (!args.TryAdd(key, value))
            {
                error = $"Duplicate argument '{key}'. {LazyTestUsage}";
                return false;
            }
        }

        if (args.Count == 0)
        {
            error = LazyTestUsage;
            return false;
        }

        return true;
    }

    private static bool TryParseUpToToken(string token, out RollState rollState)
    {
        switch (token.ToLowerInvariant())
        {
            case "need":
            case "n":
                rollState = RollState.UpToNeed;
                return true;
            case "greed":
            case "g":
                rollState = RollState.UpToGreed;
                return true;
            case "pass":
            case "p":
                rollState = RollState.UpToPass;
                return true;
            default:
                rollState = RollState.UpToNeed;
                return false;
        }
    }

    private static ContentFinderCondition? ResolveDuty(string dutyQuery)
    {
        var dutySheet = Svc.Data.GetExcelSheet<ContentFinderCondition>();
        if (uint.TryParse(dutyQuery, out var dutyId))
        {
            var duty = dutySheet.GetRowOrDefault(dutyId);
            if (duty == null || duty.Value.RowId == 0)
            {
                DuoLog.Debug($"No duty found matching id '{dutyId}'.");
                return null;
            }

            return duty.Value;
        }

        var search = dutyQuery.Trim();
        var matches = dutySheet
            .Where(x => x.RowId > 0 && !x.Name.IsEmpty &&
                        x.Name.ToString().Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();

        switch (matches.Count)
        {
            case 0:
                DuoLog.Debug($"No duty found matching your search '{search}'.");
                return null;
            case > 1:
            {
                Svc.Chat.Print(
                    $"Found {matches.Count} duties for search '{search}'. Showing the first 5:");
                foreach (var match in matches.Take(5))
                    Svc.Chat.Print($"[LazyLoot Duty Test] :: ID {match.RowId} :: {match.Name.ToString()}");
                return null;
            }
            default:
                return matches[0];
        }
    }

    private readonly struct LazyTestRequest
    {
        public LazyTestRequest(string itemQuery, string? dutyQuery, RollState upTo)
        {
            ItemQuery = itemQuery;
            DutyQuery = dutyQuery;
            UpTo = upTo;
        }

        public string ItemQuery { get; }
        public string? DutyQuery { get; }
        public RollState UpTo { get; }
    }

    internal readonly struct LazyTestResult
    {
        public LazyTestResult(DateTime timestamp, uint itemId, string itemName, uint? dutyId, string dutyName,
            RollState upTo, RollResult finalResult)
        {
            Timestamp = timestamp;
            ItemId = itemId;
            ItemName = itemName;
            DutyId = dutyId;
            DutyName = dutyName;
            UpTo = upTo;
            FinalResult = finalResult;
        }

        public DateTime Timestamp { get; }
        public uint ItemId { get; }
        public string ItemName { get; }
        public uint? DutyId { get; }
        public string DutyName { get; }
        public RollState UpTo { get; }
        public RollResult FinalResult { get; }
    }
}
