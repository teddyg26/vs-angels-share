using HarmonyLib;
using Vintagestory.API.Common;
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
            AgingDisplayUtil.TryGetMaturationRecord(liquidStack, out MaturationRecord record);

            if (barrel.Sealed && BarrelAgingUtil.IsAgeableSpirit(liquidStack))
            {
                if (record?.ActiveSession == null)
                    return "[Angel's Share]\nMaturation: Starting\nSneak-right-click to end aging.";

                AgingSnapshot projected = BarrelAgingCalculator.GetProjectedAging(barrel, liquidStack);
                if (projected == null)
                    return "[Angel's Share]\nMaturation: Starting\nSneak-right-click to end aging.";

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

            if (AgingDisplayUtil.HasFinalizedAgingData(record))
            {
                MaturationOutcome outcome = record.FinalizedProduct.Outcome;
                return
                    "[Angel's Share]\n" +
                    "Maturation: " + (outcome.MaturationStageCode ?? "Unknown") + "\n" +
                    string.Format("Quality: {0:F0}%\n", outcome.Quality) +
                    "Character: " + GetHudCharacter(
                        outcome.Intensity,
                        outcome.Smoothness
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
