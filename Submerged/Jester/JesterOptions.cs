using System;
using BepInEx.Configuration;
using Reactor.Localization.Utilities;

namespace JesterRole
{
    /// <summary>
    /// Réglages du Jester. L'hôte les règle dans le menu des rôles du lobby (ligne nombre + %),
    /// ils sont enregistrés dans la config BepInEx de Submerged et envoyés aux autres joueurs.
    /// </summary>
    public static class JesterOptions
    {
        //Un seul Jester par partie : il gagne seul
        public const int MaxCount = 1;

        public static int Count;
        public static int Chance;

        static ConfigEntry<int> _count;
        static ConfigEntry<int> _chance;

        public static StringNames JesterName { get; private set; }

        public static void Init(ConfigFile config)
        {
            _count = config.Bind("Jester", "Count", 0, "Nombre de Jesters (0 ou 1), réglable dans le menu des rôles du lobby");
            _chance = config.Bind("Jester", "Chance", 0, "Chance d'avoir un Jester (0 à 100 %), réglable dans le menu des rôles du lobby");
            JesterName = CustomStringName.CreateAndRegister("Jester");
            LoadLocal();
        }

        /// <summary>Recharge les réglages enregistrés (quand on est l'hôte).</summary>
        public static void LoadLocal()
        {
            if (_count == null) return;
            Count = Math.Clamp(_count.Value, 0, MaxCount);
            Chance = Math.Clamp(_chance.Value / 10 * 10, 0, 100);
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
    }
}
