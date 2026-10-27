using HarmonyLib;

namespace JesterRole
{
    //Écran du rôle au début de la partie : « Jester » au lieu de « Coéquipier »
    [HarmonyPatch(typeof(IntroCutscene._ShowRole_d__41), nameof(IntroCutscene._ShowRole_d__41.MoveNext))]
    public static class IntroCutscenePatch
    {
        public static void Postfix(IntroCutscene._ShowRole_d__41 __instance)
        {
            if (!Jester.IsLocal) return;
            var intro = __instance.__4__this;
            if (intro == null) return;
            intro.RoleText.text = "Jester";
            intro.RoleText.color = Jester.Color;
            intro.RoleBlurbText.text = "Faites-vous éjecter pour gagner";
            intro.RoleBlurbText.color = Jester.Color;
            intro.YouAreText.color = Jester.Color;
        }
    }
}
