using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace AngelsShare
{
    [HarmonyPatch(typeof(BlockEntityBarrel), "OnEvery3Second")]
    public static class BarrelAgingTickPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(BlockEntityBarrel __instance)
        {
            if (
                __instance?.Api == null ||
                __instance.Api.Side != EnumAppSide.Server ||
                !__instance.Sealed
            )
            {
                return true;
            }

            ItemSlot liquidSlot = __instance.Inventory[BarrelAgingUtil.LiquidSlotId];
            ItemStack liquidStack = liquidSlot?.Itemstack;
            if (!BarrelAgingUtil.IsAgeableSpirit(liquidStack)) return true;

            if (!MaturationRecordCodec.HasActiveSession(liquidStack))
            {
                BarrelAgingCalculator.InitializeAgingOnSeal(
                    __instance,
                    liquidSlot,
                    liquidStack
                );
            }

            // Continuous aging is finalized only by the transactional manual-unseal
            // path. Vanilla's timer must not auto-craft it or open it after a reload.
            return false;
        }
    }
}
