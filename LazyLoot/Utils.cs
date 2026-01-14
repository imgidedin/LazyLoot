using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Dalamud.Bindings.ImGui;

namespace LazyLoot;

internal class Utils
{
    public static unsafe int GetPlayerIlevel()
    {
        return UIState.Instance()->CurrentItemLevel;
    }


    public static bool CheckboxTextWrapped(string text, ref bool v)
    {
        ImGui.PushID(text);

        var changed = ImGui.Checkbox("##chk", ref v);
        ImGui.SameLine();

        var wrapEndX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        ImGui.PushTextWrapPos(wrapEndX);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();

        if (ImGui.IsItemClicked())
        {
            v = !v;
            changed = true;
        }

        ImGui.PopID();
        return changed;
    }
}