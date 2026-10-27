using HarmonyLib;

[HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.BeginImpostor))]
public static class IntroCutscene_BeginImpostor
{
    public static void Postfix(IntroCutscene __instance)
    {

    }
}