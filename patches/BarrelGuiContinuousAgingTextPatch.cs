using HarmonyLib;
using System;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AngelsShare
{
    [HarmonyPatch]
    public static class BarrelGuiContinuousAgingTextPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(GuiDialogBarrel), "getContentsText");
        }

        [HarmonyPostfix]
        public static void Postfix(GuiDialogBarrel __instance, ref string __result)
        {
            try
            {
                BlockPos pos = AccessTools.Field(typeof(GuiDialogBlockEntity), "BlockEntityPosition")
                    ?.GetValue(__instance) as BlockPos;

                if (pos == null) return;

                ICoreClientAPI capi = AccessTools.Field(typeof(GuiDialog), "capi")
                    ?.GetValue(__instance) as ICoreClientAPI;

                if (capi == null) return;

                BlockEntityBarrel barrel = capi.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityBarrel;

                if (barrel == null || barrel.CurrentRecipe == null) return;

                ItemSlot liquidSlot = barrel.Inventory[BarrelAgingUtil.LiquidSlotId];

                if (liquidSlot?.Itemstack == null) return;

                if (!BarrelAgingUtil.IsAgeableSpirit(liquidSlot.Itemstack)) return;

                ItemStack outStack = barrel.CurrentRecipe.RecipeOutput.ResolvedItemStack;
                WaterTightContainableProps props = BlockLiquidContainerBase.GetContainableProps(outStack);

                if (props == null) return;

                string incontainername = Lang.Get(
                    outStack.Collectible.Code.Domain
                    + ":incontainer-"
                    + outStack.Class.ToString().ToLowerInvariant()
                    + "-"
                    + outStack.Collectible.Code.Path
                );

                float litres = (float)barrel.CurrentOutSize / props.ItemsPerLitre;

                string oldTextStart = "Will turn into ";
                int oldTextIndex = __result.IndexOf(oldTextStart, StringComparison.Ordinal);

                if (oldTextIndex < 0) return;

                string before = __result.Substring(0, oldTextIndex);

                string replacement = Lang.Get(
                    "angels-share:barrel-will-age-into-after-sealing",
                    litres,
                    incontainername
                );

                __result = before + replacement;
            }
            catch
            {
                // Avoid breaking the barrel GUI if reflection fails.
            }
        }
    }
}