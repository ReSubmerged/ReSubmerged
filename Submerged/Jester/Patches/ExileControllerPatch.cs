using HarmonyLib;

namespace JesterRole
{
    //PlayerControl.Exiled est appelé sur le joueur éjecté par toutes les cinématiques d'éjection,
    //y compris celle de Submerged (qui n'utilise pas ExileController.WrapUp)
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Exiled))]
    public static class ExileControllerPatch
    {
        public static void Postfix(PlayerControl __instance)
        {
            if (AmongUsClient.Instance.AmHost && Jester.IsJester(__instance)) Jester.Win(__instance);
        }
    }
}
