using BepInEx.Configuration;
using BepInEx.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace SherriffRole
{
    //Custom server region, configured in BepInEx/config
    public static class CustomServer
    {
        public static void Register(ConfigFile config, ManualLogSource log)
        {
            var name = config.Bind("Custom", "Name", "Custom").Value;
            var host = config.Bind("Custom", "Ipv4 or Hostname", "127.0.0.1").Value;
            var port = config.Bind("Custom", "Port", (ushort)22023).Value;

            var ip = host;
            if (Uri.CheckHostName(host) == UriHostNameType.Dns)
            {
                log.LogMessage("Resolving " + host + " ...");
                try { ip = Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? ip; }
                catch { log.LogMessage("Hostname could not be resolved: " + ip); }
                log.LogMessage("IP is " + ip);
            }

            var servers = new Il2CppReferenceArray<ServerInfo>(new[] { new ServerInfo(name + "-Master-1", ip, port, false) });
            var region = new StaticHttpRegionInfo(name, StringNames.NoTranslation, ip, servers).Cast<IRegionInfo>();

            var manager = DestroyableSingleton<ServerManager>.Instance;
            var current = manager.CurrentRegion;
            if (current != null && current.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) current = region;
            manager.AddOrUpdateRegion(region);
            if (current != null) manager.SetRegion(current); //the game remembers the previous region: restore it
        }
    }
}
