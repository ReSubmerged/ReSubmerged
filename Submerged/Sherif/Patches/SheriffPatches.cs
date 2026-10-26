using HarmonyLib;
using Hazel;
using System;
using TMPro;

namespace SherriffRole
{
    [HarmonyPatch]
    public static class Patches
    {
        [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update)), HarmonyPostfix]
        static void HudManager_Update(HudManager __instance) => Sheriff.Update(__instance);

        //Écran du rôle au début de la partie : le jeu remplit les textes pendant la coroutine,
        //on les remplace à chaque étape pour afficher « Shérif » au lieu de « Coéquipier »
        [HarmonyPatch(typeof(IntroCutscene._ShowRole_d__41), nameof(IntroCutscene._ShowRole_d__41.MoveNext)), HarmonyPostfix]
        static void IntroCutscene_ShowRole(IntroCutscene._ShowRole_d__41 __instance)
        {
            if (!Sheriff.IsLocal) return;
            var intro = __instance.__4__this;
            if (intro == null) return;
            intro.RoleText.text = "Shérif";
            intro.RoleText.color = Sheriff.Color;
            intro.RoleBlurbText.text = "Tire sur l'<color=#FF1919FF>Imposteur</color>";
            intro.RoleBlurbText.color = Sheriff.Color;
            intro.YouAreText.color = Sheriff.Color;
        }

        //The Sheriff never uses the original kill logic
        [HarmonyPatch(typeof(KillButton), nameof(KillButton.DoClick)), HarmonyPrefix]
        static bool KillButton_DoClick()
        {
            if (!Sheriff.IsLocal) return true;
            Sheriff.Shoot();
            return false;
        }

        [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc)), HarmonyPostfix]
        static void PlayerControl_HandleRpc([HarmonyArgument(0)] byte callId, [HarmonyArgument(1)] MessageReader reader) => Sheriff.HandleRpc(callId, reader);

        //Host only: choose the Sheriffs once the vanilla roles are assigned (before the intro)
        [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles)), HarmonyPostfix]
        static void RoleManager_SelectRoles()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            Sheriff.SelectSheriffs();
        }

        //Back in the lobby: no more Sheriff, and the host sends his settings again
        [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start)), HarmonyPostfix]
        static void LobbyBehaviour_Start()
        {
            Sheriff.Clear();
            if (AmongUsClient.Instance && AmongUsClient.Instance.AmHost) CustomGameOptions.LoadLocal();
        }

        //A player joins: the host sends him the Sheriff settings
        [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.CoSpawnPlayer)), HarmonyPostfix]
        static void PlayerPhysics_CoSpawnPlayer() => Sheriff.SyncSettings();

        [HarmonyPatch(typeof(VersionShower), nameof(VersionShower.Start)), HarmonyPostfix]
        static void VersionShower_Start(VersionShower __instance)
        {
            __instance.text.alignment = TextAlignmentOptions.Top;
            __instance.text.fontSize = 2;
            __instance.text.text += "\n<color=#FFD11AFF>ReSubmerged Developer</color>";
        }
    }
}
