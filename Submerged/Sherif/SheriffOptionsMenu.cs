using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using UnityEngine;

namespace SherriffRole
{
    /// <summary>
    /// Ajoute une ligne « Shérif » (nombre + %) dans le menu des rôles du lobby, juste après le dernier rôle Coéquipier,
    /// et dans le récapitulatif des réglages que voient les autres joueurs.
    /// </summary>
    [HarmonyPatch]
    public static class SheriffOptionsMenu
    {
        const string RowName = "SheriffRoleOption";

        static RoleOptionSetting _row;

        static bool IsSheriffRow(RoleOptionSetting setting) => _row != null && setting != null && setting.Pointer == _row.Pointer;

        //Menu de l'hôte : Réglages des rôles > Tous
        [HarmonyPatch(typeof(RolesSettingsMenu), nameof(RolesSettingsMenu.SetQuotaTab)), HarmonyPostfix]
        static void SetQuotaTab(RolesSettingsMenu __instance)
        {
            try
            {
                AddRow(__instance);
            }
            catch (Exception e)
            {
                Reactor.Utilities.Logger<Submerged.SubmergedPlugin>.Error($"[Shérif] Ligne du menu des rôles : {e}");
            }
        }

        static void AddRow(RolesSettingsMenu menu)
        {
            var parent = menu.RoleChancesSettings.transform;
            if (_row != null && _row && _row.transform.parent == parent) return; //déjà ajoutée

            //Dernière ligne de rôle Coéquipier (l'Influenceur / le dernier rôle de l'équipe)
            RoleOptionSetting lastCrew = null;
            foreach (var setting in menu.roleChances)
            {
                if (setting == null || setting.Role == null || setting.Role.TeamType != RoleTeamTypes.Crewmate) continue;
                if (lastCrew == null || setting.transform.localPosition.y < lastCrew.transform.localPosition.y) lastCrew = setting;
            }
            if (lastCrew == null) return;

            var crewmate = FindRole(RoleTypes.Crewmate);
            if (crewmate == null) return;

            var y = lastCrew.transform.localPosition.y;
            var offset = RolesSettingsMenu.Y_OFFSET; //-0.43

            //Décale tout ce qui est en dessous (titre et rôles Imposteur) d'une ligne
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.localPosition.y < y - 0.01f) child.localPosition += new Vector3(0f, offset, 0f);
            }

            var row = UnityEngine.Object.Instantiate(menu.roleOptionSettingOrigin, Vector3.zero, Quaternion.identity, parent);
            row.name = RowName;
            row.transform.localPosition = new Vector3(lastCrew.transform.localPosition.x, y + offset, lastCrew.transform.localPosition.z);
            row.SetRole(GameOptionsManager.Instance.CurrentGameOptions.RoleOptions, crewmate, 20);
            row.SetClickMask(menu.ButtonClickMask);
            row.OnValueChanged = new Action<OptionBehaviour>(_ => { });
            row.titleText.text = "Shérif";
            row.labelSprite.color = Sheriff.Color;
            _row = row;

            var index = menu.roleChances.IndexOf(lastCrew);
            menu.roleChances.Insert(index + 1, row);
            if (menu.QuotaTabSelectables != null)
                foreach (var selectable in row.ControllerSelectable) menu.QuotaTabSelectables.Add(selectable);

            if (AmongUsClient.Instance && AmongUsClient.Instance.AmHost) CustomGameOptions.LoadLocal();
            Refresh();

            menu.scrollBar.CalculateAndSetYBounds(menu.roleChances.Count + 3, 1f, 6f, 0.43f);
        }

        static RoleBehaviour FindRole(RoleTypes type)
        {
            foreach (var role in RoleManager.Instance.AllRoles)
                if (role != null && role.Role == type) return role;
            return null;
        }

        /// <summary>Met à jour les textes et les boutons de la ligne Shérif.</summary>
        static void Refresh()
        {
            if (_row == null || !_row) return;
            _row.roleMaxCount = CustomGameOptions.SheriffCount;
            _row.roleChance = CustomGameOptions.SheriffChance;
            _row.titleText.text = "Shérif";
            _row.countText.text = CustomGameOptions.SheriffCount.ToString();
            _row.chanceText.text = CustomGameOptions.SheriffChance + "%";
            _row.labelSprite.color = Sheriff.Color;

            var host = AmongUsClient.Instance && AmongUsClient.Instance.AmHost;
            _row.CountMinusBtn.SetInteractable(host && CustomGameOptions.SheriffCount > 0);
            _row.CountPlusBtn.SetInteractable(host && CustomGameOptions.SheriffCount < CustomGameOptions.MaxCount);
            _row.ChanceMinusBtn.SetInteractable(host && CustomGameOptions.SheriffChance > 0);
            _row.ChancePlusBtn.SetInteractable(host && CustomGameOptions.SheriffChance < 100);
        }

        static void Change(int count, int chance)
        {
            if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost) return;
            if (count == CustomGameOptions.SheriffCount && chance == CustomGameOptions.SheriffChance) return;
            CustomGameOptions.SetQuota(count, chance);
            Refresh();
            NotifyChange(true);
            Sheriff.SyncSettings();
        }

        /// <summary>Petite notification en haut à gauche, comme pour les rôles du jeu.</summary>
        public static void NotifyChange(bool playSound)
        {
            Refresh();
            try
            {
                if (HudManager.Instance && HudManager.Instance.Notifier)
                    HudManager.Instance.Notifier.AddRoleSettingsChangeMessage(CustomGameOptions.SheriffName,
                        CustomGameOptions.SheriffCount, CustomGameOptions.SheriffChance, RoleTeamTypes.Crewmate, playSound);
            }
            catch (Exception e)
            {
                Reactor.Utilities.Logger<Submerged.SubmergedPlugin>.Warning($"[Shérif] Notification : {e.Message}");
            }
        }

        //Les boutons + / - de la ligne Shérif modifient nos réglages, pas ceux du Coéquipier
        [HarmonyPatch(typeof(RoleOptionSetting), nameof(RoleOptionSetting.IncreaseCount)), HarmonyPrefix]
        static bool IncreaseCount(RoleOptionSetting __instance)
        {
            if (!IsSheriffRow(__instance)) return true;
            var count = Math.Min(CustomGameOptions.SheriffCount + 1, CustomGameOptions.MaxCount);
            Change(count, CustomGameOptions.SheriffChance == 0 ? 100 : CustomGameOptions.SheriffChance);
            return false;
        }

        [HarmonyPatch(typeof(RoleOptionSetting), nameof(RoleOptionSetting.DecreaseCount)), HarmonyPrefix]
        static bool DecreaseCount(RoleOptionSetting __instance)
        {
            if (!IsSheriffRow(__instance)) return true;
            var count = Math.Max(CustomGameOptions.SheriffCount - 1, 0);
            Change(count, count == 0 ? 0 : CustomGameOptions.SheriffChance);
            return false;
        }

        [HarmonyPatch(typeof(RoleOptionSetting), nameof(RoleOptionSetting.IncreaseChance)), HarmonyPrefix]
        static bool IncreaseChance(RoleOptionSetting __instance)
        {
            if (!IsSheriffRow(__instance)) return true;
            var chance = Math.Min(CustomGameOptions.SheriffChance + 10, 100);
            Change(Math.Max(CustomGameOptions.SheriffCount, 1), chance);
            return false;
        }

        [HarmonyPatch(typeof(RoleOptionSetting), nameof(RoleOptionSetting.DecreaseChance)), HarmonyPrefix]
        static bool DecreaseChance(RoleOptionSetting __instance)
        {
            if (!IsSheriffRow(__instance)) return true;
            var chance = Math.Max(CustomGameOptions.SheriffChance - 10, 0);
            Change(chance == 0 ? 0 : CustomGameOptions.SheriffCount, chance);
            return false;
        }

        [HarmonyPatch(typeof(RoleOptionSetting), nameof(RoleOptionSetting.UpdateValuesAndText)), HarmonyPrefix]
        static bool UpdateValuesAndText(RoleOptionSetting __instance)
        {
            if (!IsSheriffRow(__instance)) return true;
            Refresh();
            return false;
        }

        //Récapitulatif des réglages (bouton « Voir les réglages » des autres joueurs) : onglet Rôles
        [HarmonyPatch(typeof(LobbyViewSettingsPane), nameof(LobbyViewSettingsPane.DrawRolesTab)), HarmonyPostfix]
        static void DrawRolesTab(LobbyViewSettingsPane __instance)
        {
            try
            {
                AddViewRow(__instance);
            }
            catch (Exception e)
            {
                Reactor.Utilities.Logger<Submerged.SubmergedPlugin>.Error($"[Shérif] Récapitulatif des rôles : {e}");
            }
        }

        static void AddViewRow(LobbyViewSettingsPane pane)
        {
            var container = pane.settingsContainer;
            var rows = new List<Transform>();
            var headers = new List<Transform>();
            for (var i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i);
                if (!child.gameObject.activeSelf) continue;
                if (child.GetComponent<ViewSettingsInfoPanelRoleVariant>()) rows.Add(child);
                else if (child.GetComponent<CategoryHeaderMasked>()) headers.Add(child);
            }
            if (headers.Count < 2 || rows.Count == 0) return;

            //Les titres sont dans l'ordre : Rôles Coéquipier puis Rôles Imposteur
            headers.Sort((a, b) => b.localPosition.y.CompareTo(a.localPosition.y));
            var impostorHeaderY = headers[1].localPosition.y;

            Transform lastCrew = null;
            foreach (var row in rows)
                if (row.localPosition.y > impostorHeaderY && (lastCrew == null || row.localPosition.y < lastCrew.localPosition.y)) lastCrew = row;
            if (lastCrew == null) return;

            var y = lastCrew.localPosition.y;
            var spacing = LobbyViewSettingsPane.SPACING_Y_ROLES;
            for (var i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i);
                if (child.localPosition.y < y - 0.01f) child.localPosition -= new Vector3(0f, spacing, 0f);
            }

            var panel = UnityEngine.Object.Instantiate(pane.infoPanelRoleOrigin, container);
            panel.transform.localScale = lastCrew.localScale;
            panel.transform.localPosition = new Vector3(lastCrew.localPosition.x, y - spacing, lastCrew.localPosition.z);
            var enabled = CustomGameOptions.SheriffCount > 0 && CustomGameOptions.SheriffChance > 0;
            panel.SetInfo("Shérif", CustomGameOptions.SheriffCount, CustomGameOptions.SheriffChance,
                LobbyViewSettingsPane.MASK_LAYER, Sheriff.Color, null, true, !enabled);
            pane.settingsInfo.Add(panel.gameObject);

            var bounds = pane.scrollBar.GetYBounds();
            pane.scrollBar.SetYBoundsMax(bounds.max + spacing);
        }
    }
}
