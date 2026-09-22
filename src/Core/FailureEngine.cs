using System;
using System.Collections;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using Il2CppInterop.Runtime.Attributes;

namespace RackMedic.Core
{
    /// <summary>
    /// Drives per-component degradation and random failure.
    /// Runs a coroutine every FailureCheckInterval real seconds.
    /// Only degrades components when a server's isOn field is true.
    /// </summary>
    public class FailureEngine : MonoBehaviour
    {
        public FailureEngine(IntPtr ptr) : base(ptr) { }

        public static FailureEngine Instance { get; private set; }

        private ComponentStore _store;
        private System.Random _rng = new System.Random();

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            _store = ComponentStore.Instance;
            MelonCoroutines.Start(FailureTick());
        }

        // ── Public: register a server and immediately suppress game EOL ─────

        /// <summary>
        /// Register a game Server with RackMedic. Sets up a default profile and
        /// sets the game's EOL fields so the native EOL system never fires.
        /// </summary>
        [HideFromIl2Cpp]
        public ServerProfile RegisterServer(Server server, string chassisId = "chassis_2u")
        {
            string sid = GameAccess.GetServerId(server);
            if (string.IsNullOrEmpty(sid)) return null;
            var profile = _store.GetOrCreate(sid, chassisId);
            GameAccess.SuppressEol(server);
            return profile;
        }

        public static void SuppressEolOnServer(Server server)
        {
            if (server == null) return;
            GameAccess.SuppressEol(server);
        }

        // ── Coroutine: periodic failure tick ─────────────────────────────────

        [HideFromIl2Cpp]
        private IEnumerator FailureTick()
        {
            // Wait a bit for the game world to settle
            yield return new WaitForSeconds(5f);

            while (true)
            {
                float interval = RackMedicMod.FailureCheckIntervalEntry?.Value ?? 60f;
                yield return new WaitForSeconds(interval);

                try
                {
                    RunFailureCheck(interval);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[RackMedic] FailureTick error: {ex}");
                }
            }
        }

        private void RunFailureCheck(float intervalSeconds)
        {
            if (_store == null) return;
            float deltaHours    = intervalSeconds / 3600f;
            float minFailureHours = RackMedicMod.MinFailureHoursEntry?.Value ?? 2f;

            // Find all Server objects in the scene
            var servers = UnityEngine.Object.FindObjectsOfType<Server>();
            if (servers == null) return;

            bool anyChanged = false;

            foreach (var server in servers)
            {
                string sid = GameAccess.GetServerId(server);
                if (string.IsNullOrEmpty(sid)) continue;
                if (!_store.IsManaged(sid)) continue;

                // Only degrade when powered on
                if (!GameAccess.GetIsOn(server)) continue;

                var profile = _store.Get(sid);
                if (profile == null) continue;

                profile.TotalPoweredOnHours += deltaHours;

                float coolingMult = profile.CoolingMultiplier;

                // Degrade every installed component
                anyChanged |= DegradeSlot(profile.Cpu,     deltaHours, coolingMult, minFailureHours, sid, "CPU");
                anyChanged |= DegradeSlot(profile.Psu,     deltaHours, coolingMult, minFailureHours, sid, "PSU");
                anyChanged |= DegradeSlot(profile.Nic,     deltaHours, coolingMult, minFailureHours, sid, "NIC");
                anyChanged |= DegradeSlot(profile.Cooling, deltaHours, coolingMult, minFailureHours, sid, "Cooling");

                if (profile.Ram != null)
                    for (int i = 0; i < profile.Ram.Length; i++)
                        anyChanged |= DegradeSlot(profile.Ram[i], deltaHours, coolingMult, minFailureHours, sid, $"RAM[{i}]");

                if (profile.Storage != null)
                    for (int i = 0; i < profile.Storage.Length; i++)
                        anyChanged |= DegradeSlot(profile.Storage[i], deltaHours, coolingMult, minFailureHours, sid, $"Storage[{i}]");

                // After degradation evaluate server health
                EvaluateServerHealth(server, profile, sid);
                GameAccess.SuppressEol(server); // belt-and-suspenders
            }

            if (anyChanged) _store.Save();
        }

        /// <summary>Returns true if the slot's health changed this tick.</summary>
        [HideFromIl2Cpp]
        private bool DegradeSlot(ComponentSlot slot, float deltaHours, float coolingMult,
                                  float minFailureHours, string sid, string slotName)
        {
            if (slot == null || string.IsNullOrEmpty(slot.DefId)) return false;
            if (slot.Health <= 0f) return false;  // already failed

            var def = slot.Def;
            if (def == null) return false;

            // Only start degrading after the minimum hours threshold
            slot.PoweredOnHours += deltaHours;
            if (slot.PoweredOnHours < minFailureHours) return false;

            // Deterministic degradation scaled by cooling
            float degradation = def.DegradationPerHour * coolingMult * deltaHours;

            // Small random variance ±20%
            double variance = 1.0 + (_rng.NextDouble() * 0.4 - 0.2);
            degradation *= (float)variance;

            float prevHealth = slot.Health;
            slot.Health = Math.Max(0f, slot.Health - degradation);

            if (slot.Health <= 0f && prevHealth > 0f)
                MelonLogger.Msg($"[RackMedic] {sid}: {slotName} ({def.DisplayName}) FAILED after {slot.PoweredOnHours:F1}h");
            else if (slot.Health < 30f && prevHealth >= 30f)
                MelonLogger.Msg($"[RackMedic] {sid}: {slotName} ({def.DisplayName}) degraded to {slot.Health:F0}%");

            return true;
        }

        [HideFromIl2Cpp]
        private void EvaluateServerHealth(Server server, ServerProfile profile, string sid)
        {
            if (profile.HasAnyCriticalFailure)
            {
                MelonLogger.Msg($"[RackMedic] {sid}: critical component failure – dispatching technician");
                TryAddBrokenServer(server, sid);
            }
            else
            {
                TryRemoveBrokenServer(sid);
            }
        }

        // ── Game interop helpers (kept minimal — use GameAccess for field access) ─

        private static void TryAddBrokenServer(Server server, string sid)
        {
            GameAccess.AddBrokenServer(server);
        }

        private static void TryRemoveBrokenServer(string sid)
        {
            GameAccess.RemoveBrokenServer(sid);
        }

        // ── Public: force-evaluate a server after workbench repair ─────────

        [HideFromIl2Cpp]
        public void ReevaluateServer(string serverId)
        {
            if (_store == null) return;
            var profile = _store.Get(serverId);
            if (profile == null) return;

            var servers = UnityEngine.Object.FindObjectsOfType<Server>();
            foreach (var s in servers)
            {
                if (GameAccess.GetServerId(s) == serverId)
                {
                    EvaluateServerHealth(s, profile, serverId);
                    return;
                }
            }
        }
    }
}
