using System;
using System.Reflection;
using HarmonyLib;
using Submerged.Extensions;
using UnityEngine;

namespace Submerged.Map.Patches;

/// <summary>
/// Les nouvelles versions d'Among Us ajoutent des champs sérialisés à ShipStatus que
/// ShipStatus.Awake instancie (Object.Instantiate). Le prefab Submerged a été construit
/// avec l'ancien script ShipStatus de SubmergedUnity, donc ces champs sont null :
/// Awake lève "The Object you want to instantiate is null", le postfix qui ajoute
/// SubmarineStatus ne s'exécute jamais, et toute la map est cassée (systèmes, ascenseurs,
/// collisions, lumières...).
///
/// Ce patch remplit, avant Awake, toute référence d'asset manquante en la copiant depuis
/// le prefab du Skeld, et journalise le nom du champ pour qu'on puisse l'ajouter
/// proprement au prefab dans SubmergedUnity.
/// </summary>
[HarmonyPatch]
public static class FillMissingShipStatusReferencesPatches
{
    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Awake))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    public static void FillMissingReferencesPrefix(ShipStatus __instance)
    {
        if (!__instance.IsSubmerged()) return;

        ShipStatus skeld = MapLoader.Skeld;

        if (!skeld)
        {
            Error("MapLoader.Skeld n'est pas chargé : impossible de compléter les références manquantes de ShipStatus.");

            return;
        }

        foreach (PropertyInfo prop in typeof(ShipStatus).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            if (!typeof(UnityObject).IsAssignableFrom(prop.PropertyType)) continue;
            if (prop.GetIndexParameters().Length > 0) continue;

            try
            {
                UnityObject current = prop.GetValue(__instance) as UnityObject;
                if (current) continue;

                UnityObject fromSkeld = prop.GetValue(skeld) as UnityObject;
                if (!fromSkeld) continue;

                // Ne jamais copier une référence vers un objet DE LA SCÈNE du Skeld
                // (ex: MedScanner, Hatch...) : seulement des prefabs / assets.
                Transform t = fromSkeld switch
                {
                    Component c => c.transform,
                    GameObject go => go.transform,
                    _ => null,
                };

                if (t && t.IsChildOf(skeld.transform)) continue;

                prop.SetValue(__instance, fromSkeld);
                Warning($"ShipStatus.{prop.Name} était null sur le prefab Submerged : copié depuis le Skeld ({fromSkeld.name}). " +
                        "À ajouter au script ShipStatus de SubmergedUnity puis reconstruire le bundle.");
            }
            catch (Exception e)
            {
                Warning($"Impossible de vérifier ShipStatus.{prop.Name} : {e.Message}");
            }
        }
    }

    /// <summary>
    /// Filet de sécurité : si Awake plante quand même, on ne laisse pas la map sans
    /// SubmarineStatus (sinon des milliers de NullReferenceException et une map inutilisable).
    /// Un postfix Harmony ne s'exécute pas quand la méthode d'origine lève une exception,
    /// un finalizer si.
    /// </summary>
    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Awake))]
    [HarmonyFinalizer]
    public static Exception AwakeFinalizer(ShipStatus __instance, Exception __exception)
    {
        if (__exception == null || !__instance.IsSubmerged()) return __exception;

        Error($"ShipStatus.Awake a échoué sur Submerged, initialisation de SubmarineStatus forcée : {__exception}");

        if (!ShipStatus.Instance) ShipStatus.Instance = __instance;

        if (!__instance.GetComponent<SubmarineStatus>())
        {
            __instance.gameObject.AddComponent<SubmarineStatus>();
        }

        return null;
    }
}
