using HarmonyLib;
using Hazel;

namespace JesterRole
{
    [HarmonyPatch]
    public static class JesterPatches
    {
        [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update)), HarmonyPostfix]
        static void HudManager_Update() => Jester.Update();

        [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc)), HarmonyPostfix]
        static void PlayerControl_HandleRpc([HarmonyArgument(0)] byte callId, [HarmonyArgument(1)] MessageReader reader) => Jester.HandleRpc(callId, reader);

        //Hôte : choisit le Jester une fois les rôles du jeu distribués (avant l'intro)
        [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles)), HarmonyPostfix]
        static void RoleManager_SelectRoles()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            Jester.SelectJester();
        }

        //Retour au lobby : plus de Jester, et l'hôte recharge ses réglages
        [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start)), HarmonyPostfix]
        static void LobbyBehaviour_Start()
        {
            Jester.Clear();
            if (AmongUsClient.Instance && AmongUsClient.Instance.AmHost) JesterOptions.LoadLocal();
        }

        //Un joueur arrive : l'hôte lui envoie les réglages du Jester
        [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.CoSpawnPlayer)), HarmonyPostfix]
        static void PlayerPhysics_CoSpawnPlayer() => Jester.SyncSettings();

        //Les tâches du Jester ne comptent pas dans la barre ni pour la victoire des Coéquipiers par les tâches
        [HarmonyPatch(typeof(GameData), nameof(GameData.RecomputeTaskCounts)), HarmonyPostfix]
        static void GameData_RecomputeTaskCounts(GameData __instance)
        {
            if (!Jester.JesterId.HasValue) return;
            var data = __instance.GetPlayerById(Jester.JesterId.Value);
            if (data == null || data.Disconnected || data.Tasks == null) return;
            foreach (var task in data.Tasks)
            {
                __instance.TotalTasks--;
                if (task.Complete) __instance.CompletedTasks--;
            }
            if (__instance.TotalTasks < 0) __instance.TotalTasks = 0;
            if (__instance.CompletedTasks < 0) __instance.CompletedTasks = 0;
        }
    }
}
