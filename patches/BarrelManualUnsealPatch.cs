using AngelsShare;
using HarmonyLib;
using Vintagestory.API.Common;
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

            AgingDisplayUtil.TryGetMaturationRecord(liquidStack, out MaturationRecord record);
            FinalizedMaturationProduct product = record?.FinalizedProduct;
            MaturationOutcome outcome = product?.Outcome;

            barrel.Api.Logger.Notification(
                "[Angel's Share] Before conversion: stack={0}, schemaVersion={1}, state={2}, effectiveDays={3:F2}, actualDays={4:F2}, quality={5:F2}, intensity={6:F1}, smoothness={7:F1}, tier={8}, designations={9}",
                liquidStack.Collectible.Code,
                record?.SchemaVersion ?? 0,
                record?.State.ToString() ?? "missing",
                (product?.TotalEffectiveMaturationHours ?? 0.0) / 24.0,
                (product?.TotalActualElapsedHours ?? 0.0) / 24.0,
                outcome?.Quality ?? -1.0,
                outcome?.Intensity ?? -1.0,
                outcome?.Smoothness ?? -1.0,
                outcome?.TierCode ?? "missing",
                AgingDisplayUtil.GetDesignationSummary(outcome)
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
