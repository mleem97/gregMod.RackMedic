using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using RackMedic.Core;

namespace RackMedic.Shop
{
    /// <summary>
    /// Integrates RackMedic component items into the game's native shop UI
    /// (ComputerShop / in-game PC monitor), using the game's built-in
    /// ModShopItem / modShopButtonPrefab system so that:
    ///   • Items appear on the PC-monitor shop screen alongside vanilla items.
    ///   • Purchases go through the game's cart → checkout → delivery flow,
    ///     so bought components spawn physically in the delivery area.
    ///   • RackMedic virtual inventory is also updated at checkout.
    /// </summary>
    public class ModShopIntegration : MonoBehaviour
    {
        public ModShopIntegration(IntPtr ptr) : base(ptr) { }

        public static ModShopIntegration Instance { get; private set; }

        // Map from modID (int) → our catalog item ID (string), populated when
        // we inject items into the shop.
        private static readonly Dictionary<int, string> _modIdToItemId = new();
        private static int _lastInjectedLoaderInstanceId = int.MinValue;

        private void Awake() { Instance = this; }

        // ── Shop injection ───────────────────────────────────────────────────

        /// <summary>Called from the Harmony postfix on ModLoader.Start to
        /// inject our items into the mod section of the native shop.
        /// Uses ModLoader.instance.CreateShopButton so items appear on the
        /// in-game PC monitor alongside vanilla mod items.</summary>
        public static void InjectItems()
        {
            try
            {
                var loader = ModLoader.instance;
                if (loader == null)
                {
                    MelonLogger.Warning("[RackMedic] ModLoader.instance is null – skipping shop injection.");
                    return;
                }

                // ModLoader.Start can be observed more than once during the
                // current game's UI/bootstrap sequence. Re-registering the
                // same 25 cards duplicates the native shop content and causes
                // its layout to grow a second time.
                int loaderInstanceId = loader.GetInstanceID();
                if (_lastInjectedLoaderInstanceId == loaderInstanceId)
                {
                    MelonLogger.Msg("[RackMedic] Shop already injected for this ModLoader instance; skipping duplicate injection.");
                    return;
                }

                _lastInjectedLoaderInstanceId = loaderInstanceId;

                _modIdToItemId.Clear();
                int modId = loader.nextModID; // start after any mods loaded before us

                // ── Components ──────────────────────────────────────────────
                foreach (var def in ShopCatalog.Components)
                {
                    RegisterItem(loader, modId, def.Id, def.DisplayName, def.Price);
                    modId++;
                }

                // ── Chassis ─────────────────────────────────────────────────
                foreach (var ch in ShopCatalog.Chassis)
                {
                    RegisterItem(loader, modId, ch.Id, ch.DisplayName, ch.Price);
                    modId++;
                }

                // ── UPS units ────────────────────────────────────────────────
                foreach (var ups in ShopCatalog.UpsUnits)
                {
                    RegisterItem(loader, modId, ups.Id, ups.DisplayName, ups.Price);
                    modId++;
                }

                loader.nextModID = modId; // advance so future mods don't collide
                MelonLogger.Msg($"[RackMedic] Injected {_modIdToItemId.Count} items into native shop.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[RackMedic] Shop injection failed: {ex}");
            }
        }

        private static void RegisterItem(
            ModLoader loader, int modId, string itemId, string displayName, int price)
        {
            var cfg = new ShopItemConfig
            {
                itemName   = displayName,
                price      = price,
                objectType = PlayerManager.ObjectInHand.ModItem
            };

            loader.CreateShopButton(modId, cfg, null);
            _modIdToItemId[modId] = itemId;
        }

        // ── Called by patch when a ModShopItem is bought ─────────────────────

        /// <summary>Resolves the catalog item for a given modID and records it
        /// so it can be added to the RackMedic inventory when checkout happens.</summary>
        public static void OnModItemBuyClicked(int modId)
        {
            if (_modIdToItemId.TryGetValue(modId, out string itemId))
                MelonLogger.Msg($"[RackMedic] Cart: {itemId} (modId={modId})");
        }

        /// <summary>Called from SpawnPhysicalItem postfix for ModItem purchases,
        /// meaning the checkout completed and the physical delivery was spawned.</summary>
        public static void OnModItemCheckedOut(int modId, int price)
        {
            if (!_modIdToItemId.TryGetValue(modId, out string itemId)) return;
            ComponentStore.Instance?.AddToInventory(itemId, 1);
            MelonLogger.Msg($"[RackMedic] Inventory += {itemId}  (paid {price} ₵)");
        }

        // ── Map lookup ───────────────────────────────────────────────────────

        public static bool TryGetItemId(int modId, out string itemId)
            => _modIdToItemId.TryGetValue(modId, out itemId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Harmony patches
    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch]
    internal static class ModShopPatches
    {
        /// <summary>After ModLoader.Start runs (all mod packs loaded + UI ready),
        /// inject our items into the shop. One-frame delay ensures any final
        /// initialisation in Start completes before we touch the UI.</summary>
        [HarmonyPatch(typeof(ModLoader), "Start")]
        [HarmonyPostfix]
        static void PostfixModLoaderStart()
        {
            try
            {
                MelonLoader.MelonCoroutines.Start(InjectNextFrame());
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[RackMedic] Shop inject failed: {ex.GetBaseException().Message}");
            }
        }

        private static System.Collections.IEnumerator InjectNextFrame()
        {
            yield return null; // wait one frame
            try
            {
                ModShopIntegration.InjectItems();
            }
            catch (System.Exception ex)
            {
                // Unbehandelt wuerde die Coroutine kommentarlos sterben —
                // lieber loggen, damit der Shop-Inject sichtbar bleibt.
                MelonLoader.MelonLogger.Error($"[RackMedic] Shop inject failed: {ex.GetBaseException().Message}");
            }
        }

        /// <summary>When a ModShopItem buy button is clicked, note it for inventory tracking.</summary>
        [HarmonyPatch(typeof(ModShopItem), "ButtonBuyItem")]
        [HarmonyPrefix]
        static void PrefixButtonBuyItem(ModShopItem __instance)
        {
            try
            {
                if (__instance == null) return;
                ModShopIntegration.OnModItemBuyClicked(__instance.modID);
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[RackMedic] Buy-click track failed: {ex.GetBaseException().Message}");
            }
        }
    }
}
