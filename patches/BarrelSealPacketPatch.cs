using AngelsShare;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace AngelsShare
{
    [HarmonyPatch(typeof(BlockEntityBarrel), "OnReceivedClientPacket")]
    public static class BarrelSealPacketPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityBarrel __instance, IPlayer player, int packetid, byte[] data)
        {
            if (__instance?.Api == null) return;

            if (__instance.Api.Side != EnumAppSide.Server) return;

            if (!__instance.Sealed) return;

            ItemSlot liquidSlot = __instance.Inventory[BarrelAgingUtil.LiquidSlotId];

            if (liquidSlot?.Itemstack == null) return;

            ItemStack liquidStack = liquidSlot.Itemstack;

            if (!BarrelAgingUtil.IsAgeableSpirit(liquidStack)) return;

            BarrelAgingCalculator.InitializeAgingOnSeal(__instance, liquidSlot, liquidStack);
        }
    }
}