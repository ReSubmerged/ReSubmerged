using System.Collections.Generic;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;

namespace JesterRole
{
    /// <summary>
    /// Jester : un Coéquipier qui gagne seul s'il se fait éjecter lors d'un vote.
    /// Ses tâches ne comptent pas pour la victoire des Coéquipiers.
    /// </summary>
    public static class Jester
    {
        public static readonly Color Color = new(0.8f, 0.1f, 0.4f, 1f);

        /// <summary>Joueur Jester de la partie en cours (ou personne).</summary>
        public static byte? JesterId;

        /// <summary>Rempli quand le Jester a été éjecté : la partie se termine sur sa victoire.</summary>
        public static byte? WinnerId;

        public static bool IsJester(PlayerControl player) => player != null && JesterId == player.PlayerId;

        public static bool IsLocal => IsJester(PlayerControl.LocalPlayer);

        public static void Clear()
        {
            JesterId = null;
            WinnerId = null;
        }

        /// <summary>Jeu libre : donne (ou retire) le rôle de Jester au joueur local, sans passer par l'hôte.</summary>
        public static void SetFreeplay(bool enabled)
        {
            if (!enabled)
            {
                if (IsLocal) Clear();
                return;
            }
            Clear();
            JesterId = PlayerControl.LocalPlayer.PlayerId;
        }

        static MessageWriter StartRpc(JesterRpc call) =>
            AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte) call, SendOption.Reliable);

        static void FinishRpc(MessageWriter writer) => AmongUsClient.Instance.FinishRpcImmediately(writer);

        //Hôte : choisit le Jester parmi les Coéquipiers simples (pas les Shérifs), comme un rôle du jeu (nombre + chance)
        public static void SelectJester()
        {
            Clear();
            var options = GameOptionsManager.Instance.CurrentGameOptions;
            var hideAndSeek = options.GameMode is GameModes.HideNSeek or GameModes.SeekFools;

            if (!hideAndSeek && JesterOptions.Count > 0 && JesterOptions.Chance > 0)
            {
                var random = new System.Random();
                if (random.Next(100) < JesterOptions.Chance)
                {
                    var candidates = new List<PlayerControl>();
                    foreach (var player in PlayerControl.AllPlayerControls)
                    {
                        var data = player.Data;
                        if (data != null && !data.Disconnected && data.Role != null && data.Role.Role == RoleTypes.Crewmate
                            && !SherriffRole.Sheriff.IsSheriff(player))
                            candidates.Add(player);
                    }
                    if (candidates.Count > 0) JesterId = candidates[random.Next(candidates.Count)].PlayerId;
                }
            }

            var writer = StartRpc(JesterRpc.SetJester);
            writer.Write(JesterId.HasValue);
            if (JesterId.HasValue) writer.Write(JesterId.Value);
            FinishRpc(writer);
        }

        //Hôte : envoie les réglages du lobby à tout le monde
        public static void SyncSettings()
        {
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || PlayerControl.LocalPlayer == null) return;
            var writer = StartRpc(JesterRpc.SyncJesterSettings);
            writer.Write((byte) JesterOptions.Count);
            writer.Write((byte) JesterOptions.Chance);
            FinishRpc(writer);
        }

        /// <summary>Hôte : le Jester vient d'être éjecté, il gagne la partie.</summary>
        public static void Win(PlayerControl jester)
        {
            if (!AmongUsClient.Instance.AmHost || WinnerId.HasValue) return;
            WinnerId = jester.PlayerId;

            //Tout le monde apprend le gagnant avant la fin de partie (même canal fiable, donc dans l'ordre)
            var writer = StartRpc(JesterRpc.JesterWin);
            writer.Write(jester.PlayerId);
            FinishRpc(writer);

            //Raison du jeu quelconque : les gagnants et l'écran de fin sont remplacés à la réception (cf. EndGamePatches)
            GameManager.Instance.RpcEndGame(GameOverReason.ImpostorsByVote, false);
        }

        public static void HandleRpc(byte callId, MessageReader reader)
        {
            switch (callId)
            {
                case (byte) JesterRpc.SetJester:
                    Clear();
                    if (reader.ReadBoolean()) JesterId = reader.ReadByte();
                    break;
                case (byte) JesterRpc.JesterWin:
                    WinnerId = reader.ReadByte();
                    break;
                case (byte) JesterRpc.SyncJesterSettings:
                {
                    int count = reader.ReadByte(), chance = reader.ReadByte();
                    var changed = count != JesterOptions.Count || chance != JesterOptions.Chance;
                    JesterOptions.Count = count;
                    JesterOptions.Chance = chance;
                    if (changed) JesterOptionsMenu.NotifyChange(false);
                    break;
                }
            }
        }

        //Appelé à chaque image depuis HudManager.Update : le Jester voit son nom en rose
        public static void Update()
        {
            if (!IsLocal) return;
            var me = PlayerControl.LocalPlayer;
            me.cosmetics.nameText.color = Color;
            if (MeetingHud.Instance == null) return;
            foreach (var area in MeetingHud.Instance.playerStates)
                if (area.PlayerId == me.PlayerId) area.NameText.color = Color;
        }
    }
}
