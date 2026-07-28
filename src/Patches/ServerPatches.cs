using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using RackMedic.Core;

namespace RackMedic.Patches
{
    /// <summary>
    /// Harmony patches that intercept the game's native EOL (end-of-life) server
    /// breaking system so that RackMedic-managed servers are never auto-broken.
    /// </summary>
    [HarmonyPatch]
    internal static class ServerPatches
    {
        // ── Patch 1: Server.ItIsBroken ────────────────────────────────────────
        // The game calls this private void when the EOL countdown expires.
        // We skip the entire method body for any managed server, so it never
        // marks the server broken, never stops its uptime, and never dispatches
        // a technician on the game's schedule (we do that ourselves via
        // FailureEngine when a real component fails).

        [HarmonyPatch(typeof(Server), "ItIsBroken")]
        [HarmonyPrefix]
        static bool PrefixItIsBroken(Server __instance)
        {
            if (!RackMedicMod.PatchEolEntry?.Value ?? false) return true; // pass-through if disabled in prefs

            string sid = TryGetServerId(__instance);
            if (string.IsNullOrEmpty(sid)) return true;

            if (ComponentStore.Instance?.IsManaged(sid) == true)
            {
                MelonLogger.Msg($"[RackMedic] Blocked native ItIsBroken on managed server: {sid}");
                // Keep EOL fields suppressed while we're here
                FailureEngine.SuppressEolOnServer(__instance);
                return false; // skip original method
            }

            return true; // run original for unmanaged servers
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string TryGetServerId(Server server)
            => GameAccess.GetServerId(server);
    }
}
