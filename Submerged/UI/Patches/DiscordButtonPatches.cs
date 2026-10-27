using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Submerged.UI.Patches;

[HarmonyPatch]
public static class DiscordButtonPatches
{
    private const string BUTTON_NAME = "DiscordButton";
    private const string DISCORD_URL = "https://discord.gg/ad7aMevNMx";

    private static readonly Color _discordColor = new(88 / 255f, 101 / 255f, 242 / 255f, 1f);

    // Bouton « Discord » en haut du menu principal, entre le nom du compte et la barre des amis
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.ActivateMainMenuUI))]
    [HarmonyPostfix]
    public static void CreateDiscordButtonPatch(MainMenuManager __instance)
    {
        PassiveButton template = __instance.creditsButton;
        if (!template) return;

        Transform parent = template.transform.parent;
        Transform existing = parent.Find(BUTTON_NAME);
        PassiveButton button = existing ? existing.GetComponent<PassiveButton>() : CreateButton(template);

        foreach (SpriteRenderer renderer in button.GetComponentsInChildren<SpriteRenderer>(true)) renderer.color = _discordColor;

        Camera camera = Camera.main;
        if (!camera) return;
        Vector3 position = camera.ViewportToWorldPoint(new Vector3(0.35f, 0.955f, 0f));
        button.transform.position = new Vector3(position.x, position.y, template.transform.position.z);
    }

    private static PassiveButton CreateButton(PassiveButton template)
    {
        PassiveButton button = UnityObject.Instantiate(template, template.transform.parent);
        button.name = BUTTON_NAME;

        // Le bouton copié ouvrait les crédits (listeners du prefab compris) : on repart d'un événement vide
        button.OnClick = new Button.ButtonClickedEvent();
        button.OnClick.AddListener((System.Action) (() => Application.OpenURL(DISCORD_URL)));

        // Supprimés tout de suite (pas en fin de frame), sinon ils s'exécutent quand même une fois :
        // AspectPosition replace le bouton sur « Crédits », la traduction remet « CRÉDITS » et le survol remet le gris
        foreach (AspectPosition aspect in button.GetComponentsInChildren<AspectPosition>(true)) UnityObject.DestroyImmediate(aspect);
        foreach (TextTranslatorTMP translator in button.GetComponentsInChildren<TextTranslatorTMP>(true)) UnityObject.DestroyImmediate(translator);
        foreach (ButtonRolloverHandler rollover in button.GetComponentsInChildren<ButtonRolloverHandler>(true)) UnityObject.DestroyImmediate(rollover);
        foreach (TextMeshPro text in button.GetComponentsInChildren<TextMeshPro>(true)) text.text = "DISCORD";

        return button;
    }
}
