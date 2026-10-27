using AmongUs.GameOptions;
using HarmonyLib;
using UnityEngine;

public static class Jester
{
    public static Color jesterColor = new Color(0.8f, 0.1f, 0.4f, 1f);
    public static byte? jesterId;
    public static NetworkedPlayerInfo? jester;
}