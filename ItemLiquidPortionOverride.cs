using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace AngelsShare
{
    public class ItemLiquidPortionOverride : Item
    {
        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            ItemStack stack = inSlot.Itemstack;

            if (stack?.Attributes == null)
            {
                return;
            }

            ITreeAttribute maturationTree = stack.Attributes.GetTreeAttribute("maturationData");

            if (maturationTree == null)
            {
                return;
            }

            double ageDays = maturationTree.GetDouble("ageDays", 0.0);
            double quality = maturationTree.GetDouble("quality", 0.0);
            double intensity = maturationTree.GetDouble("intensity", 0.0);
            double smoothness = maturationTree.GetDouble("smoothness", 0.0);
            double maturityRatio = maturationTree.GetDouble("maturityRatio", 0.0);

            string tier = maturationTree.GetString(
                "ageTier",
                BarrelAgingUtil.GetAgeTierFromMaturity(stack, maturityRatio, quality, intensity, smoothness)
            );

            if (ageDays <= 0.01 && !maturationTree.GetBool("angelsshareAged", false))
            {
                return;
            }

            string langKey = BarrelAgingUtil.GetLangKeyForAgeTier(tier);

            double totalHoursReal = maturationTree.GetDouble("ageHoursTotal", 0.0);
            double totalDaysReal = totalHoursReal / 24.0;

            double averageTemperature = maturationTree.GetDouble("averageTemperature", 20.0);
            double averageRainfall = maturationTree.GetDouble("averageRainfall", 0.5);
            double averageHumidityModifier = maturationTree.GetDouble("averageHumidityModifier", 1.0);

            string climateStyle = maturationTree.GetString("climateStyle", "Standard Continental Maturation");
            string maturationDescriptor = maturationTree.GetString("maturationDescriptor", "Unknown");
            string specialStyle = maturationTree.GetString("specialStyle", "");

            double proof = maturationTree.GetDouble("proof", 0.0);
            double ageStatementYears = maturationTree.GetDouble("ageStatementYears", 0.0);

            dsc.AppendLine();
            dsc.AppendLine("--- Barrel Maturation ---");
            dsc.AppendLine(Lang.Get(langKey));

            if (specialStyle.Length > 0)
            {
                dsc.AppendLine(specialStyle);
            }

            dsc.AppendLine(string.Format("Quality: {0:F1}%", quality));
            dsc.AppendLine("Maturation: " + maturationDescriptor);
            dsc.AppendLine(string.Format("Time Matured: {0:F1} Days", ageDays));
            dsc.AppendLine(string.Format("Intensity: {0:F0}", intensity));
            dsc.AppendLine(string.Format("Smoothness: {0:F0}", smoothness));
            dsc.AppendLine("Climate Style: " + climateStyle);

            if (proof > 0.0)
            {
                dsc.AppendLine(string.Format("Estimated Strength: {0:F0} proof", proof));
            }

            if (ageStatementYears >= 1.0 && specialStyle.Contains("Year Old"))
            {
                dsc.AppendLine(string.Format("Age Statement: {0:F0} years", ageStatementYears));
            }

            if (totalDaysReal > 0.01)
            {
                dsc.AppendLine(string.Format("Actual Time in Barrel: {0:F1} Days", totalDaysReal));
            }

            if (tier == "over-oaked")
            {
                dsc.AppendLine("Condition: Bitter, excessive wood extraction");
            }

            if (withDebugInfo)
            {
                dsc.AppendLine();
                dsc.AppendLine("[Angel's Share Debug]");
                dsc.AppendLine("agedFrom: " + maturationTree.GetString("agedFrom", "none"));
                dsc.AppendLine("agedInto: " + maturationTree.GetString("agedInto", "none"));
                dsc.AppendLine("sealedAtTotalHours: " + maturationTree.GetDouble("sealedAtTotalHours", -1));
                dsc.AppendLine("unsealedAtTotalHours: " + maturationTree.GetDouble("unsealedAtTotalHours", -1));

                dsc.AppendLine("safeWindowDays: " + maturationTree.GetDouble("safeWindowDays", -1));
                dsc.AppendLine("maturityRatio: " + maturityRatio);
                dsc.AppendLine("overAgeRatio: " + maturationTree.GetDouble("overAgeRatio", 0.0));

                dsc.AppendLine("caskTrait: " + maturationTree.GetString("caskTrait", "none"));
                dsc.AppendLine("caskVarianceSeed: " + maturationTree.GetDouble("caskVarianceSeed", -1));
                dsc.AppendLine("caskIntensityBonus: " + maturationTree.GetDouble("caskIntensityBonus", 0.0));
                dsc.AppendLine("caskSmoothnessBonus: " + maturationTree.GetDouble("caskSmoothnessBonus", 0.0));
                dsc.AppendLine("caskQualityBonus: " + maturationTree.GetDouble("caskQualityBonus", 0.0));
                dsc.AppendLine("caskSafeWindowMultiplier: " + maturationTree.GetDouble("caskSafeWindowMultiplier", 1.0));
                dsc.AppendLine("caskOverOakResistance: " + maturationTree.GetDouble("caskOverOakResistance", 1.0));

                dsc.AppendLine("averageHumidityModifier: " + averageHumidityModifier);
            }
        }
    }
}