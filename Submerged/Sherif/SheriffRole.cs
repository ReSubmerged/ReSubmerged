using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;

namespace SherriffRole
{
    public enum CustomRPC : byte { SetSheriff = 150, SheriffKill = 151 }

    //Host options, edited in BepInEx/config and sent to the other players when the Sheriff is chosen
    public static class CustomGameOptions
    {
        public static bool showSheriff;
        public static float SheriffKillCD = 30f;
    }

    public class SherriffRole : BasePlugin
    {
        public override void Load()
        {
            var show = Config.Bind("Sheriff", "Show Sheriff", false, "Everyone sees who the Sheriff is");
            var cooldown = Config.Bind("Sheriff", "Kill Cooldown", 30f, "Seconds between two shots (10 to 40)");
            CustomGameOptions.showSheriff = show.Value;
            CustomGameOptions.SheriffKillCD = System.Math.Max(10f, System.Math.Min(40f, cooldown.Value));

            Log.LogMessage("Sheriff Mod loaded");
            CustomServer.Register(Config, Log);
        }
    }
}
