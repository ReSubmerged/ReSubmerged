using HarmonyLib;
using Hazel;

namespace MinerRole
{
    [HarmonyPatch]
    public static class MinerPatches
    {
        [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update)), HarmonyPostfix]
        static void HudManager_Update(HudManager __instance) => Miner.Update(__instance);

        //Le bouton « Creuser » n'utilise jamais la capacité du rôle du jeu
        [HarmonyPatch(typeof(AbilityButton), nameof(AbilityButton.DoClick)), HarmonyPrefix]
        static bool AbilityButton_DoClick(AbilityButton __instance)
        {
            if (!Miner.IsMinerButton(__instance)) return true;
            Miner.Dig();
            return false;
        }

        //Sinon le jeu le recale sur la capacité du rôle (inexistante pour un Imposteur simple)
        [HarmonyPatch(typeof(AbilityButton), nameof(AbilityButton.Update)), HarmonyPrefix]
        static bool AbilityButton_Update(AbilityButton __instance) => !Miner.IsMinerButton(__instance);

        //Écran du rôle au début de la partie : « Mineur » au lieu de « Imposteur »
        [HarmonyPatch(typeof(IntroCutscene._ShowRole_d__41), nameof(IntroCutscene._ShowRole_d__41.MoveNext)), HarmonyPostfix]
        static void IntroCutscene_ShowRole(IntroCutscene._ShowRole_d__41 __instance)
        {
            if (!Miner.IsLocal) return;
            var intro = __instance.__4__this;
            if (intro == null) return;
            intro.RoleText.text = "Mineur";
            intro.RoleText.color = Miner.Color;
            intro.RoleBlurbText.text = "Creuse des conduits reliés entre eux";
            intro.RoleBlurbText.color = Miner.Color;
            intro.YouAreText.color = Miner.Color;
        }

        [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc)), HarmonyPostfix]
        static void PlayerControl_HandleRpc([HarmonyArgument(0)] byte callId, [HarmonyArgument(1)] MessageReader reader) => Miner.HandleRpc(callId, reader);

        //Hôte : choisit les Mineurs une fois les rôles du jeu distribués (avant l'intro)
        [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles)), HarmonyPostfix]
        static void RoleManager_SelectRoles()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            Miner.SelectMiners();
        }

        //Retour au lobby : plus de Mineur, et l'hôte recharge ses réglages
        [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start)), HarmonyPostfix]
        static void LobbyBehaviour_Start()
        {
            Miner.Clear();
            if (AmongUsClient.Instance && AmongUsClient.Instance.AmHost) MinerOptions.LoadLocal();
        }

        //Un joueur arrive : l'hôte lui envoie les réglages du Mineur
        [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.CoSpawnPlayer)), HarmonyPostfix]
        static void PlayerPhysics_CoSpawnPlayer() => Miner.SyncSettings();
    }
}
