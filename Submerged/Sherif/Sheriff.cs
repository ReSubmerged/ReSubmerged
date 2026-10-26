using Hazel;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SherriffRole
{
    public static class Sheriff
    {
        public static readonly Color Color = new Color(1f, 204f / 255f, 0f, 1f);

        /// <summary>Joueurs Shérifs de la partie en cours (plusieurs possibles).</summary>
        public static readonly HashSet<byte> Players = new();

        public static float KillTimer;
        static PlayerControl closest;

        public static bool IsLocal => PlayerControl.LocalPlayer != null && IsSheriff(PlayerControl.LocalPlayer);

        public static bool IsSheriff(PlayerControl player) => player != null && Players.Contains(player.PlayerId);

        public static void Clear()
        {
            Players.Clear();
            closest = null;
        }

        static void Set(IEnumerable<byte> ids)
        {
            Clear();
            foreach (var id in ids) Players.Add(id);
            KillTimer = Math.Max(0f, CustomGameOptions.SheriffKillCD - 10f);
        }

        public static MessageWriter StartRpc(CustomRPC call) =>
            AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)call, SendOption.Reliable);

        public static void FinishRpc(MessageWriter writer) => AmongUsClient.Instance.FinishRpcImmediately(writer);

        //Host: tell everyone who the Sheriffs are (possibly nobody), along with the host options
        public static void Announce(List<byte> sheriffs)
        {
            var writer = StartRpc(CustomRPC.SetSheriff);
            writer.Write((byte)sheriffs.Count);
            foreach (var id in sheriffs) writer.Write(id);
            writer.Write(CustomGameOptions.showSheriff);
            writer.Write(CustomGameOptions.SheriffKillCD);
            FinishRpc(writer);
            Set(sheriffs);
        }

        //Host: send the lobby settings to everyone
        public static void SyncSettings()
        {
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || PlayerControl.LocalPlayer == null) return;
            var writer = StartRpc(CustomRPC.SyncSheriffSettings);
            writer.Write((byte)CustomGameOptions.SheriffCount);
            writer.Write((byte)CustomGameOptions.SheriffChance);
            writer.Write(CustomGameOptions.showSheriff);
            writer.Write(CustomGameOptions.SheriffKillCD);
            FinishRpc(writer);
        }

        public static void HandleRpc(byte callId, MessageReader reader)
        {
            switch (callId)
            {
                case (byte)CustomRPC.SetSheriff:
                {
                    var count = reader.ReadByte();
                    var ids = new List<byte>();
                    for (var i = 0; i < count; i++) ids.Add(reader.ReadByte());
                    CustomGameOptions.showSheriff = reader.ReadBoolean();
                    CustomGameOptions.SheriffKillCD = reader.ReadSingle();
                    Set(ids);
                    break;
                }
                case (byte)CustomRPC.SheriffKill:
                {
                    var killer = Find(reader.ReadByte());
                    var victim = Find(reader.ReadByte());
                    if (IsSheriff(killer) && victim != null) killer.MurderPlayer(victim, MurderResultFlags.Succeeded);
                    break;
                }
                case (byte)CustomRPC.SyncSheriffSettings:
                {
                    int count = reader.ReadByte(), chance = reader.ReadByte();
                    var changed = count != CustomGameOptions.SheriffCount || chance != CustomGameOptions.SheriffChance;
                    CustomGameOptions.SheriffCount = count;
                    CustomGameOptions.SheriffChance = chance;
                    CustomGameOptions.showSheriff = reader.ReadBoolean();
                    CustomGameOptions.SheriffKillCD = reader.ReadSingle();
                    if (changed) SheriffOptionsMenu.NotifyChange(false);
                    break;
                }
            }
        }

        //Host only: choose the Sheriffs among the plain Crewmates, like a vanilla role (count + chance)
        public static void SelectSheriffs()
        {
            var sheriffs = new List<byte>();
            var options = GameOptionsManager.Instance.CurrentGameOptions;
            var hideAndSeek = options.GameMode is AmongUs.GameOptions.GameModes.HideNSeek or AmongUs.GameOptions.GameModes.SeekFools;

            if (!hideAndSeek && CustomGameOptions.SheriffCount > 0 && CustomGameOptions.SheriffChance > 0)
            {
                var candidates = new List<PlayerControl>();
                foreach (var player in PlayerControl.AllPlayerControls)
                {
                    var data = player.Data;
                    //Only simple Crewmates: Engineers, Scientists... keep their vanilla role
                    if (data != null && !data.Disconnected && data.Role != null && data.Role.Role == AmongUs.GameOptions.RoleTypes.Crewmate)
                        candidates.Add(player);
                }

                var random = new System.Random();
                for (var i = 0; i < CustomGameOptions.SheriffCount && candidates.Count > 0; i++)
                {
                    if (random.Next(100) >= CustomGameOptions.SheriffChance) continue;
                    var index = random.Next(candidates.Count);
                    sheriffs.Add(candidates[index].PlayerId);
                    candidates.RemoveAt(index);
                }
            }

            Announce(sheriffs);
        }

        //Called every frame from HudManager.Update
        public static void Update(HudManager hud)
        {
            if (Players.Count == 0) return;
            UpdateRole(hud);
            if (MeetingHud.Instance != null) HighlightInMeeting(MeetingHud.Instance);
        }

        //Local Sheriff clicked the kill button (the original kill logic is never used)
        public static void Shoot()
        {
            var me = PlayerControl.LocalPlayer;
            if (KillTimer != 0 || !me.CanMove || closest == null) return;

            //Shooting a Crewmate kills the Sheriff instead
            var victim = closest.Data.Role != null && closest.Data.Role.IsImpostor ? closest : me;
            var writer = StartRpc(CustomRPC.SheriffKill);
            writer.Write(me.PlayerId);
            writer.Write(victim.PlayerId);
            FinishRpc(writer);
            me.MurderPlayer(victim, MurderResultFlags.Succeeded);

            me.SetKillTimer(CustomGameOptions.SheriffKillCD);
            KillTimer = CustomGameOptions.SheriffKillCD;
        }

        static PlayerControl Find(byte id)
        {
            foreach (var player in PlayerControl.AllPlayerControls)
                if (player.PlayerId == id) return player;
            return null;
        }

        static PlayerControl FindClosestTarget()
        {
            if (!ShipStatus.Instance) return null;

            PlayerControl result = null;
            var me = PlayerControl.LocalPlayer;
            var range = GameManager.Instance.LogicOptions.GetKillDistance();
            var origin = me.GetTruePosition();
            foreach (var info in GameData.Instance.AllPlayers)
            {
                var player = info.Object;
                if (info.Disconnected || info.IsDead || info.PlayerId == me.PlayerId || player == null || player.inVent) continue;

                var offset = player.GetTruePosition() - origin;
                var distance = offset.magnitude;
                //In range and not through walls
                if (distance <= range && !PhysicsHelpers.AnyNonTriggersBetween(origin, offset.normalized, distance, Constants.ShipAndObjectsMask))
                {
                    result = player;
                    range = distance;
                }
            }
            return result;
        }

        static void HighlightInMeeting(MeetingHud meeting)
        {
            foreach (var area in meeting.playerStates)
            {
                if (!Players.Contains(area.PlayerId)) continue;
                if (CustomGameOptions.showSheriff || area.PlayerId == PlayerControl.LocalPlayer.PlayerId) area.NameText.color = Color;
            }
        }

        static void UpdateRole(HudManager hud)
        {
            if (PlayerControl.AllPlayerControls.Count <= 1) return;

            foreach (var player in PlayerControl.AllPlayerControls)
                if (player != null && IsSheriff(player) && (CustomGameOptions.showSheriff || player == PlayerControl.LocalPlayer))
                    player.cosmetics.nameText.color = Color;

            if (!IsLocal) return;
            var me = PlayerControl.LocalPlayer;

            //The cooldown restarts after each meeting
            if (MeetingHud.Instance != null) KillTimer = CustomGameOptions.SheriffKillCD;

            //The button only exists in a running match, while alive and outside meetings
            var active = !me.Data.IsDead && MeetingHud.Instance == null && ShipStatus.Instance != null;
            var button = hud.KillButton;
            if (active) button.Show();
            else button.Hide();
            if (!active || !me.CanMove) return;

            KillTimer = Math.Max(0f, KillTimer - Time.deltaTime);
            button.SetCoolDown(KillTimer, CustomGameOptions.SheriffKillCD);
            closest = FindClosestTarget();
            button.SetTarget(closest); //null clears the highlight
            if (Input.GetKeyDown(KeyCode.Q)) button.DoClick();
        }
    }
}
