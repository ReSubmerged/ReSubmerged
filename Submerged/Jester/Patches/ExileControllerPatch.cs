using HarmonyLib;

[HarmonyPatch(typeof(ExileController), nameof(ExileController.WrapUp))]
public static class ExileController_WrapUp
{
    public static bool Prefix()
    {
        PlayerControl.LocalPlayer.Die(DeathReason.Kill, true);
        return true;
    }
}