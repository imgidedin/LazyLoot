using Dalamud.Interface.FontIdentifier;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace LazyLoot;

internal class Utils
{
    public static unsafe int GetPlayerIlevel()
    {
        return UIState.Instance()->CurrentItemLevel;
    }
}