using HarmonyLib;
using Hazel;
using System;
using System.Collections.Generic;
using TMPro;

namespace SherriffRole
{
    [HarmonyPatch]
    public static class Patches
    {
        //Lobby settings summary
        [HarmonyPatch(typeof(IGameOptionsExtensions), nameof(IGameOptionsExtensions.ToHudString)), HarmonyPostfix]
        static void ToHudString(ref string __result) =>
            __result += "\nShow Sheriff: " + (CustomGameOptions.showSheriff ? "On" : "Off") + "\nSheriff Kill Cooldown: " + CustomGameOptions.SheriffKillCD + "s";

        [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update)), HarmonyPostfix]
        static void HudManager_Update(HudManager __instance) => Sheriff.Update(__instance);

        //Role reveal: the vanilla texts are set during the coroutine, so overwrite them for a second
        [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.ShowRole)), HarmonyPrefix]
        static void IntroCutscene_ShowRole(IntroCutscene __instance)
        {
            if (!Sheriff.IsLocal) return;
            HudManager.Instance.StartCoroutine(Effects.Lerp(1f, new Action<float>(_ =>
            {
                __instance.RoleText.text = "Sheriff";
                __instance.RoleText.color = Sheriff.Color;
                __instance.RoleBlurbText.text = "Shoot the <color=#FF0000FF>Impostor</color>";
                __instance.RoleBlurbText.color = Sheriff.Color;
                __instance.BackgroundBar.material.color = Sheriff.Color;
            })));
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

        //Host only: pick a random Crewmate as Sheriff once the roles are assigned
        [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles)), HarmonyPostfix]
        static void RoleManager_SelectRoles()
        {
            if (!AmongUsClient.Instance.AmHost) return;

            var crew = new List<PlayerControl>();
            foreach (var player in PlayerControl.AllPlayerControls)
                if (player.Data != null && !player.Data.Disconnected && player.Data.Role != null && !player.Data.Role.IsImpostor) crew.Add(player);
            if (crew.Count == 0) return;

            Sheriff.Announce(crew[new Random().Next(crew.Count)]);
        }

        [HarmonyPatch(typeof(VersionShower), nameof(VersionShower.Start)), HarmonyPostfix]
        static void VersionShower_Start(VersionShower __instance)
        {
            __instance.text.alignment = TextAlignmentOptions.Top;
            __instance.text.fontSize = 2;
            __instance.text.text += "\n<color=#FFD11AFF>ReSubmerged Developer</color>";
        }
    }
}
