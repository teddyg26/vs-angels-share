using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace AngelsShare
{
    public static class AgingDisplayUtil
    {
        public static bool TryGetMaturationRecord(ItemStack stack, out MaturationRecord record)
        {
            return MaturationRecordCodec.TryRead(stack, out record);
        }

        public static bool HasFinalizedAgingData(MaturationRecord record)
        {
            return record?.FinalizedProduct != null;
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
            dsc.AppendLine("- Quality: " + GetQualityBand(projected.Quality));
            dsc.AppendLine(string.Format("- Intensity: {0:F0}", projected.Intensity));
            dsc.AppendLine(string.Format("- Smoothness: {0:F0}", projected.Smoothness));
            dsc.AppendLine("- Climate Style: " + GetDisplayClimateStyle(projected.ClimateStyle));

            if (projected.MaturationDescriptor == "Heavy Oak" || projected.MaturationDescriptor == "Over-Oaked")
            {
                dsc.AppendLine("- Warning: heavy wood extraction is developing.");
            }

            dsc.AppendLine("- Sneak-right-click to end aging.");
        }

        public static void AppendFinalizedShort(StringBuilder dsc, MaturationRecord record)
        {
            FinalizedMaturationProduct product = record?.FinalizedProduct;
            MaturationOutcome outcome = product?.Outcome;
            if (outcome == null) return;

            string displayTier = GetDisplayTier(outcome);

            dsc.AppendLine();
            dsc.AppendLine("[Angel's Share]");
            dsc.AppendLine("- " + displayTier);
            dsc.AppendLine(string.Format("- Quality: {0:F1}%", outcome.Quality));
            dsc.AppendLine("- Maturation: " + (outcome.MaturationStageCode ?? "Unknown"));
            dsc.AppendLine("Hold Shift before hovering for more details.");
        }

        public static void AppendFinalizedDetailed(StringBuilder dsc, MaturationRecord record)
        {
            FinalizedMaturationProduct product = record?.FinalizedProduct;
            MaturationOutcome outcome = product?.Outcome;
            if (outcome == null) return;

            dsc.AppendLine();
            dsc.AppendLine("[Angel's Share: Barrel Maturation]");
            dsc.AppendLine("- Tier: " + GetDisplayTier(outcome));
            dsc.AppendLine(string.Format("- Quality: {0:F1}%", outcome.Quality));
            dsc.AppendLine("- Maturation: " + (outcome.MaturationStageCode ?? "Unknown"));
            dsc.AppendLine(string.Format(
                "- Actual time sealed: {0:F1} days",
                product.TotalActualElapsedHours / 24.0
            ));
            dsc.AppendLine(string.Format(
                "- Effective maturation: {0:F1} days",
                product.TotalEffectiveMaturationHours / 24.0
            ));
            dsc.AppendLine(string.Format("- Intensity: {0:F0}", outcome.Intensity));
            dsc.AppendLine(string.Format("- Smoothness: {0:F0}", outcome.Smoothness));
            dsc.AppendLine("- Climate Style: " + GetDisplayClimateStyle(outcome.ClimateStyleCode));

            if (outcome.TierCode == "over-oaked")
            {
                dsc.AppendLine("- Condition: Bitter, excessive wood extraction.");
            }
        }

        public static void AppendDebug(StringBuilder dsc, MaturationRecord record)
        {
            if (record == null) return;

            dsc.AppendLine();
            dsc.AppendLine("[Angel's Share Debug]");
            dsc.AppendLine("schemaVersion: " + record.SchemaVersion);
            dsc.AppendLine("state: " + record.State);
            dsc.AppendLine("completedSessions: " + (record.CompletedSessions?.Count ?? 0));

            if (record.ActiveSession != null)
            {
                ActiveMaturationSession session = record.ActiveSession;
                dsc.AppendLine("activeSequence: " + session.Sequence);
                dsc.AppendLine("sealedAtCalendarHours: " + session.SealedAtCalendarHours);
                dsc.AppendLine("lastIntegratedAtCalendarHours: " + session.LastIntegratedAtCalendarHours);
                dsc.AppendLine("actualElapsedHours: " + session.ActualElapsedHours);
                dsc.AppendLine("effectiveMaturationHours: " + session.EffectiveMaturationHours);
                dsc.AppendLine("caskProfile: " + (session.Cask?.ProfileCode ?? "none"));
                dsc.AppendLine("caskRollSeed: " + (session.Cask?.RollSeed ?? 0));
            }

            if (record.FinalizedProduct != null)
            {
                FinalizedMaturationProduct product = record.FinalizedProduct;
                dsc.AppendLine("totalActualElapsedHours: " + product.TotalActualElapsedHours);
                dsc.AppendLine("totalEffectiveMaturationHours: " + product.TotalEffectiveMaturationHours);
                dsc.AppendLine("startingVolumeLitres: " + product.Volume.StartingVolumeLitres);
                dsc.AppendLine("currentVolumeLitres: " + product.Volume.CurrentVolumeLitres);
                dsc.AppendLine("productLiquidCode: " + product.ProductLiquidCode);
            }
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

        private static string GetLocalizedSpiritName(ItemStack liquidStack)
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

            return localized;
        }

        public static string GetSpiritDisplayName(ItemStack liquidStack)
        {
            return NormalizeSpiritDisplayName(GetLocalizedSpiritName(liquidStack));
        }

        public static string NormalizeSpiritNameInText(string text, ItemStack liquidStack)
        {
            if (string.IsNullOrEmpty(text) || liquidStack?.Collectible?.Code == null)
                return text;

            string rawName = GetLocalizedSpiritName(liquidStack);
            string normalizedName = NormalizeSpiritDisplayName(rawName);

            if (
                string.IsNullOrEmpty(rawName) ||
                string.IsNullOrEmpty(normalizedName) ||
                rawName == normalizedName
            )
            {
                return text;
            }

            return text.Replace(rawName, normalizedName);
        }

        public static string GetAgedSpiritDisplayName(ItemStack liquidStack, MaturationRecord record)
        {
            if (liquidStack?.Collectible?.Code == null)
                return "Aged Spirit";

            string baseSpiritName = GetSpiritDisplayName(liquidStack);
            MaturationOutcome outcome = record?.FinalizedProduct?.Outcome;

            if (outcome == null)
                return baseSpiritName;

            return FormatAgedSpiritDisplayName(baseSpiritName, outcome);
        }

        public static string FormatAgedSpiritDisplayName(
            string baseSpiritName,
            MaturationOutcome outcome
        )
        {
            if (string.IsNullOrEmpty(baseSpiritName)) baseSpiritName = "Spirit";
            if (outcome == null) return baseSpiritName;

            double proof = MaturationRecordCodec.GetDesignationValue(
                outcome,
                "angels-share:cask-strength"
            );
            double ageStatementYears = MaturationRecordCodec.GetDesignationValue(
                outcome,
                "angels-share:age-stated"
            );

            bool isCaskStrength =
                proof > 0.0 &&
                MaturationRecordCodec.HasDesignation(outcome, "angels-share:cask-strength");

            bool hasAgeStatement =
                ageStatementYears >= 8.0;

            if (isCaskStrength && hasAgeStatement)
            {
                return string.Format(
                    "{0:F0} Proof {1}-Year {2}",
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
                    "{0}-Year {1}",
                    (int)ageStatementYears,
                    baseSpiritName
                );
            }

            string tierOrSpecial = GetCompactTierOrSpecial(outcome);

            if (string.IsNullOrEmpty(tierOrSpecial))
                tierOrSpecial = "Aged";

            return tierOrSpecial + " " + baseSpiritName;
        }

        private static string GetCompactTierOrSpecial(MaturationOutcome outcome)
        {
            string designation = GetDisplayDesignation(outcome);

            if (!string.IsNullOrEmpty(designation))
                return designation;

            switch (outcome?.TierCode)
            {
                case "reserve":
                    return "Reserve";

                case "over-oaked":
                    return "Over-oaked";

                case "young":
                    return "Young";

                case "rested":
                    return "Rested";

                case "white":
                    return "White / Unaged";

                case "aged":
                default:
                    return "Aged";
            }
        }

        public static void AppendFinalizedBarrelGuiCompact(
            StringBuilder dsc,
            ItemStack liquidStack,
            MaturationRecord record
        )
        {
            if (record?.FinalizedProduct?.Outcome == null) return;

            dsc.AppendLine();
            dsc.AppendLine("Angel's Share:");
            dsc.AppendLine(GetAgedSpiritDisplayName(liquidStack, record));
        }

        public static string GetDesignationSummary(MaturationOutcome outcome)
        {
            if (outcome?.Designations == null || outcome.Designations.Count == 0)
                return string.Empty;

            string[] codes = new string[outcome.Designations.Count];

            for (int i = 0; i < outcome.Designations.Count; i++)
            {
                codes[i] = outcome.Designations[i]?.Code ?? string.Empty;
            }

            return string.Join(",", codes);
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

        private static string GetDisplayTier(MaturationOutcome outcome)
        {
            if (outcome == null) return Lang.Get(BarrelAgingUtil.GetLangKeyForAgeTier("white"));

            string designation = GetDisplayDesignation(outcome);

            if (!string.IsNullOrEmpty(designation))
                return designation;

            return Lang.Get(BarrelAgingUtil.GetLangKeyForAgeTier(outcome.TierCode));
        }

        private static string GetDisplayDesignation(MaturationOutcome outcome)
        {
            bool ageStated = MaturationRecordCodec.GetDesignationValue(
                outcome,
                "angels-share:age-stated"
            ) >= 8.0;
            bool caskStrength = MaturationRecordCodec.HasDesignation(
                outcome,
                "angels-share:cask-strength"
            );

            if (ageStated && caskStrength)
                return "Age-Stated Cask-Strength Reserve";

            if (ageStated)
                return "Age-Stated Reserve";

            if (caskStrength)
                return "Cask-Strength Reserve";

            if (outcome?.Designations != null)
            {
                foreach (MaturationDesignation designation in outcome.Designations)
                {
                    if (designation?.Code != "angels-share:legacy-special") continue;

                    string displayName = designation.Extensions?.GetString("displayName", string.Empty);
                    if (!string.IsNullOrEmpty(displayName)) return displayName;
                }
            }

            return string.Empty;
        }
    }
}
