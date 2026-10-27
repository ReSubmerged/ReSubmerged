using System;
using System.Collections.Generic;
using System.Globalization;
using AmongUs.GameOptions;
using HarmonyLib;
using Reactor.Utilities.Extensions;
using Submerged.Resources;
using UnityEngine;

namespace MinerRole
{
    /// <summary>
    /// Menu des rôles du lobby pour le Mineur : une ligne nombre + % sous les rôles Imposteur (onglet « Tous »),
    /// un onglet rouge « Mineur » (info + temps de recharge + nombre max de conduits),
    /// et une ligne dans le récapitulatif des réglages que voient les autres joueurs.
    /// </summary>
    [HarmonyPatch]
    public static class MinerOptionsMenu
    {
        const string Name = "Mineur";
        const string Info = "Creusez des conduits n'importe où sur la carte.\nVos conduits sont reliés entre eux.";

        static RoleOptionSetting _row;
        static RoleSettingsTabButton _tab;
        static bool _inserting;
        static NumberOption _cooldownOption, _maxVentsOption;
        static FloatGameSetting _cooldownSetting, _maxVentsSetting;
        static Sprite _icon, _screenshot;

        static bool IsHost => AmongUsClient.Instance && AmongUsClient.Instance.AmHost;
        static bool IsMinerRow(RoleOptionSetting setting) => _row != null && _row && setting != null && setting.Pointer == _row.Pointer;
        static bool Is(NumberOption option, NumberOption ours) => ours != null && ours && option != null && option.Pointer == ours.Pointer;
        static bool IsOurOption(NumberOption option) => Is(option, _cooldownOption) || Is(option, _maxVentsOption);

        static void LogError(string what, Exception e) => Reactor.Utilities.Logger<Submerged.SubmergedPlugin>.Error($"[Mineur] {what} : {e}");

        static RoleBehaviour FindRole(RoleTypes type)
        {
            foreach (var role in RoleManager.Instance.AllRoles)
                if (role != null && role.Role == type) return role;
            return null;
        }

        #region Ligne nombre + % (onglet « Tous »)

        [HarmonyPatch(typeof(RolesSettingsMenu), nameof(RolesSettingsMenu.SetQuotaTab)), HarmonyPostfix]
        static void SetQuotaTab(RolesSettingsMenu __instance)
        {
            try
            {
                AddRow(__instance);
            }
            catch (Exception e)
            {
                LogError("Ligne du menu des rôles", e);
            }
        }

        static void AddRow(RolesSettingsMenu menu)
        {
            var parent = menu.RoleChancesSettings.transform;
            if (_row != null && _row && _row.transform.parent == parent) return; //déjà ajoutée

            //Dernière ligne de rôle Imposteur
            RoleOptionSetting lastImpostor = null;
            foreach (var setting in menu.roleChances)
            {
                if (setting == null || setting.Role == null || setting.Role.TeamType != RoleTeamTypes.Impostor) continue;
                if (lastImpostor == null || setting.transform.localPosition.y < lastImpostor.transform.localPosition.y) lastImpostor = setting;
            }
            if (lastImpostor == null) return;

            var impostor = FindRole(RoleTypes.Impostor);
            if (impostor == null) return;

            var y = lastImpostor.transform.localPosition.y;
            var offset = RolesSettingsMenu.Y_OFFSET;
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.localPosition.y < y - 0.01f) child.localPosition += new Vector3(0f, offset, 0f);
            }

            var row = UnityEngine.Object.Instantiate(menu.roleOptionSettingOrigin, Vector3.zero, Quaternion.identity, parent);
            row.name = "MinerRoleOption";
            row.transform.localPosition = new Vector3(lastImpostor.transform.localPosition.x, y + offset, lastImpostor.transform.localPosition.z);
            row.SetRole(GameOptionsManager.Instance.CurrentGameOptions.RoleOptions, impostor, 20);
            row.SetClickMask(menu.ButtonClickMask);
            row.OnValueChanged = new Action<OptionBehaviour>(_ => { });
            _row = row;

            menu.roleChances.Insert(menu.roleChances.IndexOf(lastImpostor) + 1, row);
            if (menu.QuotaTabSelectables != null)
                foreach (var selectable in row.ControllerSelectable) menu.QuotaTabSelectables.Add(selectable);

            if (IsHost) MinerOptions.LoadLocal();
            Refresh();

            menu.scrollBar.CalculateAndSetYBounds(menu.roleChances.Count + 3, 1f, 6f, 0.43f);
        }

        /// <summary>Met à jour les textes et les boutons de la ligne Mineur.</summary>
        static void Refresh()
        {
            if (_row == null || !_row) return;
            _row.roleMaxCount = MinerOptions.Count;
            _row.roleChance = MinerOptions.Chance;
            _row.titleText.text = Name;
            _row.countText.text = MinerOptions.Count.ToString();
            _row.chanceText.text = MinerOptions.Chance + "%";
            _row.labelSprite.color = Miner.Color;

            _row.CountMinusBtn.SetInteractable(IsHost && MinerOptions.Count > 0);
            _row.CountPlusBtn.SetInteractable(IsHost && MinerOptions.Count < MinerOptions.MaxCount);
            _row.ChanceMinusBtn.SetInteractable(IsHost && MinerOptions.Chance > 0);
            _row.ChancePlusBtn.SetInteractable(IsHost && MinerOptions.Chance < 100);
        }

        static void Change(int count, int chance)
        {
            if (!IsHost) return;
            if (count == MinerOptions.Count && chance == MinerOptions.Chance) return;
            MinerOptions.SetQuota(count, chance);
            NotifyChange(true);
            Miner.SyncSettings();
        }

        /// <summary>Petite notification en haut à gauche, comme pour les rôles du jeu.</summary>
        public static void NotifyChange(bool playSound)
        {
            Refresh();
            try
            {
                if (HudManager.Instance && HudManager.Instance.Notifier)
                    HudManager.Instance.Notifier.AddRoleSettingsChangeMessage(MinerOptions.MinerName,
                        MinerOptions.Count, MinerOptions.Chance, RoleTeamTypes.Impostor, playSound);
            }
            catch (Exception e)
            {
                Reactor.Utilities.Logger<Submerged.SubmergedPlugin>.Warning($"[Mineur] Notification : {e.Message}");
            }
        }

        //Les boutons + / - de la ligne Mineur modifient nos réglages, pas ceux de l'Imposteur
        [HarmonyPatch(typeof(RoleOptionSetting), nameof(RoleOptionSetting.IncreaseCount)), HarmonyPrefix]
        static bool IncreaseCount(RoleOptionSetting __instance)
        {
            if (!IsMinerRow(__instance)) return true;
            Change(Math.Min(MinerOptions.Count + 1, MinerOptions.MaxCount), MinerOptions.Chance == 0 ? 100 : MinerOptions.Chance);
            return false;
        }

        [HarmonyPatch(typeof(RoleOptionSetting), nameof(RoleOptionSetting.DecreaseCount)), HarmonyPrefix]
        static bool DecreaseCount(RoleOptionSetting __instance)
        {
            if (!IsMinerRow(__instance)) return true;
            var count = Math.Max(MinerOptions.Count - 1, 0);
            Change(count, count == 0 ? 0 : MinerOptions.Chance);
            return false;
        }

        [HarmonyPatch(typeof(RoleOptionSetting), nameof(RoleOptionSetting.IncreaseChance)), HarmonyPrefix]
        static bool IncreaseChance(RoleOptionSetting __instance)
        {
            if (!IsMinerRow(__instance)) return true;
            Change(Math.Max(MinerOptions.Count, 1), Math.Min(MinerOptions.Chance + 10, 100));
            return false;
        }

        [HarmonyPatch(typeof(RoleOptionSetting), nameof(RoleOptionSetting.DecreaseChance)), HarmonyPrefix]
        static bool DecreaseChance(RoleOptionSetting __instance)
        {
            if (!IsMinerRow(__instance)) return true;
            var chance = Math.Max(MinerOptions.Chance - 10, 0);
            Change(chance == 0 ? 0 : MinerOptions.Count, chance);
            return false;
        }

        [HarmonyPatch(typeof(RoleOptionSetting), nameof(RoleOptionSetting.UpdateValuesAndText)), HarmonyPrefix]
        static bool UpdateValuesAndText(RoleOptionSetting __instance)
        {
            if (!IsMinerRow(__instance)) return true;
            Refresh();
            return false;
        }

        #endregion

        #region Onglet « Mineur »

        //Le jeu crée les onglets Coéquipier puis Imposteur : on glisse le Mineur juste avant le premier onglet Imposteur.
        //Priorité basse : passe après l'onglet du Shérif, qui se place au même endroit.
        [HarmonyPatch(typeof(RolesSettingsMenu), nameof(RolesSettingsMenu.AddRoleTab)), HarmonyPrefix, HarmonyPriority(Priority.Low)]
        static void AddRoleTabPrefix(RolesSettingsMenu __instance, RoleBehaviour role, ref float tabXPos)
        {
            if (_inserting || role == null || role.TeamType == RoleTeamTypes.Crewmate) return;
            if (_tab != null && _tab && _tab.transform.IsChildOf(__instance.transform)) return; //déjà ajouté

            try
            {
                AddTab(__instance, role, ref tabXPos);
            }
            catch (Exception e)
            {
                LogError("Onglet du menu des rôles", e);
            }
        }

        static void AddTab(RolesSettingsMenu menu, RoleBehaviour borrowed, ref float tabXPos)
        {
            //On laisse le jeu créer un vrai onglet Imposteur (rouge, position, sélection), puis on le transforme en Mineur
            _inserting = true;
            try
            {
                menu.AddRoleTab(borrowed, ref tabXPos);
            }
            finally
            {
                _inserting = false;
            }

            var button = menu.roleTabs[menu.roleTabs.Count - 1];
            var tab = button.GetComponentInParent<RoleSettingsTabButton>();
            if (tab == null) return;
            tab.name = "MinerRoleTab";
            _tab = tab;

            var icon = Icon(tab.icon.sprite);
            if (icon != null) tab.icon.sprite = icon;

            button.OnClick.RemoveAllListeners();
            button.OnClick.AddListener((Action)(() => OpenTab(menu, button, borrowed)));
        }

        /// <summary>Ouvre l'onglet Mineur : même page que les rôles du jeu, avec notre info et nos réglages.</summary>
        static void OpenTab(RolesSettingsMenu menu, PassiveButton button, RoleBehaviour borrowed)
        {
            try
            {
                menu.ChangeTab(borrowed, button);

                //Retire les réglages du rôle emprunté
                foreach (var child in menu.advancedSettingChildren)
                    if (child != null && child) UnityEngine.Object.Destroy(child.gameObject);
                menu.advancedSettingChildren.Clear();

                var name = borrowed.NiceName;
                menu.roleTitleText.text = ReplaceName(menu.roleTitleText.text, name);
                menu.roleHeaderText.text = ReplaceName(menu.roleHeaderText.text, name);
                menu.roleDescriptionText.text = Info;

                var screenshot = Screenshot(menu.roleScreenshot.sprite);
                if (screenshot != null) menu.roleScreenshot.sprite = screenshot;

                _cooldownOption = AddOption(menu, 0, ref _cooldownSetting, MinerOptions.Cooldown, MinerOptions.CooldownStep, MinerOptions.MinCooldown, MinerOptions.MaxCooldown, true);
                _maxVentsOption = AddOption(menu, 1, ref _maxVentsSetting, MinerOptions.MaxVentCount, 1f, MinerOptions.MinVents, MinerOptions.MaxVents, false);
                RefreshOptions();
                menu.scrollBar.CalculateAndSetYBounds(menu.advancedSettingChildren.Count + 3, 1f, 6f, 0.45f);
            }
            catch (Exception e)
            {
                LogError("Ouverture de l'onglet", e);
            }
        }

        static string ReplaceName(string text, string name)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(name)) return text;
            return text.Replace(name, Name).Replace(name.ToUpperInvariant(), Name.ToUpperInvariant());
        }

        /// <summary>Pioche blanche, à la taille de l'icône du jeu.</summary>
        static Sprite Icon(Sprite original)
        {
            if (_icon != null && _icon) return _icon;
            var tex = ResourceManager.GetTexture("MinerIcon.png");
            if (tex == null) return null;
            var size = original != null ? Mathf.Max(original.bounds.size.x, original.bounds.size.y) : 1f;
            _icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width / size);
            _icon.DontUnload();
            return _icon;
        }

        /// <summary>Capture du Mineur en jeu (si l'image existe), recadrée au format de l'image du jeu.</summary>
        static Sprite Screenshot(Sprite original)
        {
            if (_screenshot != null && _screenshot) return _screenshot;
            var tex = ResourceManager.GetTexture("MinerScreenshot.png");
            if (tex == null) return null;

            float width = tex.width, height = tex.height;
            if (original != null)
            {
                var aspect = original.rect.width / original.rect.height;
                if (width / height > aspect) width = height * aspect;
                else height = width / aspect;
            }
            var rect = new Rect((tex.width - width) / 2f, (tex.height - height) / 2f, width, height);
            var ppu = original != null ? width / original.bounds.size.x : 100f;
            _screenshot = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), ppu);
            _screenshot.DontUnload();
            return _screenshot;
        }

        #endregion

        #region Réglages de l'onglet

        static NumberOption AddOption(RolesSettingsMenu menu, int index, ref FloatGameSetting data, float value, float step, float min, float max, bool seconds)
        {
            var option = UnityEngine.Object.Instantiate(menu.numberOptionOrigin, Vector3.zero, Quaternion.identity, menu.AdvancedRolesSettings.transform);
            option.transform.localPosition = new Vector3(RolesSettingsMenu.X_ADVANCED_START,
                RolesSettingsMenu.Y_ADVANCED_START + index * RolesSettingsMenu.Y_ADVANCED_OFFSET, -2f);
            //Enregistré avant SetUpFromData, pour que nos patchs s'appliquent dès l'initialisation
            if (index == 0) _cooldownOption = option;
            else _maxVentsOption = option;
            option.SetClickMask(menu.ButtonClickMask);
            option.SetUpFromData(Data(ref data, value, step, min, max, seconds), RolesSettingsMenu.MASK_LAYER);
            option.OnValueChanged = new Action<OptionBehaviour>(_ => { });
            menu.advancedSettingChildren.Add(option);
            return option;
        }

        static FloatGameSetting Data(ref FloatGameSetting data, float value, float step, float min, float max, bool seconds)
        {
            if (data != null && data) return data;
            data = ScriptableObject.CreateInstance<FloatGameSetting>();
            data.hideFlags = HideFlags.HideAndDontSave;
            data.Title = StringNames.None;
            data.Type = OptionTypes.Float;
            data.Value = value;
            data.Increment = step;
            data.ValidRange = new FloatRange(min, max);
            data.FormatString = "0.0#";
            data.SuffixType = seconds ? NumberSuffixes.Seconds : NumberSuffixes.None;
            data.ZeroIsInfinity = false;
            return data;
        }

        /// <summary>Met à jour les textes et les boutons des réglages (le jeu n'y touche pas, cf. patchs ci-dessous).</summary>
        static void RefreshOptions()
        {
            SetOption(_cooldownOption, "Temps de recharge pour creuser", MinerOptions.Cooldown,
                MinerOptions.Cooldown.ToString("0.#", CultureInfo.InvariantCulture) + "s", MinerOptions.MinCooldown, MinerOptions.MaxCooldown);
            SetOption(_maxVentsOption, "Nombre max de conduits", MinerOptions.MaxVentCount,
                MinerOptions.MaxVentCount.ToString(), MinerOptions.MinVents, MinerOptions.MaxVents);
        }

        static void SetOption(NumberOption option, string title, float value, string text, float min, float max)
        {
            if (option == null || !option) return;
            option.Value = value;
            option.oldValue = value;
            option.TitleText.text = title;
            option.ValueText.text = text;
            option.MinusBtn.SetInteractable(IsHost && value > min);
            option.PlusBtn.SetInteractable(IsHost && value < max);
        }

        static void Step(NumberOption option, int direction)
        {
            if (!IsHost) return;
            if (Is(option, _cooldownOption)) MinerOptions.SetCooldown(MinerOptions.Cooldown + direction * MinerOptions.CooldownStep);
            else MinerOptions.SetMaxVents(MinerOptions.MaxVentCount + direction);
            RefreshOptions();
            Miner.SyncSettings();
        }

        //Nos réglages n'écrivent jamais dans les options du jeu (ils n'y ont pas d'option associée)
        [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.Increase)), HarmonyPrefix]
        static bool Increase(NumberOption __instance)
        {
            if (!IsOurOption(__instance)) return true;
            Step(__instance, 1);
            return false;
        }

        [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.Decrease)), HarmonyPrefix]
        static bool Decrease(NumberOption __instance)
        {
            if (!IsOurOption(__instance)) return true;
            Step(__instance, -1);
            return false;
        }

        [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.Initialize)), HarmonyPrefix]
        static bool Initialize(NumberOption __instance) => Skip(__instance);

        [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.UpdateValue)), HarmonyPrefix]
        static bool UpdateValue(NumberOption __instance) => Skip(__instance);

        [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.AdjustButtonsActiveState)), HarmonyPrefix]
        static bool AdjustButtonsActiveState(NumberOption __instance) => Skip(__instance);

        [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.FixedUpdate)), HarmonyPrefix]
        static bool FixedUpdate(NumberOption __instance) => !IsOurOption(__instance);

        static bool Skip(NumberOption option)
        {
            if (!IsOurOption(option)) return true;
            RefreshOptions();
            return false;
        }

        #endregion

        #region Récapitulatif des réglages (bouton « Voir les réglages » des autres joueurs)

        [HarmonyPatch(typeof(LobbyViewSettingsPane), nameof(LobbyViewSettingsPane.DrawRolesTab)), HarmonyPostfix]
        static void DrawRolesTab(LobbyViewSettingsPane __instance)
        {
            try
            {
                AddViewRow(__instance);
            }
            catch (Exception e)
            {
                LogError("Récapitulatif des rôles", e);
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

            Transform lastImpostor = null;
            foreach (var row in rows)
                if (row.localPosition.y < impostorHeaderY && (lastImpostor == null || row.localPosition.y < lastImpostor.localPosition.y)) lastImpostor = row;
            if (lastImpostor == null) return;

            var y = lastImpostor.localPosition.y;
            var spacing = LobbyViewSettingsPane.SPACING_Y_ROLES;
            for (var i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i);
                if (child.localPosition.y < y - 0.01f) child.localPosition -= new Vector3(0f, spacing, 0f);
            }

            var panel = UnityEngine.Object.Instantiate(pane.infoPanelRoleOrigin, container);
            panel.transform.localScale = lastImpostor.localScale;
            panel.transform.localPosition = new Vector3(lastImpostor.localPosition.x, y - spacing, lastImpostor.localPosition.z);
            var enabled = MinerOptions.Count > 0 && MinerOptions.Chance > 0;
            panel.SetInfo(Name, MinerOptions.Count, MinerOptions.Chance,
                LobbyViewSettingsPane.MASK_LAYER, Miner.Color, null, false, !enabled);
            pane.settingsInfo.Add(panel.gameObject);

            var bounds = pane.scrollBar.GetYBounds();
            pane.scrollBar.SetYBoundsMax(bounds.max + spacing);
        }

        #endregion
    }
}
