using System;
using BepInEx.Configuration;
using Reactor.Localization.Utilities;

namespace SherriffRole
{
    public enum CustomRPC : byte { SetSheriff = 150, SheriffKill = 151, SyncSheriffSettings = 152 }

    /// <summary>
    /// Réglages du Shérif. L'hôte les règle dans le menu des rôles du lobby (nombre et %),
    /// ils sont enregistrés dans la config BepInEx de Submerged et envoyés aux autres joueurs.
    /// </summary>
    public static class CustomGameOptions
    {
        public const int MaxCount = 15;
        public const float MinKillCD = 10f;
        public const float MaxKillCD = 40f;

        public static int SheriffCount;
        public static int SheriffChance;
        public static bool showSheriff;
        public static float SheriffKillCD = 30f;

        static ConfigEntry<int> _count;
        static ConfigEntry<int> _chance;
        static ConfigEntry<bool> _show;
        static ConfigEntry<float> _cooldown;

        public static StringNames SheriffName { get; private set; }
        public static StringNames KillCooldownName { get; private set; }

        public static void Init(ConfigFile config)
        {
            _count = config.Bind("Sheriff", "Count", 0, "Nombre de Shérifs (0 à 15), réglable dans le menu des rôles du lobby");
            _chance = config.Bind("Sheriff", "Chance", 0, "Chance d'avoir un Shérif (0 à 100 %), réglable dans le menu des rôles du lobby");
            _show = config.Bind("Sheriff", "Show Sheriff", false, "Tout le monde voit qui est le Shérif");
            _cooldown = config.Bind("Sheriff", "Kill Cooldown", 30f, "Secondes entre deux tirs (10 à 40)");
            SheriffName = CustomStringName.CreateAndRegister("Shérif");
            KillCooldownName = CustomStringName.CreateAndRegister("Temps de recharge du tir");
            LoadLocal();
        }

        /// <summary>Recharge les réglages enregistrés (quand on est l'hôte).</summary>
        public static void LoadLocal()
        {
            if (_count == null) return;
            SheriffCount = Math.Clamp(_count.Value, 0, MaxCount);
            SheriffChance = Math.Clamp(_chance.Value / 10 * 10, 0, 100);
            showSheriff = _show.Value;
            SheriffKillCD = Math.Clamp(_cooldown.Value, MinKillCD, MaxKillCD);
        }

        /// <summary>L'hôte modifie le temps de recharge du tir depuis l'onglet Shérif du lobby.</summary>
        public static void SetKillCooldown(float cooldown)
        {
            SheriffKillCD = Math.Clamp(cooldown, MinKillCD, MaxKillCD);
            if (_cooldown != null) _cooldown.Value = SheriffKillCD;
        }

        /// <summary>L'hôte modifie le nombre ou la chance depuis le lobby.</summary>
        public static void SetQuota(int count, int chance)
        {
            SheriffCount = Math.Clamp(count, 0, MaxCount);
            SheriffChance = Math.Clamp(chance, 0, 100);
            if (_count != null)
            {
                _count.Value = SheriffCount;
                _chance.Value = SheriffChance;
            }
        }
    }
}
