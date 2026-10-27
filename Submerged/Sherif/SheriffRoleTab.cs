using System;
using System.Globalization;
using AmongUs.GameOptions;
using HarmonyLib;
using Reactor.Utilities.Extensions;
using Submerged.Resources;
using UnityEngine;

namespace SherriffRole
{
    /// <summary>
    /// Ajoute un onglet jaune « Shérif » dans la rangée d'onglets du menu des rôles du lobby (après le dernier rôle Coéquipier),
    /// avec l'info du rôle et le réglage du temps de recharge du tir.
    /// </summary>
    [HarmonyPatch]
    public static class SheriffRoleTab
    {
        const string TabName = "SheriffRoleTab";
        const string Info = "Tirez sur les Imposteurs.\nSi vous tirez sur un Coéquipier, c'est vous qui mourez.";
        const float CooldownStep = 2.5f;

        static readonly Color HeaderTextColor = new(0.35f, 0.25f, 0f, 1f);

        static RoleSettingsTabButton _tab;
        static RoleBehaviour _lastCrewRole;
        static bool _inserting;
        static bool _opening;
        static NumberOption _cooldownOption;
        static FloatGameSetting _cooldownSetting;
        static Color? _headerSpriteColor, _headerTextColor;
        static Sprite _icon, _screenshot, _background;

        static bool IsSheriffTab(PassiveButton button) => _tab != null && _tab && button != null && button.Pointer == _tab.button.Pointer;
        static bool IsCooldownOption(NumberOption option) => _cooldownOption != null && _cooldownOption && option != null && option.Pointer == _cooldownOption.Pointer;
        static bool IsHost => AmongUsClient.Instance && AmongUsClient.Instance.AmHost;

        //Le jeu crée les onglets Coéquipier puis Imposteur : on glisse le Shérif juste avant le premier onglet Imposteur
        [HarmonyPatch(typeof(RolesSettingsMenu), nameof(RolesSettingsMenu.AddRoleTab)), HarmonyPrefix]
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
                Reactor.Utilities.Logger<Submerged.SubmergedPlugin>.Error($"[Shérif] Onglet du menu des rôles : {e}");
            }
        }

        [HarmonyPatch(typeof(RolesSettingsMenu), nameof(RolesSettingsMenu.AddRoleTab)), HarmonyPostfix]
        static void AddRoleTabPostfix(RoleBehaviour role)
        {
            if (!_inserting && role != null && role.TeamType == RoleTeamTypes.Crewmate) _lastCrewRole = role;
        }

        static void AddTab(RolesSettingsMenu menu, ref float tabXPos)
        {
            //On laisse le jeu créer un vrai onglet Coéquipier (position, masque, sélection), puis on le transforme en Shérif
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
            tab.name = TabName;
            _tab = tab;

            //Le fond de l'onglet est une image déjà bleue : on en fait une copie jaune
            var background = YellowBackground(tab.background.sprite);
            if (background != null) tab.background.sprite = background;
            var icon = Icon(tab.icon.sprite);
            if (icon != null) tab.icon.sprite = icon;

            button.OnClick.RemoveAllListeners();
            button.OnClick.AddListener((Action)(() => OpenTab(menu, button, borrowed)));
        }

        static RoleBehaviour FindRole(RoleTypes type)
        {
            foreach (var role in RoleManager.Instance.AllRoles)
                if (role != null && role.Role == type) return role;
            return null;
        }

        /// <summary>Ouvre l'onglet Shérif : même page que les rôles du jeu, avec notre info et notre réglage.</summary>
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
                menu.roleHeaderSprite.color = Sheriff.Color;
                menu.roleHeaderText.color = HeaderTextColor;

                var screenshot = Screenshot(menu.roleScreenshot.sprite);
                if (screenshot != null) menu.roleScreenshot.sprite = screenshot;

                AddCooldownOption(menu);
            }
            catch (Exception e)
            {
                Reactor.Utilities.Logger<Submerged.SubmergedPlugin>.Error($"[Shérif] Ouverture de l'onglet : {e}");
            }
        }

        /// <summary>Étoile de shérif blanche, à la taille de l'icône du jeu.</summary>
        static Sprite Icon(Sprite original)
        {
            if (_icon != null && _icon) return _icon;
            var tex = ResourceManager.GetTexture("SheriffIcon.png");
            if (tex == null) return null;
            var size = original != null ? Mathf.Max(original.bounds.size.x, original.bounds.size.y) : 1f;
            _icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width / size);
            _icon.DontUnload();
            return _icon;
        }

        /// <summary>Capture du Shérif en jeu, recadrée au format de l'image du jeu.</summary>
        static Sprite Screenshot(Sprite original)
        {
            if (_screenshot != null && _screenshot) return _screenshot;
            var tex = ResourceManager.GetTexture("SheriffScreenshot.png");
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

        /// <summary>Copie l'image de l'onglet en remplaçant le bleu par le jaune du Shérif (dégradé et transparence gardés).</summary>
        static Sprite YellowBackground(Sprite original)
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
                pixels[i] = new Color(Sheriff.Color.r * shade, Sheriff.Color.g * shade, Sheriff.Color.b * shade, pixel.a);
            }
            tex.SetPixels(pixels);
            tex.Apply();

            var pivot = new Vector2(original.pivot.x / original.rect.width, original.pivot.y / original.rect.height);
            _background = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, original.pixelsPerUnit, 0, SpriteMeshType.FullRect, original.border);
            _background.DontUnload();
            return _background;
        }

        static string ReplaceName(string text, string name)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(name)) return text;
            return text.Replace(name, "Shérif").Replace(name.ToUpperInvariant(), "SHÉRIF");
        }

        static void AddCooldownOption(RolesSettingsMenu menu)
        {
            var option = UnityEngine.Object.Instantiate(menu.numberOptionOrigin, Vector3.zero, Quaternion.identity, menu.AdvancedRolesSettings.transform);
            option.transform.localPosition = new Vector3(RolesSettingsMenu.X_ADVANCED_START, RolesSettingsMenu.Y_ADVANCED_START, -2f);
            _cooldownOption = option; //avant SetUpFromData, pour que nos patchs s'appliquent dès l'initialisation
            option.SetClickMask(menu.ButtonClickMask);
            option.SetUpFromData(CooldownSetting(), RolesSettingsMenu.MASK_LAYER);
            option.OnValueChanged = new Action<OptionBehaviour>(_ => { });
            RefreshOption();

            menu.advancedSettingChildren.Add(option);
            menu.scrollBar.CalculateAndSetYBounds(menu.advancedSettingChildren.Count + 3, 1f, 6f, 0.45f);
        }

        static FloatGameSetting CooldownSetting()
        {
            if (_cooldownSetting != null && _cooldownSetting) return _cooldownSetting;
            var setting = ScriptableObject.CreateInstance<FloatGameSetting>();
            setting.hideFlags = HideFlags.HideAndDontSave;
            setting.Title = CustomGameOptions.KillCooldownName;
            setting.Type = OptionTypes.Float;
            setting.Value = CustomGameOptions.SheriffKillCD;
            setting.Increment = CooldownStep;
            setting.ValidRange = new FloatRange(CustomGameOptions.MinKillCD, CustomGameOptions.MaxKillCD);
            setting.FormatString = "0.0#";
            setting.SuffixType = NumberSuffixes.Seconds;
            setting.ZeroIsInfinity = false;
            _cooldownSetting = setting;
            return setting;
        }

        /// <summary>Met à jour le texte et les boutons du réglage (le jeu n'y touche pas, cf. patchs ci-dessous).</summary>
        static void RefreshOption()
        {
            if (_cooldownOption == null || !_cooldownOption) return;
            var cooldown = CustomGameOptions.SheriffKillCD;
            _cooldownOption.Value = cooldown;
            _cooldownOption.oldValue = cooldown;
            _cooldownOption.TitleText.text = "Temps de recharge du tir";
            _cooldownOption.ValueText.text = cooldown.ToString("0.#", CultureInfo.InvariantCulture) + "s";
            _cooldownOption.MinusBtn.SetInteractable(IsHost && cooldown > CustomGameOptions.MinKillCD);
            _cooldownOption.PlusBtn.SetInteractable(IsHost && cooldown < CustomGameOptions.MaxKillCD);
        }

        static void ChangeCooldown(float cooldown)
        {
            if (!IsHost) return;
            CustomGameOptions.SetKillCooldown(cooldown);
            RefreshOption();
            Sheriff.SyncSettings();
        }

        //Les autres onglets ne doivent pas garder le bandeau jaune
        [HarmonyPatch(typeof(RolesSettingsMenu), nameof(RolesSettingsMenu.ChangeTab)), HarmonyPostfix]
        static void ChangeTab(RolesSettingsMenu __instance)
        {
            if (_opening) return;
            if (_headerSpriteColor.HasValue && __instance.roleHeaderSprite.color == Sheriff.Color) __instance.roleHeaderSprite.color = _headerSpriteColor.Value;
            if (_headerTextColor.HasValue && __instance.roleHeaderText.color == HeaderTextColor) __instance.roleHeaderText.color = _headerTextColor.Value;
        }

        //Le réglage du Shérif ne doit jamais écrire dans les options du jeu (il n'a pas d'option associée)
        [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.Increase)), HarmonyPrefix]
        static bool Increase(NumberOption __instance)
        {
            if (!IsCooldownOption(__instance)) return true;
            ChangeCooldown(CustomGameOptions.SheriffKillCD + CooldownStep);
            return false;
        }

        [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.Decrease)), HarmonyPrefix]
        static bool Decrease(NumberOption __instance)
        {
            if (!IsCooldownOption(__instance)) return true;
            ChangeCooldown(CustomGameOptions.SheriffKillCD - CooldownStep);
            return false;
        }

        [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.Initialize)), HarmonyPrefix]
        static bool Initialize(NumberOption __instance) => Skip(__instance);

        [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.UpdateValue)), HarmonyPrefix]
        static bool UpdateValue(NumberOption __instance) => Skip(__instance);

        [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.AdjustButtonsActiveState)), HarmonyPrefix]
        static bool AdjustButtonsActiveState(NumberOption __instance) => Skip(__instance);

        [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.FixedUpdate)), HarmonyPrefix]
        static bool FixedUpdate(NumberOption __instance) => !IsCooldownOption(__instance);

        static bool Skip(NumberOption option)
        {
            if (!IsCooldownOption(option)) return true;
            RefreshOption();
            return false;
        }
    }
}
