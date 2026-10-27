using HarmonyLib;

namespace JesterRole
{
    [HarmonyPatch]
    public static class EndGameManagerPatch
    {
        //Fin de partie reçue : si le Jester a gagné, il est le seul gagnant
        [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd)), HarmonyPostfix]
        static void AmongUsClient_OnGameEnd()
        {
            if (!Jester.WinnerId.HasValue) return;
            var data = GameData.Instance ? GameData.Instance.GetPlayerById(Jester.WinnerId.Value) : null;
            if (data == null) return;
            EndGameResult.CachedWinners.Clear();
            EndGameResult.CachedWinners.Add(new CachedPlayerData(data));
        }

        //Écran de fin : texte et couleur du Jester, seulement quand il a gagné
        [HarmonyPatch(typeof(EndGameManager), nameof(EndGameManager.SetEverythingUp)), HarmonyPostfix]
        static void EndGameManager_SetEverythingUp(EndGameManager __instance)
        {
            if (Jester.WinnerId.HasValue)
            {
                var won = PlayerControl.LocalPlayer != null && PlayerControl.LocalPlayer.PlayerId == Jester.WinnerId.Value;
                __instance.WinText.text = won ? "Victoire du Jester" : "Le Jester a gagné";
                __instance.WinText.color = Jester.Color;
                __instance.BackgroundBar.material.color = Jester.Color;
            }

            //On remet à zéro une fois la partie terminée
            Jester.Clear();
        }
    }
}
