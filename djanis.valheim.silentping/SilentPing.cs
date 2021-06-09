using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;


namespace djanis.valheim.silentping
{
    [BepInPlugin("djanis.valheim.silentping", SilentPing.ModName, SilentPing.Version)]
    public class SilentPing : BaseUnityPlugin
    {
        public const string Version = "1.0";
        public const string ModName = "SilentPing";
        Harmony _Harmony;
        private static ConfigEntry<bool> modEnabled;
        public static ManualLogSource Log;

        private void Awake()
        {
#if DEBUG
            Log = Logger;
#else
            Log = new ManualLogSource(null);
#endif
            modEnabled = Config.Bind<bool>("General", "Enable Mod", true, "Enable mod to silence pings.");
            Config.Save();

            if (!modEnabled.Value)
            {
                return;
            }
            _Harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
        }

        private void OnDestroy()
        {
            if (_Harmony != null)
            {
                _Harmony.UnpatchSelf();
            }
        }

        [HarmonyPatch(typeof(Chat), nameof(Chat.AddString), typeof(string), typeof(string), typeof(Talker.Type))]

        public static class Chat_RemovePingsWorldTexts_Patch
        {
            private static bool Prefix(ref Chat __instance, ref string user, ref string text, ref Talker.Type type)
            {
                if (modEnabled.Value && type == Talker.Type.Ping)
                {
                    return false;
                }
                return true;
            }
        }
    }
}