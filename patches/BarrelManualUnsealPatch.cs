using AngelsShare;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace AngelsShare
{
    [HarmonyPatch(typeof(BlockBarrel), "OnBlockInteractStart")]
    public static class BarrelManualUnsealPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(BlockBarrel __instance, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref bool __result)
        {
            if (world == null || byPlayer == null || blockSel?.Position == null) return true;

            BlockEntityBarrel barrel = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityBarrel;

            if (barrel == null) return true;

            if (!barrel.Sealed) return true;

            ItemSlot liquidSlot = barrel.Inventory[BarrelAgingUtil.LiquidSlotId];

            if (liquidSlot?.Itemstack == null) return true;

            ItemStack liquidStack = liquidSlot.Itemstack;

            if (!BarrelAgingUtil.IsAgeableSpirit(liquidStack)) return true;

            bool isSneaking = byPlayer.Entity?.Controls?.Sneak == true
                || byPlayer.WorldData?.EntityControls?.ShiftKey == true;

            if (!isSneaking)
            {
                __result = true;
                return false;
            }

            if (world.Side == EnumAppSide.Client)
            {
                __result = true;
                return false;
            }

            BarrelAgingCalculator.FinalizeAgingOnUnseal(barrel, liquidSlot, liquidStack);

            ITreeAttribute oldTree = liquidStack.Attributes?.GetTreeAttribute("maturationData");

            barrel.Api.Logger.Notification(
                "[Angel's Share] Before conversion: stack={0}, hasTree={1}, ageDays={2:F2}, quality={3:F2}, intensity={4:F1}, smoothness={5:F1}, maturity={6:F3}, tier={7}, special={8}",
                liquidStack.Collectible.Code,
                oldTree != null,
                oldTree?.GetDouble("ageDays", 0.0) ?? -1,
                oldTree?.GetDouble("quality", 0.0) ?? -1,
                oldTree?.GetDouble("intensity", 0.0) ?? -1,
                oldTree?.GetDouble("smoothness", 0.0) ?? -1,
                oldTree?.GetDouble("maturityRatio", 0.0) ?? -1,
                oldTree?.GetString("ageTier", "missing") ?? "missing",
                oldTree?.GetString("specialStyle", "") ?? ""
            );

            bool converted = BarrelAgingUtil.ConvertAgingSpiritOnUnseal(barrel.Api, liquidSlot);

            if (converted)
            {
                barrel.Sealed = false;
                barrel.MarkDirty(true);
                barrel.Api.World.BlockAccessor.MarkBlockEntityDirty(barrel.Pos);
                barrel.Api.World.PlaySoundAt(new AssetLocation("sounds/block/barrelopen"), barrel.Pos.X, barrel.Pos.Y, barrel.Pos.Z, byPlayer);
            }

            __result = true;
            return false;
        }
    }
}