using AngelsShare;
using HarmonyLib;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace AngelsShare
{
    [HarmonyPatch(typeof(BlockEntity), "GetBlockInfo")]
    public static class BarrelBlockInfoPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntity __instance, IPlayer forPlayer, StringBuilder dsc)
        {
            if (__instance is not BlockEntityBarrel barrel) return;

            ItemSlot liquidSlot = barrel.Inventory[BarrelAgingUtil.LiquidSlotId];

            if (liquidSlot?.Itemstack == null) return;

            ItemStack liquidStack = liquidSlot.Itemstack;
            ITreeAttribute tree = liquidStack.Attributes.GetTreeAttribute("maturationData");

            if (barrel.Sealed && BarrelAgingUtil.IsAgeableSpirit(liquidStack))
            {
                if (tree == null || !tree.HasAttribute("sealedAtTotalHours"))
                {
                    dsc.AppendLine();
                    dsc.AppendLine("[Angel's Share: Maturation Tracker]");
                    dsc.AppendLine("- Aging data not initialized yet.");
                    dsc.AppendLine("- If you just sealed the barrel, close and reopen the barrel GUI.");
                    dsc.AppendLine("- Sneak-right-click to end aging.");
                    return;
                }

                AgingSnapshot projected = BarrelAgingCalculator.GetProjectedAging(barrel, liquidStack);

                dsc.AppendLine();
                dsc.AppendLine("[Angel's Share: Maturation Tracker]");
                dsc.AppendLine("- Maturation: " + projected.MaturationDescriptor);
                dsc.AppendLine(string.Format("- Quality: {0:F1}%", projected.Quality));
                dsc.AppendLine(string.Format("- Intensity: {0:F0}", projected.Intensity));
                dsc.AppendLine(string.Format("- Smoothness: {0:F0}", projected.Smoothness));
                dsc.AppendLine("- Climate Style: " + projected.ClimateStyle);

                if (projected.MaturationDescriptor == "Heavy Oak" || projected.MaturationDescriptor == "Over-Oaked")
                {
                    dsc.AppendLine("- Warning: heavy wood extraction is developing.");
                }

                dsc.AppendLine("- Sneak-right-click to end aging.");
                return;
            }

            if (tree != null && tree.GetBool("angelsshareAged", false))
            {
                double ageDays = tree.GetDouble("ageDays", 0.0);
                double quality = tree.GetDouble("quality", 0.0);
                double intensity = tree.GetDouble("intensity", 0.0);
                double smoothness = tree.GetDouble("smoothness", 0.0);
                string tier = tree.GetString("ageTier", BarrelAgingUtil.GetAgeTierFromMaturity(liquidStack, tree.GetDouble("maturityRatio", 0.0), quality, intensity, smoothness));
                string specialStyle = tree.GetString("specialStyle", "");

                dsc.AppendLine();
                dsc.AppendLine("[Angel's Share: Maturation]");
                dsc.AppendLine("- Tier: " + tier);

                if (specialStyle.Length > 0)
                {
                    dsc.AppendLine("- " + specialStyle);
                }

                dsc.AppendLine(string.Format("- Time Matured: {0:F2} days", ageDays));
                dsc.AppendLine(string.Format("- Quality: {0:F1}%", quality));
                dsc.AppendLine(string.Format("- Intensity: {0:F0}", intensity));
                dsc.AppendLine(string.Format("- Smoothness: {0:F0}", smoothness));
                dsc.AppendLine("- Climate Style: " + tree.GetString("climateStyle", "Standard Continental Maturation"));

                double proof = tree.GetDouble("proof", 0.0);

                if (proof > 0.0)
                {
                    dsc.AppendLine(string.Format("- Estimated Strength: {0:F0} proof", proof));
                }

                if (tier == "over-oaked")
                {
                    dsc.AppendLine("- Condition: Bitter, excessive wood extraction.");
                }
            }
        }
    }
}