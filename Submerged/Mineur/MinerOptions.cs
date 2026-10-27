using System;
using BepInEx.Configuration;
using Reactor.Localization.Utilities;

namespace MinerRole
{
    /// <summary>
    /// Réglages du Mineur. L'hôte les règle dans le menu des rôles du lobby (ligne nombre + % et onglet « Mineur »),
    /// ils sont enregistrés dans la config BepInEx de Submerged et envoyés aux autres joueurs.
    /// </summary>
    public static class MinerOptions
    {
        public const int MaxCount = 3;
        public const float MinCooldown = 10f, MaxCooldown = 60f, CooldownStep = 2.5f;
        public const int MinVents = 1, MaxVents = 10;

        public static int Count;
        public static int Chance;
        public static float Cooldown = 25f;
        public static int MaxVentCount = 3;

        static ConfigEntry<int> _count;
        static ConfigEntry<int> _chance;
        static ConfigEntry<float> _cooldown;
        static ConfigEntry<int> _maxVents;

        public static StringNames MinerName { get; private set; }

        public static void Init(ConfigFile config)
        {
            _count = config.Bind("Miner", "Count", 0, "Nombre de Mineurs (0 à 3), réglable dans le menu des rôles du lobby");
            _chance = config.Bind("Miner", "Chance", 0, "Chance d'avoir un Mineur (0 à 100 %), réglable dans le menu des rôles du lobby");
            _cooldown = config.Bind("Miner", "Cooldown", 25f, "Secondes entre deux conduits creusés (10 à 60)");
            _maxVents = config.Bind("Miner", "Max Vents", 3, "Nombre maximum de conduits par Mineur (1 à 10)");
            MinerName = CustomStringName.CreateAndRegister("Mineur");
            LoadLocal();
        }

        /// <summary>Recharge les réglages enregistrés (quand on est l'hôte).</summary>
        public static void LoadLocal()
        {
            if (_count == null) return;
            Count = Math.Clamp(_count.Value, 0, MaxCount);
            Chance = Math.Clamp(_chance.Value / 10 * 10, 0, 100);
            Cooldown = Math.Clamp(_cooldown.Value, MinCooldown, MaxCooldown);
            MaxVentCount = Math.Clamp(_maxVents.Value, MinVents, MaxVents);
        }

        /// <summary>L'hôte modifie le nombre ou la chance depuis le lobby.</summary>
        public static void SetQuota(int count, int chance)
        {
            Count = Math.Clamp(count, 0, MaxCount);
            Chance = Math.Clamp(chance, 0, 100);
            if (_count == null) return;
            _count.Value = Count;
            _chance.Value = Chance;
        }

        /// <summary>L'hôte modifie le temps de recharge depuis l'onglet Mineur.</summary>
        public static void SetCooldown(float cooldown)
        {
            Cooldown = Math.Clamp(cooldown, MinCooldown, MaxCooldown);
            if (_cooldown != null) _cooldown.Value = Cooldown;
        }

        /// <summary>L'hôte modifie le nombre max de conduits depuis l'onglet Mineur.</summary>
        public static void SetMaxVents(int maxVents)
        {
            MaxVentCount = Math.Clamp(maxVents, MinVents, MaxVents);
            if (_maxVents != null) _maxVents.Value = MaxVentCount;
        }
    }
}
