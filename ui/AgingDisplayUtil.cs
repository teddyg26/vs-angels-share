using System;
using System.Text;
using Vintagestory.API.Common;
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

        public static string GetQualityBand(double quality)
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

            string tier = tree.GetString("ageTier", "unknown");
            string displayTier = GetDisplayTier(tree);
            string specialStyle = tree.GetString("specialStyle", "");
            double quality = tree.GetDouble("quality", 0.0);
            string maturation = tree.GetString("maturationDescriptor", "Unknown");

            dsc.AppendLine();
            dsc.AppendLine("[Angel's Share]");
            dsc.AppendLine("- " + displayTier);
            dsc.AppendLine(string.Format("- Quality: {0:F1}%", quality));
            dsc.AppendLine("- Maturation: " + maturation);
            dsc.AppendLine("Hold Shift before hovering for more details.");
        }

        public static void AppendFinalizedDetailed(StringBuilder dsc, ITreeAttribute tree)
        {
            if (tree == null) return;

            string tier = tree.GetString("ageTier", "unknown");
            string displayTier = GetDisplayTier(tree);
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
            dsc.AppendLine("- Tier: " + displayTier);
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

        private static string StripLiquidPrefix(string langPath)
        {
            if (langPath.StartsWith("liquid-"))
            {
                return langPath.Substring("liquid-".Length);
            }

            return langPath;
        }

        private static bool IsMissingLangValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            if (value.StartsWith("game:") || value.StartsWith("angels-share:"))
            {
                return true;
            }

            if (value.StartsWith("item-") || value.StartsWith("incontainer-item-"))
            {
                return true;
            }

            return false;
        }

        private static string NormalizeSpiritDisplayName(string localized)
        {
            if (string.IsNullOrEmpty(localized))
                return "Spirit";
            
            localized = StripAgingPrefix(localized);

            int openParen = localized.IndexOf('(');
            int closeParen = localized.IndexOf(')');

            if (openParen >= 0 && closeParen > openParen)
            {
                string baseName = localized.Substring(0, openParen).Trim();
                string variant = localized.Substring(openParen + 1, closeParen - openParen - 1).Trim();

                if (!string.IsNullOrEmpty(baseName) && !string.IsNullOrEmpty(variant))
                {
                    return variant + " " + baseName;
                }
            }

            return localized;
        }

        private static string StripAgingPrefix(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "Spirit";
            
            if (name.StartsWith("Aged "))
            {
                return name.Substring("Aged ".Length);
            }

            if (name.StartsWith("White "))
            {
                return name.Substring("White ".Length);
            }

            if (name.StartsWith("White / Unaged "))
            {
                return name.Substring("White / Unaged ".Length);
            }

            return name;
        }

        private static string FallbackNameFromPath(string langPath)
        {
            string value = langPath;

            int slashIndex = value.LastIndexOf('/');
            if (slashIndex >= 0 && slashIndex < value.Length - 1)
                value = value.Substring(slashIndex + 1);

            value = value.Replace("Liquid-", "");
            value = value.Replace("spiritportion-", "");
            value = value.Replace("whitespiritportion-", "");
            value = value.Replace("ginportion-", "");

            if (string.IsNullOrEmpty(value))
                return "Spirit";

            return ToTitleCaseWords(value) + " Spirit";
        }

        private static string ToTitleCaseWords(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "Unknown";

            string[] parts = value.Split('-');

            for (int i = 0 ; i < parts.Length; i++)
            {
                if (parts[i].Length == 0)
                    continue;
                
                if (parts[i].Length == 1)
                {
                    parts[i] = parts[i].ToUpperInvariant();
                }
                else
                {
                    parts[i] =
                        char.ToUpperInvariant(parts[i][0]) +
                        parts[i].Substring(1).ToLowerInvariant();
                }
            }

            return string.Join(" ", parts);
        }

        private static string GetSpiritNameFromVariant(ItemStack liquidStack)
        {
            if (liquidStack?.Collectible?.Code == null)
                return "Spirit";

            AssetLocation code = liquidStack.Collectible.Code;

            string domain = code.Domain;
            string path = code.Path;

            // Flatten lang key from liquid/spiritportion to liquid-spiritportion
            string langPath = path.Replace("/", "-");

            // If called on whitespirit, turn into spirit (this shouldn't happen?)
            langPath = langPath.Replace("whitespiritportion-", "spiritportion-");

            string localized = Lang.GetMatching(domain + ":incontainer-item-" + StripLiquidPrefix(langPath));

            if (IsMissingLangValue(localized))
            {
                localized = Lang.GetMatching(domain + ":item-" + langPath);
            }

            if (IsMissingLangValue(localized))
            {
                localized = Lang.GetMatching("game:incontainer-item-" + StripLiquidPrefix(langPath));
            }

            if (IsMissingLangValue(localized))
            {
                localized = Lang.GetMatching("game:item-" + langPath);
            }

            if (IsMissingLangValue(localized))
            {
                return FallbackNameFromPath(langPath);
            }

            return NormalizeSpiritDisplayName(localized);
        }

        public static string GetAgedSpiritDisplayName(ItemStack liquidStack, ITreeAttribute tree)
        {
            if (liquidStack?.Collectible?.Code == null)
                return "Aged Spirit";

            string baseSpiritName = GetSpiritNameFromVariant(liquidStack);

            if (tree == null)
                return baseSpiritName;

            string specialStyle = tree.GetString("specialStyle", "");
            double proof = tree.GetDouble("proof", 0.0);
            double ageStatementYears = tree.GetDouble("ageStatementYears", 0.0);

            bool isCaskStrength =
                proof > 0.0 &&
                specialStyle.Contains("Cask-Strength");

            bool hasAgeStatement =
                ageStatementYears >= 8.0;

            if (isCaskStrength && hasAgeStatement)
            {
                return string.Format(
                    "{0:F0} Proof {1}-Year Old {2}",
                    proof,
                    (int)ageStatementYears,
                    baseSpiritName
                );
            }

            if (isCaskStrength)
            {
                return string.Format(
                    "{0:F0} Proof {1}",
                    proof,
                    baseSpiritName
                );
            }

            if (hasAgeStatement)
            {
                return string.Format(
                    "{0}-Year Old {1}",
                    (int)ageStatementYears,
                    baseSpiritName
                );
            }

            return "Aged " + baseSpiritName;
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
                    dsc.AppendLine(string.Format("{0:F0} proof", proof));
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