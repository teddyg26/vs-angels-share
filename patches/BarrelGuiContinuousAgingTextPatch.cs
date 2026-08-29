using HarmonyLib;
using System;
using System.Reflection;
using System.Text;
using System.Linq;

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
                if (__result.Contains("Angel's Share:")) return;

                BlockEntityBarrel barrel = TryGetBarrel(__instance);
                if (barrel == null)
                    return;

                ItemSlot liquidSlot = barrel.Inventory[BarrelAgingUtil.LiquidSlotId];
                if (liquidSlot?.Itemstack == null)
                    return;

                ItemStack liquidStack = liquidSlot.Itemstack;
                if (!AgingDisplayUtil.TryGetMaturationRecord(
                    liquidStack,
                    out MaturationRecord record
                ))
                    return;

                if (
                    barrel.Sealed &&
                    BarrelAgingUtil.IsAgeableSpirit(liquidStack) &&
                    record.ActiveSession != null
                )
                {
                    TryReplaceDummyRecipeText(barrel, ref __result);
                    AppendProjectedAgingText(barrel, liquidStack, ref __result);
                    return;
                }

                if (AgingDisplayUtil.HasFinalizedAgingData(record))
                    AppendFinalizedAgingText(record, ref __result);
            }
            catch
            {
                // Do not break barrel GUI if reflection fails.
            }
        }

        private static BlockEntityBarrel TryGetBarrel(GuiDialogBarrel dialog)
        {
            if (dialog == null)
            {
                return null;
            }

            BlockEntityBarrel directBarrel =
                FindFieldValue<BlockEntityBarrel>(dialog) ??
                FindPropertyValue<BlockEntityBarrel>(dialog);

            if (directBarrel != null)
            {
                return directBarrel;
            }

            BlockPos pos =
                FindFieldValue<BlockPos>(dialog) ??
                FindPropertyValue<BlockPos>(dialog);

            if (pos == null)
            {
                return null;
            }

            ICoreClientAPI capi =
                FindFieldValue<ICoreClientAPI>(dialog) ??
                FindPropertyValue<ICoreClientAPI>(dialog);

            if (capi == null)
            {
                return null;
            }

            return capi.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityBarrel;
        }

        private static T FindFieldValue<T>(object instance) where T : class
        {
            Type type = instance.GetType();

            while (type != null)
            {
                FieldInfo[] fields = type.GetFields(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic
                );

                foreach (FieldInfo field in fields)
                {
                    object value = null;

                    try
                    {
                        value = field.GetValue(instance);
                    }
                    catch
                    {
                        continue;
                    }

                    if (value is T typedValue)
                    {
                        return typedValue;
                    }
                }

                type = type.BaseType;
            }

            return null;
        }

        private static T FindPropertyValue<T>(object instance) where T : class
        {
            Type type = instance.GetType();

            while (type != null)
            {
                PropertyInfo[] properties = type.GetProperties(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic
                );

                foreach (PropertyInfo property in properties)
                {
                    if (!property.CanRead || property.GetIndexParameters().Length > 0)
                        continue;

                    object value = null;

                    try
                    {
                        value = property.GetValue(instance);
                    }
                    catch
                    {
                        continue;
                    }

                    if (value is T typedValue)
                    {
                        return typedValue;
                    }
                }

                type = type.BaseType;
            }

            return null;
        }

        private static void TryReplaceDummyRecipeText(BlockEntityBarrel barrel, ref string text)
        {
            if (barrel.CurrentRecipe == null) return;

            ItemStack outStack = barrel.CurrentRecipe.Output.ResolvedItemstack;
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
            int oldTextIndex = text.IndexOf(oldTextStart, StringComparison.Ordinal);

            if (oldTextIndex < 0) return;

            string before = text.Substring(0, oldTextIndex);

            string replacement = Lang.Get(
                "angels-share:barrel-will-age-into-after-sealing",
                litres,
                incontainername
            );

            text = before + replacement;
        }

        private static void AppendProjectedAgingText(BlockEntityBarrel barrel, ItemStack liquidStack, ref string text)
        {
            AgingSnapshot projected = BarrelAgingCalculator.GetProjectedAging(barrel, liquidStack);

            StringBuilder extra = new StringBuilder();
            AgingDisplayUtil.AppendProjectedBarrelHud(extra, projected);

            text = text.TrimEnd() + "\n" + extra.ToString();
        }

        private static void AppendFinalizedAgingText(MaturationRecord record, ref string text)
        {
            StringBuilder extra = new StringBuilder();
            AgingDisplayUtil.AppendFinalizedBarrelGuiCompact(extra, record);

            text = text.TrimEnd() + "\n" + extra.ToString();
        }
    }
}
