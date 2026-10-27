using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(EndGameManager), nameof(EndGameManager.SetEverythingUp))]
public static class EndGameManager_SetEverythingUp
{
    public static void Postfix(EndGameManager __instance)
    {
        __instance.WinText.text = "Jester Wins";
        __instance.WinText.color = Jester.jesterColor;
        __instance.BackgroundBar.material.color = Jester.jesterColor;

        // We set these back to null when the game finishes
        Jester.jester = null;
        Jester.jesterId = null;
    }
}