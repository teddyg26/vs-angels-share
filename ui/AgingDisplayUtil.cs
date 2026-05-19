using System;
using System.Text;

using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace AngelsShare
{
    public static class AgingDisplayUtil
    {
        public static bool HasFinalizedAgingData(ITreeAttribute tree)
        {
            return tree != null && tree.GetBool("angelsshareAged", false);
        }

        public static ITreeAttribute GetMaturationTree(Vintagestory.API.Common.ItemStack stack)
        {
            return stack?.Attributes?.GetTreeAttribute("maturationData");
        }

        private static string GetQualityBand(double quality)
        {
            if (quality >= 90.0) return "Excellent";
            if (quality >= 75.0) return "Very Good";
            if (quality >= 55.0) return "Good";
            if (quality >= 35.0) return "Developing";
            if (quality >= 15.0) return "Young";
            return "New";
        }

        public static void AppendProjectedBarrelHud(StringBuilder dsc, AgingSnapshot projected)
        {
            if (projected == null) return;

            dsc.AppendLine();
            dsc.AppendLine("[Angel's Share: Maturation Tracker]");
            dsc.AppendLine("- Maturation: " + projected.MaturationDescriptor);
            dsc.AppendLine(string.Format("- Quality: ", GetQualityBand(projected.Quality)));
            dsc.AppendLine(string.Format("- Intensity: {0:F0}", projected.Intensity));
            dsc.AppendLine(string.Format("- Smoothness: {0:F0}", projected.Smoothness));
            dsc.AppendLine("- Climate Style: " + GetDisplayClimateStyle(projected.ClimateStyle));

            if (projected.MaturationDescriptor == "Heavy Oak" || projected.MaturationDescriptor == "Over-Oaked")
            {
                dsc.AppendLine("- Warning: heavy wood extraction is developing.");
            }

            dsc.AppendLine("- Sneak-right-click to end aging.");
        }

        public static void AppendFinalizedShort(StringBuilder dsc, ITreeAttribute tree)
        {
            if (tree == null) return;

            string tier = GetDisplayTier(tree);
            string specialStyle = tree.GetString("specialStyle", "");
            double quality = tree.GetDouble("quality", 0.0);
            string maturation = tree.GetString("maturationDescriptor", "Unknown");

            dsc.AppendLine();
            dsc.AppendLine("[Angel's Share]");
            dsc.AppendLine("- " + Lang.Get(BarrelAgingUtil.GetLangKeyForAgeTier(tier)));

            if (specialStyle.Length > 0)
            {
                dsc.AppendLine("- " + specialStyle);
            }

            dsc.AppendLine(string.Format("- Quality: {0:F1}%", quality));
            dsc.AppendLine("- Maturation: " + maturation);
            dsc.AppendLine("Hold Shift before hovering for more details.");
        }

        public static void AppendFinalizedDetailed(StringBuilder dsc, ITreeAttribute tree)
        {
            if (tree == null) return;

            string tier = tree.GetString("ageTier", "unknown");
            string tierName = GetDisplayTier(tree);
            string specialStyle = tree.GetString("specialStyle", "");

            double ageDays = tree.GetDouble("ageDays", 0.0);
            double quality = tree.GetDouble("quality", 0.0);
            double intensity = tree.GetDouble("intensity", 0.0);
            double smoothness = tree.GetDouble("smoothness", 0.0);
            double proof = tree.GetDouble("proof", 0.0);

            string maturation = tree.GetString("maturationDescriptor", "Unknown");
            string climateStyle = tree.GetString("climateStyle", "Standard Continental Maturation");

            dsc.AppendLine();
            dsc.AppendLine("[Angel's Share: Barrel Maturation]");
            dsc.AppendLine("- Tier: " + tierName);
            dsc.AppendLine(string.Format("- Quality: {0:F1}%", quality));
            dsc.AppendLine("- Maturation: " + maturation);
            dsc.AppendLine(string.Format("- Time Matured: {0:F1} days", ageDays));
            dsc.AppendLine(string.Format("- Intensity: {0:F0}", intensity));
            dsc.AppendLine(string.Format("- Smoothness: {0:F0}", smoothness));
            dsc.AppendLine("- Climate Style: " + GetDisplayClimateStyle(climateStyle));

            if (tier == "over-oaked")
            {
                dsc.AppendLine("- Condition: Bitter, excessive wood extraction.");
            }
        }

        public static void AppendDebug(StringBuilder dsc, ITreeAttribute tree)
        {
            if (tree == null) return;

            dsc.AppendLine();
            dsc.AppendLine("[Angel's Share Debug]");
            dsc.AppendLine("safeWindowDays: " + tree.GetDouble("safeWindowDays", -1));
            dsc.AppendLine("maturityRatio: " + tree.GetDouble("maturityRatio", -1));
            dsc.AppendLine("overAgeRatio: " + tree.GetDouble("overAgeRatio", -1));
            dsc.AppendLine("caskTrait: " + tree.GetString("caskTrait", "none"));
            dsc.AppendLine("caskVarianceSeed: " + tree.GetDouble("caskVarianceSeed", -1));
            dsc.AppendLine("agedFrom: " + tree.GetString("agedFrom", "none"));
            dsc.AppendLine("agedInto: " + tree.GetString("agedInto", "none"));
        }

        public static string GetAgedSpiritDisplayName(Vintagestory.API.Common.ItemStack liquidStack, ITreeAttribute tree)
        {
            if (liquidStack?.Collectible?.Code == null || tree == null)
                return "Aged Spirit";

            string path = liquidStack.Collectible.Code.Path;
            string variant = path.Contains("-")
                ? path.Substring(path.LastIndexOf('-') + 1)
                : "spirit";

            string prettyVariant = char.ToUpperInvariant(variant[0]) + variant.Substring(1);

            double proof = tree.GetDouble("proof", 0.0);
            string tier = tree.GetString("ageTier", "aged");

            if (proof > 0.0 && tier == "reserve")
                return string.Format("{0:F0} Proof {1} Whiskey", proof, prettyVariant);

            if (tier == "aged" || tier == "reserve")
                return "Aged " + prettyVariant + " Whiskey";

            if (tier == "over-oaked")
                return "Over-Oaked " + prettyVariant + " Whiskey";

            return prettyVariant + " Whiskey";
        }

        public static void AppendFinalizedBarrelGuiCompact(StringBuilder dsc, ITreeAttribute tree)
        {
            if (tree == null) return;

            string tier = tree.GetString("ageTier", "unknown");
            string tierName = GetDisplayTier(tree);

            string specialStyle = tree.GetString("specialStyle", "");
            string maturation = tree.GetString("maturationDescriptor", "Unknown");

            double quality = tree.GetDouble("quality", 0.0);
            double ageDays = tree.GetDouble("ageDays", 0.0);
            double intensity = tree.GetDouble("intensity", 0.0);
            double smoothness = tree.GetDouble("smoothness", 0.0);
            double proof = tree.GetDouble("proof", 0.0);

            dsc.AppendLine();
            dsc.AppendLine("Angel's Share:");
            dsc.AppendLine(string.Format("{0} · Quality {1:F0}%", tierName, quality));

            if (specialStyle.Length > 0)
            {
                if (specialStyle.Contains("Cask-Strength") && proof > 0.0)
                {
                    dsc.AppendLine(string.Format("Cask-Strength · {0:F0} proof", proof));
                }
                else
                {
                    dsc.AppendLine(specialStyle);
                }
            }
            else
            {
                string character = GetShortCharacter(intensity, smoothness);
                dsc.AppendLine(maturation + " · " + character);
            }

            dsc.AppendLine(string.Format("{0:F1} days matured", ageDays));
        }

        private static string GetShortCharacter(double intensity, double smoothness)
        {
            if (intensity >= 75.0 && smoothness >= 75.0)
                return "Balanced";

            if (intensity >= 75.0)
                return "Intense";

            if (smoothness >= 75.0)
                return "Smooth";

            if (intensity < 35.0 && smoothness < 35.0)
                return "Dull";

            return "Developed";
        }

        private static string GetDisplayClimateStyle(string climateStyle)
        {
            switch (climateStyle)
            {
                case "Hot Dry Fast-Maturation":
                    return "Hot Dry Cellar Maturation";

                case "Hot Humid Tropical Maturation":
                    return "Tropical Cellar Maturation";

                case "Cool Humid Slow-Aged":
                    return "Cool Damp Cellar Maturation";

                case "Cool Dry Concentrated":
                    return "Cold Dry Cellar Maturation";

                case "Humid Continental Maturation":
                    return "Damp Cellar Maturation";

                case "Dry Continental Maturation":
                    return "Dry Cellar Maturation";

                case "Standard Continental Maturation":
                default:
                    return "Continental Cellar Maturation";
            }
        }

        private static string GetDisplayTier(ITreeAttribute tree)
        {
            string tier = tree.GetString("ageTier", "unknown");
            string specialStyle = tree.GetString("specialStyle", "");

            if (!string.IsNullOrEmpty(specialStyle))
                return specialStyle;

            return Lang.Get(BarrelAgingUtil.GetLangKeyForAgeTier(tier));
        }
    }
}