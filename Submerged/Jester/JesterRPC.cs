using HarmonyLib;
using Hazel;

enum JesterRpcCalls
{
    SendJester = 42
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc))]
public static class PlayerControl_HandleRpc
{
    public static void Postfix(byte callId, MessageReader reader)
    {
        // We are using 42 for our rpc that sends the jester id to the other players
        if(callId == (byte)JesterRpcCalls.SendJester)
        {
            byte jesterId = reader.ReadByte();
            Jester.jesterId = jesterId;
        }
    }
}

public static class JesterRpc
{
    public static void SendJester()
    {
        var writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)JesterRpcCalls.SendJester, SendOption.Reliable, -1);
        writer.Write((sbyte) Jester.jesterId);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }
}