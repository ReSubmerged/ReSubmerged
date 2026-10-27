using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using MinerRole;
using Reactor.Utilities.Extensions;
using SherriffRole;
using UnityEngine;

namespace FreeplayRoles
{
    /// <summary>
    /// Jeu libre : ajoute « Shérif » et « Mineur » dans le dossier des rôles du PC portable,
    /// à côté des fichiers des rôles du jeu, pour pouvoir les essayer sur les mannequins.
    /// </summary>
    [HarmonyPatch]
    public static class FreeplayRoles
    {
        enum Kind { Sheriff, Miner }

        static readonly Dictionary<IntPtr, Kind> Buttons = new();

        static bool TryGetKind(TaskAddButton button, out Kind kind)
        {
            kind = default;
            return button != null && Buttons.TryGetValue(button.Pointer, out kind);
        }

        static string NameOf(Kind kind) => kind == Kind.Sheriff ? "Shérif" : "Mineur";

        static bool IsLocal(Kind kind) => kind == Kind.Sheriff ? Sheriff.IsLocal : Miner.IsLocal;

        static RoleBehaviour FindRole(RoleTypes type)
        {
            foreach (var role in RoleManager.Instance.AllRoles)
                if (role != null && role.Role == type) return role;
            return null;
        }

        //Le dossier des rôles vient d'être affiché : on ajoute nos deux fichiers à la suite
        [HarmonyPatch(typeof(TaskAdderGame), nameof(TaskAdderGame.ShowFolder)), HarmonyPostfix]
        static void ShowFolder(TaskAdderGame __instance, TaskFolder taskFolder)
        {
            //Le jeu a détruit les anciens fichiers : on oublie les nôtres (leur adresse pourrait être réutilisée)
            Buttons.Clear();
            try
            {
                //PC ouvert avant l'apparition sur la carte : choisir un de nos rôles là bloquait l'apparition
                //(le joueur restait au point d'attente, hors de la carte). Nos fichiers n'y sont donc pas proposés.
                if (!HasSpawned()) return;
                if (taskFolder == null || taskFolder.RoleChildren == null || taskFolder.RoleChildren.Count == 0) return;
                AddButton(__instance, taskFolder, Kind.Sheriff, RoleTypes.Crewmate);
                AddButton(__instance, taskFolder, Kind.Miner, RoleTypes.Impostor);
            }
            catch (Exception e)
            {
                Reactor.Utilities.Logger<Submerged.SubmergedPlugin>.Error($"[Jeu libre] Rôles du PC portable : {e}");
            }
        }

        static bool _spawned;

        /// <summary>Le joueur local est apparu sur la carte (il n'est plus au point d'attente en (0, 0)).</summary>
        static bool HasSpawned()
        {
            var me = PlayerControl.LocalPlayer;
            if (me == null) return false;
            return _spawned || ((Vector2) me.transform.position).sqrMagnitude > 0.0001f;
        }

        //Début d'une partie de jeu libre : rien ne remet les rôles à zéro (pas de retour au lobby), on le fait ici
        [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Start)), HarmonyPostfix]
        static void ShipStatus_Start()
        {
            if (!AmongUsClient.Instance || AmongUsClient.Instance.NetworkMode != NetworkModes.FreePlay) return;
            _spawned = false;
            Sheriff.Clear();
            Miner.Clear();
        }

        [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.SpawnPlayer)), HarmonyPostfix]
        static void ShipStatus_SpawnPlayer([HarmonyArgument(0)] PlayerControl player)
        {
            if (player != null && player.AmOwner) _spawned = true;
        }

        static void AddButton(TaskAdderGame game, TaskFolder folder, Kind kind, RoleTypes baseRole)
        {
            var role = FindRole(baseRole);
            if (role == null || game.ActiveItems.Count == 0) return;

            //Position : juste après le dernier fichier affiché, comme le fait le jeu
            var last = game.ActiveItems[game.ActiveItems.Count - 1];
            var rowHeight = RowHeight(game);
            var x = last.localPosition.x + game.fileWidth;
            var y = last.localPosition.y;
            if (x > game.lineWidth)
            {
                x = 0f;
                y -= rowHeight;
            }

            //Copie du fichier « Coéquipier » / « Imposteur » déjà affiché : il a l'icône et la couleur d'équipe du jeu
            var source = FindRoleButton(game, baseRole);
            var button = UnityEngine.Object.Instantiate(source != null ? source : game.RoleButton);
            button.name = NameOf(kind) + "FreeplayRole";
            button.role = role; //le jeu donne le rôle de base (Coéquipier / Imposteur), on ajoute le nôtre par-dessus
            Buttons[button.Pointer] = kind;
            game.AddFileAsChild(folder, button, ref x, ref y, ref rowHeight);
            SetText(button, kind);
            SetIcon(button, kind);
        }

        static TaskAddButton FindRoleButton(TaskAdderGame game, RoleTypes type)
        {
            foreach (var item in game.ActiveItems)
            {
                var button = item ? item.GetComponent<TaskAddButton>() : null;
                if (button != null && button.role != null && button.role.Role == type && !Buttons.ContainsKey(button.Pointer)) return button;
            }
            return null;
        }

        /// <summary>Remplace l'icône du rôle de base par celle du Shérif / du Mineur, à la même taille.</summary>
        static void SetIcon(TaskAddButton button, Kind kind)
        {
            var role = button.role;
            if (role == null) return;
            var replaced = false;
            foreach (var renderer in button.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer == null || renderer.sprite == null) continue;
                if (button.Overlay != null && renderer.Pointer == button.Overlay.Pointer) continue;
                var sprite = renderer.sprite;
                if (Icons.TryGetValue(kind, out var mine) && mine && sprite.Pointer == mine.Pointer)
                {
                    replaced = true; //déjà remplacée
                    continue;
                }
                var isRoleIcon = (role.RoleIconWhite != null && sprite.Pointer == role.RoleIconWhite.Pointer)
                                 || (role.RoleIconSolid != null && sprite.Pointer == role.RoleIconSolid.Pointer);
                if (!isRoleIcon) continue;
                var icon = Icon(kind, sprite);
                if (icon == null) return;
                renderer.sprite = icon;
                replaced = true;
            }
            if (replaced || button.FileImage == null) return;

            //Le jeu n'affiche pas l'icône du rôle sur ce fichier : on pose la nôtre par-dessus l'image du fichier
            var holder = button.FileImage.transform.Find("RoleIcon");
            if (holder == null)
            {
                holder = new GameObject("RoleIcon").transform;
                holder.SetParent(button.FileImage.transform, false);
                holder.localPosition = new Vector3(0f, 0f, -0.01f);
                var added = holder.gameObject.AddComponent<SpriteRenderer>();
                added.sortingOrder = button.FileImage.sortingOrder + 1;
                added.maskInteraction = button.FileImage.maskInteraction;
                added.color = kind == Kind.Sheriff ? Sheriff.Color : Miner.Color;
            }
            var size = button.FileImage.sprite != null ? button.FileImage.sprite.bounds.size : Vector3.one;
            holder.GetComponent<SpriteRenderer>().sprite = Icon(kind, null, 0.6f * Mathf.Min(size.x, size.y));
        }

        static readonly Dictionary<Kind, Sprite> Icons = new();

        /// <summary>Icône (étoile de shérif / pioche), à la taille de <paramref name="like"/> ou de <paramref name="worldSize"/>.</summary>
        static Sprite Icon(Kind kind, Sprite like, float worldSize = 0f)
        {
            var size = like != null ? Mathf.Max(like.bounds.size.x, like.bounds.size.y) : worldSize;
            if (size <= 0f) size = 1f;
            if (Icons.TryGetValue(kind, out var cached) && cached && Mathf.Abs(Mathf.Max(cached.bounds.size.x, cached.bounds.size.y) - size) < 0.001f) return cached;

            var tex = Submerged.Resources.ResourceManager.GetTexture(kind == Kind.Sheriff ? "SheriffIcon.png" : "MinerIcon.png");
            if (tex == null) return null;
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width / size);
            sprite.DontUnload();
            Icons[kind] = sprite;
            return sprite;
        }

        /// <summary>Hauteur d'une ligne de fichiers, mesurée sur ceux déjà affichés.</summary>
        static float RowHeight(TaskAdderGame game)
        {
            float top = float.MinValue, below = float.MinValue;
            foreach (var item in game.ActiveItems) top = Mathf.Max(top, item.localPosition.y);
            foreach (var item in game.ActiveItems)
                if (item.localPosition.y < top - 0.01f) below = Mathf.Max(below, item.localPosition.y);
            return below > float.MinValue ? top - below : 1.5f;
        }

        static void SetText(TaskAddButton button, Kind kind)
        {
            if (button.Text == null || button.role == null) return;
            var text = button.Text.text;
            var baseName = button.role.NiceName;
            button.Text.text = !string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(baseName) && text.Contains(baseName)
                ? text.Replace(baseName, NameOf(kind))
                : NameOf(kind) + ".exe";
        }

        //Le jeu remplit le nom du fichier au démarrage du bouton : on remet le nôtre
        [HarmonyPatch(typeof(TaskAddButton), nameof(TaskAddButton.Start)), HarmonyPostfix]
        static void TaskAddButton_Start(TaskAddButton __instance)
        {
            if (!TryGetKind(__instance, out var kind)) return;
            SetText(__instance, kind);
            SetIcon(__instance, kind);
        }

        //Coche : sur notre fichier si on a ce rôle, et plus sur « Coéquipier » / « Imposteur » dans ce cas
        [HarmonyPatch(typeof(TaskAddButton), nameof(TaskAddButton.Update)), HarmonyPostfix]
        static void TaskAddButton_Update(TaskAddButton __instance)
        {
            if (__instance.role == null || __instance.Overlay == null) return;
            if (TryGetKind(__instance, out var kind))
            {
                __instance.Overlay.enabled = IsLocal(kind);
                return;
            }
            if ((Sheriff.IsLocal && __instance.role.Role == RoleTypes.Crewmate) || (Miner.IsLocal && __instance.role.Role == RoleTypes.Impostor))
                __instance.Overlay.enabled = false;
        }

        //Choix d'un rôle sur le PC : le jeu donne le rôle de base, puis on active (ou retire) Shérif et Mineur
        [HarmonyPatch(typeof(TaskAddButton), nameof(TaskAddButton.AddTask)), HarmonyPostfix]
        static void TaskAddButton_AddTask(TaskAddButton __instance)
        {
            if (__instance.role == null) return; //une tâche, pas un rôle
            var isOurs = TryGetKind(__instance, out var kind);
            Sheriff.SetFreeplay(isOurs && kind == Kind.Sheriff);
            Miner.SetFreeplay(isOurs && kind == Kind.Miner);
        }
    }
}
