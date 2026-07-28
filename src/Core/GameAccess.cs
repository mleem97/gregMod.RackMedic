using System;
using System.Reflection;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace RackMedic.Core
{
    /// <summary>
    /// Runtime reflection-based accessor for game type fields/properties
    /// that may have differing capitalization or type handling in the
    /// IL2CppInterop layer. Logs resolved names on first call.
    /// </summary>
    internal static class GameAccess
    {
        private static bool _initialized;

        // Server field descriptors
        private static PropertyInfo _serverIdProp;
        private static PropertyInfo _eolProp;
        private static PropertyInfo _eolTimeProp;
        private static PropertyInfo _isOnProp;

        // StaticUIElements coin descriptor
        private static PropertyInfo _coinsProp;

        // ── Init ──────────────────────────────────────────────────────────────

        private static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            var tServer = typeof(Server);
            _serverIdProp = FindProp(tServer, "serverID", "serverId", "ServerID");
            _eolProp      = FindProp(tServer, "eol", "Eol", "EOL");
            _eolTimeProp  = FindProp(tServer, "eolTime", "EolTime", "eoltime");
            _isOnProp     = FindProp(tServer, "isOn", "IsOn", "is_on");

            var tUI = typeof(StaticUIElements);
            _coinsProp = FindProp(tUI, "coins", "Coins", "coin");

            MelonLogger.Msg("[RackMedic][GameAccess] Resolved:" +
                $" serverID={_serverIdProp?.Name ?? "null"}" +
                $" eol={_eolProp?.Name ?? "null"}" +
                $" eolTime={_eolTimeProp?.Name ?? "null"}" +
                $" isOn={_isOnProp?.Name ?? "null"}" +
                $" coins={_coinsProp?.Name ?? "null"}");
        }

        private static PropertyInfo FindProp(Type type, params string[] names)
        {
            foreach (var name in names)
            {
                var p = type.GetProperty(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null) return p;
            }
            return null;
        }

        // ── Server field access ───────────────────────────────────────────────

        public static string GetServerId(Server server)
        {
            Init();
            try { return _serverIdProp?.GetValue(server) as string; }
            catch { return null; }
        }

        public static bool GetIsOn(Server server)
        {
            Init();
            try { return (bool)(_isOnProp?.GetValue(server) ?? false); }
            catch { return false; }
        }

        public static void SuppressEol(Server server)
        {
            Init();
            try
            {
                // eol = false
                _eolProp?.SetValue(server, false);

                // eolTime = very large value; handle both int and float typed fields
                if (_eolTimeProp != null)
                {
                    if (_eolTimeProp.PropertyType == typeof(int))
                        _eolTimeProp.SetValue(server, int.MaxValue);
                    else
                        _eolTimeProp.SetValue(server, float.MaxValue);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[RackMedic] SuppressEol failed: {ex.Message}");
            }
        }

        // ── StaticUIElements coin access ──────────────────────────────────────

        public static int GetCoins()
        {
            Init();
            try
            {
                var ui = UnityEngine.Object.FindObjectOfType<StaticUIElements>();
                if (ui == null) return 0;
                var v = _coinsProp?.GetValue(ui);
                return v == null ? 0 : Convert.ToInt32(v);
            }
            catch { return 0; }
        }

        public static bool SpendCoins(int amount)
        {
            Init();
            try
            {
                var ui = UnityEngine.Object.FindObjectOfType<StaticUIElements>();
                if (ui == null) return false;
                var v = _coinsProp?.GetValue(ui);
                int current = v == null ? 0 : Convert.ToInt32(v);
                if (current < amount) return false;
                _coinsProp?.SetValue(ui, Convert.ChangeType(current - amount, _coinsProp.PropertyType));
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[RackMedic] SpendCoins failed: {ex.Message}");
                return false;
            }
        }

        // ── TechnicianManager helpers ─────────────────────────────────────────

        public static void AddBrokenServer(Server server)
        {
            try
            {
                var mgr = TechnicianManager.instance;
                if (mgr == null) return;
                var method = typeof(TechnicianManager).GetMethod("AddBrokenServer",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                method?.Invoke(mgr, new object[] { server });
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[RackMedic] AddBrokenServer failed: {ex.Message}");
            }
        }

        public static void RemoveBrokenServer(string serverId)
        {
            try
            {
                var mgr = TechnicianManager.instance;
                if (mgr == null) return;
                // Try RemoveBrokenServer method (may not exist by that name)
                var method = typeof(TechnicianManager).GetMethod("RemoveBrokenServer",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null)
                {
                    method.Invoke(mgr, new object[] { serverId });
                    return;
                }
                // Fallback: try removing from a brokenServers collection field
                var field = typeof(TechnicianManager).GetField("brokenServers",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field?.GetValue(mgr) is System.Collections.IDictionary dict)
                    if (dict.Contains(serverId)) dict.Remove(serverId);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[RackMedic] RemoveBrokenServer failed: {ex.Message}");
            }
        }
    }
}
