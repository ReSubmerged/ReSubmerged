using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using Reactor.Utilities.Extensions;
using Submerged.Resources;
using UnityEngine;

namespace JesterRole
{
    /// <summary>
    /// Menu des rôles du lobby pour le Jester : une ligne nombre + % sous les rôles Coéquipier (onglet « Tous »),
    /// un onglet rose « Jester » avec l'info du rôle, et une ligne dans le récapitulatif des autres joueurs.
    /// </summary>
    [HarmonyPatch]
    public static class JesterOptionsMenu
    {
        const string Name = "Jester";
        const string Info = "Faites-vous éjecter lors d'un vote pour gagner seul.\nVos tâches ne comptent pas pour les Coéquipiers.";

        static readonly Color HeaderTextColor = Color.white;

        static RoleOptionSetting _row;
        static RoleSettingsTabButton _tab;
        static RoleBehaviour _lastCrewRole;
        static bool _inserting;
        static bool _opening;
        static Color? _headerSpriteColor, _headerTextColor;
        static Sprite _icon, _screenshot, _background;

        static bool IsHost => AmongUsClient.Instance && AmongUsClient.Instance.AmHost;
        static bool IsJesterRow(RoleOptionSetting setting) => _row != null && _row && setting != null && setting.Pointer == _row.Pointer;

        static void LogError(string what, Exception e) => Reactor.Utilities.Logger<Submerged.SubmergedPlugin>.Error($"[Jester] {what} : {e}");

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

            //Dernière ligne de rôle Coéquipier
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
            var offset = RolesSettingsMenu.Y_OFFSET;
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.localPosition.y < y - 0.01f) child.localPosition += new Vector3(0f, offset, 0f);
            }

            var row = UnityEngine.Object.Instantiate(menu.roleOptionSettingOrigin, Vector3.zero, Quaternion.identity, parent);
            row.name = "JesterRoleOption";
            row.transform.localPosition = new Vector3(lastCrew.transform.localPosition.x, y + offset, lastCrew.transform.localPosition.z);
            row.SetRole(GameOptionsManager.Instance.CurrentGameOptions.RoleOptions, crewmate, 20);
            row.SetClickMask(menu.ButtonClickMask);
            row.OnValueChanged = new Action<OptionBehaviour>(_ => { });
            _row = row;

            menu.roleChances.Insert(menu.roleChances.IndexOf(lastCrew) + 1, row);
            if (menu.QuotaTabSelectables != null)
                foreach (var selectable in row.ControllerSelectable) menu.QuotaTabSelectables.Add(selectable);

            if (IsHost) JesterOptions.LoadLocal();
            Refresh();

            menu.scrollBar.CalculateAndSetYBounds(menu.roleChances.Count + 3, 1f, 6f, 0.43f);
        }

        /// <summary>Met à jour les textes et les boutons de la ligne Jester.</summary>
        static void Refresh()
        {
            if (_row == null || !_row) return;
            _row.roleMaxCount = JesterOptions.Count;
            _row.roleChance = JesterOptions.Chance;
            _row.titleText.text = Name;
            _row.countText.text = JesterOptions.Count.ToString();
            _row.chanceText.text = JesterOptions.Chance + "%";
            _row.labelSprite.color = Jester.Color;

            _row.CountMinusBtn.SetInteractable(IsHost && JesterOptions.Count > 0);
            _row.CountPlusBtn.SetInteractable(IsHost && JesterOptions.Count < JesterOptions.MaxCount);
            _row.ChanceMinusBtn.SetInteractable(IsHost && JesterOptions.Chance > 0);
            _row.ChancePlusBtn.SetInteractable(IsHost && JesterOptions.Chance < 100);
        }

        static void Change(int count, int chance)
        {
            if (!IsHost) return;
            if (count == JesterOptions.Count && chance == JesterOptions.Chance) return;
            JesterOptions.SetQuota(count, chance);
            NotifyChange(true);
            Jester.SyncSettings();
        }

        /// <summary>Petite notification en haut à gauche, comme pour les rôles du jeu.</summary>
        public static void NotifyChange(bool playSound)
        {
            Refresh();
            try
            {
                if (HudManager.Instance && HudManager.Instance.Notifier)
                    HudManager.Instance.Notifier.AddRoleSettingsChangeMessage(JesterOptions.JesterName,
                        JesterOptions.Count, JesterOptions.Chance, RoleTeamTypes.Crewmate, playSound);
            }
            catch (Exception e)
            {
                Reactor.Utilities.Logger<Submerged.SubmergedPlugin>.Warning($"[Jester] Notification : {e.Message}");
            }
        }

        //Les boutons + / - de la ligne Jester modifient nos réglages, pas ceux du Coéquipier
        [HarmonyPatch(typeof(RoleOptionSetting), nameof(RoleOptionSetting.IncreaseCount)), HarmonyPrefix]
        static bool IncreaseCount(RoleOptionSetting __instance)
        {
            if (!IsJesterRow(__instance)) return true;
            Change(Math.Min(JesterOptions.Count + 1, JesterOptions.MaxCount), JesterOptions.Chance == 0 ? 100 : JesterOptions.Chance);
            return false;
        }

        [HarmonyPatch(typeof(RoleOptionSetting), nameof(RoleOptionSetting.DecreaseCount)), HarmonyPrefix]
        static bool DecreaseCount(RoleOptionSetting __instance)
        {
            if (!IsJesterRow(__instance)) return true;
            var count = Math.Max(JesterOptions.Count - 1, 0);
            Change(count, count == 0 ? 0 : JesterOptions.Chance);
            return false;
        }

        [HarmonyPatch(typeof(RoleOptionSetting), nameof(RoleOptionSetting.IncreaseChance)), HarmonyPrefix]
        static bool IncreaseChance(RoleOptionSetting __instance)
        {
            if (!IsJesterRow(__instance)) return true;
            Change(Math.Max(JesterOptions.Count, 1), Math.Min(JesterOptions.Chance + 10, 100));
            return false;
        }

        [HarmonyPatch(typeof(RoleOptionSetting), nameof(RoleOptionSetting.DecreaseChance)), HarmonyPrefix]
        static bool DecreaseChance(RoleOptionSetting __instance)
        {
            if (!IsJesterRow(__instance)) return true;
            var chance = Math.Max(JesterOptions.Chance - 10, 0);
            Change(chance == 0 ? 0 : JesterOptions.Count, chance);
            return false;
        }

        [HarmonyPatch(typeof(RoleOptionSetting), nameof(RoleOptionSetting.UpdateValuesAndText)), HarmonyPrefix]
        static bool UpdateValuesAndText(RoleOptionSetting __instance)
        {
            if (!IsJesterRow(__instance)) return true;
            Refresh();
            return false;
        }

        #endregion

        #region Onglet « Jester »

        //Le jeu crée les onglets Coéquipier puis Imposteur : on glisse le Jester juste avant le premier onglet Imposteur.
        //Priorité entre celle du Shérif (normale) et du Mineur (basse) : l'ordre est Shérif, Jester, Mineur.
        [HarmonyPatch(typeof(RolesSettingsMenu), nameof(RolesSettingsMenu.AddRoleTab)), HarmonyPrefix, HarmonyPriority(Priority.LowerThanNormal)]
        static void AddRoleTabPrefix(RolesSettingsMenu __instance, RoleBehaviour role, ref float tabXPos)
        {
            if (_inserting || role == null || role.TeamType == RoleTeamTypes.Crewmate) return;
            if (_tab != null && _tab && _tab.transform.IsChildOf(__instance.transform)) return; //déjà ajouté

            try
            {
                AddTab(__instance, ref tabXPos);
            }
            catch (Exception e)
            {
                LogError("Onglet du menu des rôles", e);
            }
        }

        [HarmonyPatch(typeof(RolesSettingsMenu), nameof(RolesSettingsMenu.AddRoleTab)), HarmonyPostfix]
        static void AddRoleTabPostfix(RoleBehaviour role)
        {
            if (!_inserting && role != null && role.TeamType == RoleTeamTypes.Crewmate) _lastCrewRole = role;
        }

        static void AddTab(RolesSettingsMenu menu, ref float tabXPos)
        {
            //On laisse le jeu créer un vrai onglet Coéquipier (position, masque, sélection), puis on le transforme en Jester
            var borrowed = _lastCrewRole != null && _lastCrewRole ? _lastCrewRole : FindRole(RoleTypes.Crewmate);
            if (borrowed == null) return;

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
            tab.name = "JesterRoleTab";
            _tab = tab;

            //Le fond de l'onglet est une image déjà bleue : on en fait une copie rose
            var background = PinkBackground(tab.background.sprite);
            if (background != null) tab.background.sprite = background;
            var icon = Icon(tab.icon.sprite);
            if (icon != null) tab.icon.sprite = icon;

            button.OnClick.RemoveAllListeners();
            button.OnClick.AddListener((Action)(() => OpenTab(menu, button, borrowed)));
        }

        /// <summary>Ouvre l'onglet Jester : même page que les rôles du jeu, avec notre info (le Jester n'a pas de réglage en plus).</summary>
        static void OpenTab(RolesSettingsMenu menu, PassiveButton button, RoleBehaviour borrowed)
        {
            try
            {
                _opening = true;
                try
                {
                    menu.ChangeTab(borrowed, button);
                }
                finally
                {
                    _opening = false;
                }

                //Retire les réglages du rôle emprunté
                foreach (var child in menu.advancedSettingChildren)
                    if (child != null && child) UnityEngine.Object.Destroy(child.gameObject);
                menu.advancedSettingChildren.Clear();

                var name = borrowed.NiceName;
                menu.roleTitleText.text = ReplaceName(menu.roleTitleText.text, name);
                menu.roleHeaderText.text = ReplaceName(menu.roleHeaderText.text, name);
                menu.roleDescriptionText.text = Info;

                _headerSpriteColor ??= menu.roleHeaderSprite.color;
                _headerTextColor ??= menu.roleHeaderText.color;
                menu.roleHeaderSprite.color = Jester.Color;
                menu.roleHeaderText.color = HeaderTextColor;

                var screenshot = Screenshot(menu.roleScreenshot.sprite);
                if (screenshot != null) menu.roleScreenshot.sprite = screenshot;

                menu.scrollBar.CalculateAndSetYBounds(3, 1f, 6f, 0.45f);
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

        //Les autres onglets ne doivent pas garder le bandeau rose
        [HarmonyPatch(typeof(RolesSettingsMenu), nameof(RolesSettingsMenu.ChangeTab)), HarmonyPostfix]
        static void ChangeTab(RolesSettingsMenu __instance)
        {
            if (_opening) return;
            if (_headerSpriteColor.HasValue && __instance.roleHeaderSprite.color == Jester.Color) __instance.roleHeaderSprite.color = _headerSpriteColor.Value;
            if (_headerTextColor.HasValue && __instance.roleHeaderText.color == HeaderTextColor) __instance.roleHeaderText.color = _headerTextColor.Value;
        }

        /// <summary>Chapeau de Jester blanc, à la taille de l'icône du jeu.</summary>
        static Sprite Icon(Sprite original)
        {
            if (_icon != null && _icon) return _icon;
            var tex = ResourceManager.GetTexture("JesterIcon.png");
            if (tex == null) return null;
            var size = original != null ? Mathf.Max(original.bounds.size.x, original.bounds.size.y) : 1f;
            _icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width / size);
            _icon.DontUnload();
            return _icon;
        }

        /// <summary>Capture du Jester en jeu (si l'image existe), recadrée au format de l'image du jeu.</summary>
        static Sprite Screenshot(Sprite original)
        {
            if (_screenshot != null && _screenshot) return _screenshot;
            var tex = ResourceManager.GetTexture("JesterScreenshot.png");
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

        /// <summary>Copie l'image de l'onglet en remplaçant le bleu par le rose du Jester (dégradé et transparence gardés).</summary>
        static Sprite PinkBackground(Sprite original)
        {
            if (_background != null && _background) return _background;
            if (original == null) return null;

            //La texture du jeu n'est pas lisible : on la recopie via une RenderTexture
            var source = original.texture;
            var area = original.textureRect;
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D((int) area.width, (int) area.height, TextureFormat.ARGB32, false);
            tex.ReadPixels(new Rect(area.x, area.y, area.width, area.height), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            var pixels = tex.GetPixels();
            var brightest = 0.01f;
            for (var i = 0; i < pixels.Length; i++)
                if (pixels[i].a > 0.1f) brightest = Mathf.Max(brightest, pixels[i].maxColorComponent);
            for (var i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                var shade = Mathf.Clamp01(pixel.maxColorComponent / brightest);
                pixels[i] = new Color(Jester.Color.r * shade, Jester.Color.g * shade, Jester.Color.b * shade, pixel.a);
            }
            tex.SetPixels(pixels);
            tex.Apply();

            var pivot = new Vector2(original.pivot.x / original.rect.width, original.pivot.y / original.rect.height);
            _background = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, original.pixelsPerUnit, 0, SpriteMeshType.FullRect, original.border);
            _background.DontUnload();
            return _background;
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
            var enabled = JesterOptions.Count > 0 && JesterOptions.Chance > 0;
            panel.SetInfo(Name, JesterOptions.Count, JesterOptions.Chance,
                LobbyViewSettingsPane.MASK_LAYER, Jester.Color, null, true, !enabled);
            pane.settingsInfo.Add(panel.gameObject);

            var bounds = pane.scrollBar.GetYBounds();
            pane.scrollBar.SetYBoundsMax(bounds.max + spacing);
        }

        #endregion
    }
}
