using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using Submerged.Vents;
using UnityEngine;

namespace MinerRole
{
    public enum MinerRpc : byte { SetMiner = 153, PlaceVent = 154, SyncMinerSettings = 155 }

    /// <summary>
    /// Mineur : un Imposteur qui creuse des conduits là où il se trouve. Ses conduits sont reliés entre eux (en boucle)
    /// et utilisables par tous les Imposteurs.
    /// </summary>
    public static class Miner
    {
        public static readonly Color Color = Palette.ImpostorRed;

        /// <summary>Joueurs Mineurs de la partie en cours.</summary>
        public static readonly HashSet<byte> Players = new();

        /// <summary>Conduits creusés par chaque Mineur, dans l'ordre.</summary>
        static readonly Dictionary<byte, List<Vent>> Vents = new();

        public static float Timer;
        static AbilityButton _button;

        public static bool IsLocal => PlayerControl.LocalPlayer != null && IsMiner(PlayerControl.LocalPlayer);

        public static bool IsMiner(PlayerControl player) => player != null && Players.Contains(player.PlayerId);

        public static bool IsMinerButton(ActionButton button) => _button != null && _button && button != null && button.Pointer == _button.Pointer;

        static int VentsLeft(byte playerId) => Math.Max(0, MinerOptions.MaxVentCount - (Vents.TryGetValue(playerId, out var list) ? list.Count : 0));

        public static void Clear()
        {
            Players.Clear();
            Vents.Clear();
            if (_button != null && _button) _button.Hide();
        }

        static void Set(IEnumerable<byte> ids)
        {
            Clear();
            foreach (var id in ids) Players.Add(id);
            Timer = Math.Max(0f, MinerOptions.Cooldown - 10f);
        }

        static MessageWriter StartRpc(MinerRpc call) =>
            AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte) call, SendOption.Reliable);

        static void FinishRpc(MessageWriter writer) => AmongUsClient.Instance.FinishRpcImmediately(writer);

        static void WriteSettings(MessageWriter writer)
        {
            writer.Write(MinerOptions.Cooldown);
            writer.Write((byte) MinerOptions.MaxVentCount);
        }

        static void ReadSettings(MessageReader reader)
        {
            MinerOptions.Cooldown = reader.ReadSingle();
            MinerOptions.MaxVentCount = reader.ReadByte();
        }

        //Hôte : choisit les Mineurs parmi les Imposteurs simples, comme un rôle du jeu (nombre + chance)
        public static void SelectMiners()
        {
            var miners = new List<byte>();
            var options = GameOptionsManager.Instance.CurrentGameOptions;
            var hideAndSeek = options.GameMode is GameModes.HideNSeek or GameModes.SeekFools;

            if (!hideAndSeek && MinerOptions.Count > 0 && MinerOptions.Chance > 0)
            {
                var candidates = new List<PlayerControl>();
                foreach (var player in PlayerControl.AllPlayerControls)
                {
                    var data = player.Data;
                    //Seulement les Imposteurs simples : le Métamorphe, le Fantôme… gardent leur rôle
                    if (data != null && !data.Disconnected && data.Role != null && data.Role.Role == RoleTypes.Impostor)
                        candidates.Add(player);
                }

                var random = new System.Random();
                for (var i = 0; i < MinerOptions.Count && candidates.Count > 0; i++)
                {
                    if (random.Next(100) >= MinerOptions.Chance) continue;
                    var index = random.Next(candidates.Count);
                    miners.Add(candidates[index].PlayerId);
                    candidates.RemoveAt(index);
                }
            }

            var writer = StartRpc(MinerRpc.SetMiner);
            writer.Write((byte) miners.Count);
            foreach (var id in miners) writer.Write(id);
            WriteSettings(writer);
            FinishRpc(writer);
            Set(miners);
        }

        //Hôte : envoie les réglages du lobby à tout le monde
        public static void SyncSettings()
        {
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || PlayerControl.LocalPlayer == null) return;
            var writer = StartRpc(MinerRpc.SyncMinerSettings);
            writer.Write((byte) MinerOptions.Count);
            writer.Write((byte) MinerOptions.Chance);
            WriteSettings(writer);
            FinishRpc(writer);
        }

        public static void HandleRpc(byte callId, MessageReader reader)
        {
            switch (callId)
            {
                case (byte) MinerRpc.SetMiner:
                {
                    var count = reader.ReadByte();
                    var ids = new List<byte>();
                    for (var i = 0; i < count; i++) ids.Add(reader.ReadByte());
                    ReadSettings(reader);
                    Set(ids);
                    break;
                }
                case (byte) MinerRpc.PlaceVent:
                {
                    var owner = reader.ReadByte();
                    var position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    if (Players.Contains(owner)) CreateVent(owner, position);
                    break;
                }
                case (byte) MinerRpc.SyncMinerSettings:
                {
                    int count = reader.ReadByte(), chance = reader.ReadByte();
                    var changed = count != MinerOptions.Count || chance != MinerOptions.Chance;
                    MinerOptions.Count = count;
                    MinerOptions.Chance = chance;
                    ReadSettings(reader);
                    if (changed) MinerOptionsMenu.NotifyChange(false);
                    break;
                }
            }
        }

        static bool CanDig(PlayerControl me) =>
            Timer <= 0f && me.CanMove && !me.inVent && !me.Data.IsDead && VentsLeft(me.PlayerId) > 0 && ShipStatus.Instance != null;

        /// <summary>Le Mineur local a cliqué sur « Creuser ».</summary>
        public static void Dig()
        {
            var me = PlayerControl.LocalPlayer;
            if (me == null || !IsLocal || !CanDig(me)) return;

            var position = me.GetTruePosition();
            var writer = StartRpc(MinerRpc.PlaceVent);
            writer.Write(me.PlayerId);
            writer.Write(position.x);
            writer.Write(position.y);
            FinishRpc(writer);
            CreateVent(me.PlayerId, position);

            Timer = MinerOptions.Cooldown;
        }

        /// <summary>Crée le conduit chez tout le monde (même numéro partout) et le relie aux autres conduits du Mineur.</summary>
        static void CreateVent(byte owner, Vector2 position)
        {
            var ship = ShipStatus.Instance;
            if (!ship) return;
            if (!Vents.TryGetValue(owner, out var list)) Vents[owner] = list = new List<Vent>();
            if (list.Count >= MinerOptions.MaxVentCount) return;

            var template = FindTemplate(ship, position);
            if (template == null) return;

            var vent = UnityEngine.Object.Instantiate(template, template.transform.parent);
            vent.name = "MinerVent";
            //Numéro fixe par Mineur et par conduit : identique chez tous les joueurs
            var id = 1000 + owner * 16 + list.Count;
            while (ship.AllVents.Any(v => v && v.Id == id)) id += 256;
            vent.Id = id;

            //Même profondeur que les conduits de la carte (le z suit le y)
            var depth = template.transform.position.z - template.transform.position.y / 1000f;
            vent.transform.position = new Vector3(position.x, position.y, position.y / 1000f + depth);

            vent.Left = null;
            vent.Right = null;
            vent.Center = null;
            if (list.Count > 0)
            {
                var previous = list[list.Count - 1];
                previous.Right = vent;
                vent.Left = previous;
                //À partir de 3 conduits, le dernier rejoint le premier : ils forment une boucle
                if (list.Count >= 2)
                {
                    vent.Right = list[0];
                    list[0].Left = vent;
                }
            }
            list.Add(vent);

            ship.AllVents = ship.AllVents.Append(vent).ToArray();
        }

        /// <summary>Le conduit le plus proche sert de modèle (en évitant les conduits spéciaux de Submerged).</summary>
        static Vent FindTemplate(ShipStatus ship, Vector2 position)
        {
            Vent best = null;
            var bestDistance = float.MaxValue;
            foreach (var vent in ship.AllVents)
            {
                if (!vent || vent.Id >= 1000) continue;
                if (vent.Id is VentPatchData.UPPER_CENTRAL_VENT_ID or VentPatchData.LOWER_CENTRAL_VENT_ID or VentPatchData.ENGINE_ROOM_VENT_ID) continue;
                var distance = Vector2.Distance(position, vent.transform.position);
                if (distance < bestDistance)
                {
                    best = vent;
                    bestDistance = distance;
                }
            }
            return best ?? (ship.AllVents.Length > 0 ? ship.AllVents[0] : null);
        }

        //Appelé à chaque image depuis HudManager.Update
        public static void Update(HudManager hud)
        {
            if (!IsLocal)
            {
                if (_button != null && _button) _button.Hide();
                return;
            }

            var me = PlayerControl.LocalPlayer;
            var button = Button(hud);
            if (button == null) return;

            //Le temps de recharge repart après chaque réunion
            if (MeetingHud.Instance != null) Timer = MinerOptions.Cooldown;

            var active = !me.Data.IsDead && MeetingHud.Instance == null && ShipStatus.Instance != null;
            if (active) button.Show();
            else button.Hide();
            if (!active) return;

            if (me.CanMove) Timer = Math.Max(0f, Timer - Time.deltaTime);
            button.SetCoolDown(Timer, MinerOptions.Cooldown);
            button.SetUsesRemaining(VentsLeft(me.PlayerId));
            if (CanDig(me)) button.SetEnabled();
            else button.SetDisabled();
            if (Input.GetKeyDown(KeyCode.F)) Dig();
        }

        /// <summary>Bouton « Creuser » : copie du bouton de capacité du jeu (libre pour un Imposteur simple), avec l'icône du conduit.</summary>
        static AbilityButton Button(HudManager hud)
        {
            if (_button != null && _button) return _button;
            if (!hud.AbilityButton) return null;

            _button = UnityEngine.Object.Instantiate(hud.AbilityButton, hud.AbilityButton.transform.parent);
            _button.name = "MinerButton";
            if (hud.ImpostorVentButton && hud.ImpostorVentButton.graphic) _button.graphic.sprite = hud.ImpostorVentButton.graphic.sprite;
            _button.OverrideText("CREUSER");
            _button.gameObject.SetActive(true);
            return _button;
        }
    }
}
