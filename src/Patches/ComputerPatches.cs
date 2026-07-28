using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using RackMedic.Shop;

namespace RackMedic.Patches
{
    /// <summary>
    /// Patches for the in-game desk PC (ComputerShop).
    ///
    /// The game already has dedicated mod-shop support (ModShopItem,
    /// modShopButtonPrefab, modShopItemsParent) which ModShopIntegration uses
    /// to inject our items directly into the PC-monitor shop UI.
    ///
    /// We no longer intercept ButtonShopScreen — the vanilla shop opens normally,
    /// and our items appear alongside the game's own items.
    /// The IMGUI ShopScreen is retained but only reachable via the F8 fallback key.
    /// </summary>
    [HarmonyPatch]
    internal static class ComputerPatches
    {
        // No patches needed here now that ModShopIntegration handles injection.
        // Keeping the class so existing Harmony PatchAll still finds it without errors.
    }
}
