using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AngelsShare
{
    [HarmonyPatch(typeof(BlockBarrel), "GetPlacedBlockInfo")]
    public static class BarrelHudInfoPatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            IWorldAccessor world,
            BlockPos pos,
            IPlayer forPlayer,
            ref string __result
        )
        {
            if (world == null || pos == null) return;

            BlockEntityBarrel barrel = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityBarrel;

            if (barrel == null) return;

            string agingText = GetAgingHudText(barrel);

            if (string.IsNullOrEmpty(agingText)) return;

            if (__result != null && __result.Contains("[Angel's Share]")) return;

            if (string.IsNullOrEmpty(__result))
            {
                __result = agingText;
            }
            else
            {
                __result = __result.TrimEnd() + "\n" + agingText;
            }
        }

        private static string GetAgingHudText(BlockEntityBarrel barrel)
        {
            ItemSlot liquidSlot = barrel.Inventory[BarrelAgingUtil.LiquidSlotId];

            if (liquidSlot?.Itemstack == null) return null;

            ItemStack liquidStack = liquidSlot.Itemstack;
            ITreeAttribute tree = AgingDisplayUtil.GetMaturationTree(liquidStack);

            if (barrel.Sealed && BarrelAgingUtil.IsAgeableSpirit(liquidStack))
            {
                if (tree == null || !tree.HasAttribute("sealedAtTotalHours"))
                    return "[Angel's Share]\nMaturation: Starting\nSneak-right-click to end aging.";

                AgingSnapshot projected = BarrelAgingCalculator.GetProjectedAging(barrel, liquidStack);

                string text =
                    "[Angel's Share]\n" +
                    "Maturation: " + projected.MaturationDescriptor + "\n" +
                    "Quality: " + AgingDisplayUtil.GetQualityBand(projected.Quality) + "\n" +
                    "Character: " + GetHudCharacter(projected.Intensity, projected.Smoothness);

                if (
                    projected.MaturationDescriptor == "Heavy Oak" ||
                    projected.MaturationDescriptor == "Over-Oaked"
                )
                {
                    text += "\nWarning: heavy oak developing.";
                }

                text += "\nSneak-right-click to end aging.";

                return text;
            }

            if (tree != null && AgingDisplayUtil.HasFinalizedAgingData(tree))
            {
                return
                    "[Angel's Share]\n" +
                    "Maturation: " + tree.GetString("maturationDescriptor", "Unknown") + "\n" +
                    string.Format("Quality: {0:F0}%\n", tree.GetDouble("quality", 0.0)) +
                    "Character: " + GetHudCharacter(
                        tree.GetDouble("intensity", 0.0),
                        tree.GetDouble("smoothness", 0.0)
                    );
            }

            return null;
        }

        private static string GetHudCharacter(double intensity, double smoothness)
        {
            if (intensity >= 75.0 && smoothness >= 75.0)
                return "Balanced";

            if (intensity >= 75.0)
                return "Intense";

            if (smoothness >= 75.0)
                return "Smooth";

            if (intensity < 35.0 && smoothness < 35.0)
                return "Dull";

            return "Developing";
        }
    }
}