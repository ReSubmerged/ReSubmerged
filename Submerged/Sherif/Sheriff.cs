using Hazel;
using System;
using UnityEngine;

namespace SherriffRole
{
    public static class Sheriff
    {
        public static readonly Color Color = new Color(1f, 204f / 255f, 0f, 1f);

        public static PlayerControl Player;
        public static float KillTimer;
        static PlayerControl closest;

        public static bool IsLocal => IsSheriff(PlayerControl.LocalPlayer);

        public static bool IsSheriff(PlayerControl player) => Player != null && player != null && player.PlayerId == Player.PlayerId;

        public static void Set(PlayerControl player)
        {
            if (player == null) return;
            Player = player;
            KillTimer = Math.Max(0f, CustomGameOptions.SheriffKillCD - 10f);
        }

        public static MessageWriter StartRpc(CustomRPC call) =>
            AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)call, SendOption.Reliable);

        public static void FinishRpc(MessageWriter writer) => AmongUsClient.Instance.FinishRpcImmediately(writer);

        //Host: tell everyone who the Sheriff is, along with the host options
        public static void Announce(PlayerControl sheriff)
        {
            var writer = StartRpc(CustomRPC.SetSheriff);
            writer.Write(sheriff.PlayerId);
            writer.Write(CustomGameOptions.showSheriff);
            writer.Write(CustomGameOptions.SheriffKillCD);
            FinishRpc(writer);
            Set(sheriff);
        }

        public static void HandleRpc(byte callId, MessageReader reader)
        {
            switch (callId)
            {
                case (byte)CustomRPC.SetSheriff:
                    var sheriff = Find(reader.ReadByte());
                    CustomGameOptions.showSheriff = reader.ReadBoolean();
                    CustomGameOptions.SheriffKillCD = reader.ReadSingle();
                    Set(sheriff);
                    break;
                case (byte)CustomRPC.SheriffKill:
                    var killer = Find(reader.ReadByte());
                    var victim = Find(reader.ReadByte());
                    if (IsSheriff(killer) && victim != null) killer.MurderPlayer(victim, MurderResultFlags.Succeeded);
                    break;
            }
        }

        //Called every frame from HudManager.Update
        public static void Update(HudManager hud)
        {
            if (Player == null) return;
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
            if (!IsLocal && !CustomGameOptions.showSheriff) return;
            foreach (var area in meeting.playerStates)
                if (area.PlayerId == Player.PlayerId) area.NameText.color = Color;
        }

        static void UpdateRole(HudManager hud)
        {
            if (PlayerControl.AllPlayerControls.Count <= 1) return;
            if (IsLocal || CustomGameOptions.showSheriff) Player.cosmetics.nameText.color = Color;
            if (!IsLocal) return;

            //The cooldown restarts after each meeting
            if (MeetingHud.Instance != null) KillTimer = CustomGameOptions.SheriffKillCD;

            //The button only exists in a running match, while alive and outside meetings
            var active = !Player.Data.IsDead && MeetingHud.Instance == null && ShipStatus.Instance != null;
            var button = hud.KillButton;
            if (active) button.Show();
            else button.Hide();
            if (!active || !Player.CanMove) return;

            KillTimer = Math.Max(0f, KillTimer - Time.deltaTime);
            button.SetCoolDown(KillTimer, CustomGameOptions.SheriffKillCD);
            closest = FindClosestTarget();
            button.SetTarget(closest); //null clears the highlight
            if (Input.GetKeyDown(KeyCode.Q)) button.DoClick();
        }
    }
}
