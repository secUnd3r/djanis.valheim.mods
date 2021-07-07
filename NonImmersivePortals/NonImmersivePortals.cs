using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Diagnostics;
using UnityEngine;

namespace NonImmersivePortals
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class NonImmersivePortals : BaseUnityPlugin
    {
        public const string PluginGUID = "djanis.valheim.nonimmersiveportals";
        public const string PluginName = "NonImmersivePortals";
        public const string PluginVersion = "1.0.0";

        public static ConfigEntry<bool> _isModEnabled;
        public static ConfigEntry<bool> _showPortalBlackScreen;
        public static ConfigEntry<float> _teleportLowTime;
        public static ConfigEntry<double> _considerSceneLoadedSeconds;
        public static ConfigEntry<int> _decreaseTeleportTimeByPercent;
        public static ConfigEntry<int> _multiplyDeltaTimeBy;

        //public static Stopwatch _teleportStopwatch;

        public Harmony _harmony;

        private void Awake()
        {
            _isModEnabled = 
                Config.Bind(
                    "_Global", 
                    "isModEnabled", 
                    true, 
                    "Globally enable or disable this mod.");

            _showPortalBlackScreen =
                Config.Bind(
                    "Portal",
                    "showPortalBlackScreen",
                    true,
                    "if enabled, show the portal black screen during teleportation.");

            _teleportLowTime =
                Config.Bind(
                    "TimeManipulation",
                    "TeleportLowTime",
                    2.0f,
                    "Manipulate the low end limit for teleportation.");

            _considerSceneLoadedSeconds = 
                Config.Bind(
                    "TimeManipulation", 
                    "ConsiderAreaLoadedAfterSeconds", 
                    3.75, 
                    "Indicates a threshold in seconds after which an area should be considered sufficiently loaded (ie. lazy approach) to allow the teleportation to end prematurely.");

            _decreaseTeleportTimeByPercent = 
                Config.Bind("TimeManipulation", 
                "DecreaseMinLoadTimeByPercent", 
                50, 
                "Decreases the artificial minimum teleportation duration hardcoded by the developers (Iron Gate). 100% indicates removal of the minimum wait time and means that the only condition for arrival is the area load state. Value will be clamped in the range 0-100.");
            
            _multiplyDeltaTimeBy = 
                Config.Bind(
                    "TimeManipulation", 
                    "MultiplyDeltaTimeBy", 
                    3, 
                    "Multiplier of the speed in which the teleport time increases until the artificial minimum duration is reached. Value will be clamped in the range 1-10.");

            _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
        }

        private void OnDestroy()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
            }
        }


    }

    public static class MathExtension
    {
        public static T Clamp<T>(this T val, T min, T max) where T : IComparable<T>
        {
            if (val.CompareTo(min) < 0) return min;
            else if (val.CompareTo(max) > 0) return max;
            else return val;
        }
    }

    [HarmonyPatch(typeof(Hud))]
    static class HudPatch
    {
        [HarmonyTranspiler]
        [HarmonyPatch(nameof(Hud.UpdateBlackScreen))]
        static IEnumerable<CodeInstruction> HudUpdateBlackScreenTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            return new CodeMatcher(instructions)
                .MatchForward(
                    useEnd: false,
                    new CodeMatch(
                        OpCodes.Callvirt,
                        AccessTools.Method(typeof(Character), 
                        nameof(Character.IsTeleporting))))
                .SetAndAdvance(
                    OpCodes.Call,
                    Transpilers.EmitDelegate<Func<Player, bool>>(
                        player => NonImmersivePortals._showPortalBlackScreen.Value && player.IsTeleporting()).operand)
                .InstructionEnumeration();
        }
    }

    [HarmonyPatch(typeof(Character))]
    public class CharacterPatch
    {
        //[HarmonyTranspiler]
        //[HarmonyPatch(nameof(Character.ApplyDamage))]
        //public static IEnumerable<CodeInstruction> PreventDamageWhileTeleporting(IEnumerable<CodeInstruction> instructions)
        //{
        //    return new CodeMatcher(instructions)
        //        .MatchForward(
        //            useEnd: false,
        //            new CodeMatch(
        //                OpCodes.Callvirt,
        //                AccessTools.Method(typeof(Character), 
        //                nameof(Character.IsTeleporting))))
        //        .SetAndAdvance(
        //            OpCodes.Call,
        //            //Transpilers.EmitDelegate<Func<Character, bool>>(
        //            //    player => NonImmersivePortals._teleportStopwatch.Elapsed.TotalSeconds < 0.5f || player.IsTeleporting()).operand)
        //            Transpilers.EmitDelegate<Func<Character, bool>>(
        //                player => player.IsTeleporting()).operand)
        //        .InstructionEnumeration();
        //}
    }

    [HarmonyPatch(typeof(Player))]
    static class PlayerPatch
    {
        private static bool _hasTeleported;

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Player.UpdateTeleport))]
        public static void DecreaseTeleportTime(ref float dt, ref Player __instance, ref float ___m_teleportTimer)
        {
            //if (__instance.m_teleporting && (!NonImmersivePortals._showPortalBlackScreen.Value ||
            //    NonImmersivePortals._teleportStopwatch.Elapsed.TotalSeconds > Hud.instance.GetFadeDuration(__instance)))
            if (__instance.m_teleporting && (!NonImmersivePortals._showPortalBlackScreen.Value))
            {
                dt *= NonImmersivePortals._multiplyDeltaTimeBy.Value.Clamp(1, 10);

                if (___m_teleportTimer < 2f)
                {
                    ___m_teleportTimer = 2f; // Jump to first branch in UpdateTeleport skipping initial frames.
                }

                // Decrease artificial minimum teleport duration.
                ___m_teleportTimer *= (1f + NonImmersivePortals._decreaseTeleportTimeByPercent.Value / 100f).Clamp(1f, 2f);
            }
        }
        [HarmonyTranspiler]
        [HarmonyPatch(nameof(Player.UpdateTeleport))]
        public static IEnumerable<CodeInstruction> ApplyConsiderAreaLoadedConfig(IEnumerable<CodeInstruction> instructions)
        {
            return new CodeMatcher(instructions)
                .MatchForward(
                    useEnd: false,
                    new CodeMatch(
                        OpCodes.Callvirt,
                        AccessTools.Method(typeof(ZNetScene),
                        nameof(ZNetScene.IsAreaReady))))
                .SetAndAdvance(
                    OpCodes.Call,
                    Transpilers.EmitDelegate<Func<ZNetScene, Vector3, bool>>(
                        IsAreaLoadedLazy).operand)
                .InstructionEnumeration();
        }

        private static bool IsAreaLoadedLazy(ZNetScene zNetScene, Vector3 targetPos)
        {
            //return NonImmersivePortals._teleportStopwatch.Elapsed.TotalSeconds >
            //       NonImmersivePortals._considerSceneLoadedSeconds.Value || zNetScene.IsAreaReady(targetPos);
            return zNetScene.IsAreaReady(targetPos);
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Player.TeleportTo))]
        public static void SetLastTeleportTime(bool __result)
        {
            if (__result)
            {
                //NonImmersivePortals._teleportStopwatch.Restart();
                _hasTeleported = true;
            }
        }
    }

    [HarmonyPatch(typeof(TeleportWorld))]
    public static class TeleportWorldPatch
    {
        internal static float lastTeleportDistance;

        [HarmonyPrefix]
        [HarmonyPatch(nameof(TeleportWorld.Teleport))]
        public static bool IsFront(ref Player player, TeleportWorld __instance, ref ZNetView ___m_nview, ref float ___m_exitDistance)
        {
            var trigger = __instance.GetComponentInChildren<TeleportWorldTrigger>();
            //MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "Starting new stopwatch");
            //NonImmersivePortals._teleportStopwatch = new Stopwatch();
            //NonImmersivePortals._teleportStopwatch.Start();
            return trigger;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(nameof(TeleportWorld.Teleport))]
        public static IEnumerable<CodeInstruction> ReplaceTeleportToWithImmersiveApproach(IEnumerable<CodeInstruction> instructions)
        {
            return new CodeMatcher(instructions)
                .MatchForward(
                    useEnd: false,
                    new CodeMatch(
                        OpCodes.Callvirt, 
                        AccessTools.Method(typeof(Character),
                        nameof(Character.TeleportTo))))
                .InsertAndAdvance(new CodeInstruction(OpCodes.Ldarg_0))
                .InsertAndAdvance(new CodeInstruction(OpCodes.Ldloc_0))
                .SetAndAdvance(
                    OpCodes.Call,
                    Transpilers.EmitDelegate<Func<Character, Vector3, Quaternion, bool, TeleportWorld, ZDOID, bool>>(
                        ImmersiveTeleportTo).operand)
                .InstructionEnumeration();
        }

        public static bool ImmersiveTeleportTo(Character c, Vector3 pos, Quaternion rot, bool distantTeleport, TeleportWorld instance, ZDOID zdoid)
        {
            var player = (Player)c;
            var zdo = ZDOMan.instance.GetZDO(zdoid);
            var trigger = instance.GetComponentInChildren<TeleportWorldTrigger>();

            var target = ZNetScene.instance.FindInstance(zdo)?.gameObject;

            lastTeleportDistance = (instance.transform.position - pos).magnitude;

            if (target == null)
            {
                // Fallback to original method.
                return player.TeleportTo(pos, rot, distantTeleport);
            }

            // Calculate angle at target trigger bounds that reflects angle at which overlapping at source trigger occurred.
            var targetTrigger = target.GetComponentInChildren<TeleportWorldTrigger>();

            //float rotationDiff = -Quaternion.Angle(trigger.transform.rotation, targetTrigger.transform.rotation);
            var targetRot = targetTrigger.transform.rotation;

            Vector3 triggerToPlayer = player.transform.position - trigger.transform.position;
            //Vector3 positionOffset = Quaternion.Euler(0f, rotationDiff, 0f) * triggerToPlayer;
            //Vector3 a = targetTrigger.transform.rotation * Vector3.forward * 1.15f;
            //var targetPos = targetTrigger.transform.position + a * instance.m_exitDistance + positionOffset;
            var targetPos = targetTrigger.transform.position * instance.m_exitDistance + triggerToPlayer;

            if (ZNetScene.instance.IsAreaReady(targetPos)/*|| target.gameObject.activeSelf*/)
            {
                // Portal is in a loaded area thus close enough for instant teleport.

                if (player.IsTeleporting() || player.m_teleportCooldown < 0.1f)
                {
                    return false;
                }

                // Teleport him!
                PlayerPatch.SetLastTeleportTime(true);
                player.m_teleporting = true; // May act as a lock for async routines.
                player.m_maxAirAltitude = player.transform.position.y;
                targetPos.y += 0.2f;
                player.transform.position = targetPos;
                player.transform.rotation = targetRot;
                player.m_body.velocity = Vector3.zero;
                player.SetLookDir(targetRot * Vector3.forward);
                player.m_teleportCooldown = 0f;
                player.m_teleporting = false;
                player.ResetCloth();
                return true;
            }
            return player.TeleportTo(targetPos, targetRot, distantTeleport);
        }
    }
}