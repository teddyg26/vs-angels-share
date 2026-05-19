using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AngelsShare
{
    public static class BarrelAgingCalculator
    {
        public static void InitializeAgingOnSeal(BlockEntityBarrel barrel, ItemSlot liquidSlot, ItemStack liquidStack)
        {
            if (barrel?.Api == null || liquidSlot?.Itemstack == null || liquidStack == null) return;

            ICoreAPI api = barrel.Api;
            ITreeAttribute tree = liquidStack.Attributes.GetOrAddTreeAttribute("maturationData");

            if (tree.HasAttribute("sealedAtTotalHours")) return;

            double nowTotalHours = api.World.Calendar.ElapsedHours;

            BlockPos pos = barrel.Pos;
            ClimateCondition climate = api.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.NowValues);

            float currentTemp = climate != null ? climate.Temperature : 20f;
            float currentRainfall = climate != null ? climate.Rainfall : 0.5f;

            CaskProfile profile = RollCaskProfile(barrel, liquidStack, nowTotalHours);

            tree.SetDouble("sealedAtTotalHours", nowTotalHours);
            tree.SetDouble("startingTemperature", currentTemp);
            tree.SetDouble("startingRainfall", currentRainfall);

            tree.SetString("caskTrait", profile.Trait);
            tree.SetDouble("caskVarianceSeed", profile.CaskVariance);
            tree.SetDouble("caskIntensityBonus", profile.IntensityBonus);
            tree.SetDouble("caskSmoothnessBonus", profile.SmoothnessBonus);
            tree.SetDouble("caskQualityBonus", profile.QualityBonus);
            tree.SetDouble("caskSafeWindowMultiplier", profile.SafeWindowMultiplier);
            tree.SetDouble("caskOverOakResistance", profile.OverOakResistance);

            tree.SetDouble("ageHours", 0.0);
            tree.SetDouble("ageDays", 0.0);
            tree.SetDouble("ageHoursTotal", 0.0);
            tree.SetDouble("safeWindowDays", 60.0);
            tree.SetDouble("maturityRatio", 0.0);
            tree.SetDouble("overAgeRatio", 0.0);

            tree.SetDouble("quality", 0.0);
            tree.SetDouble("intensity", 0.0);
            tree.SetDouble("smoothness", 0.0);
            tree.SetDouble("balance", 0.0);

            tree.SetDouble("averageTemperature", currentTemp);
            tree.SetDouble("averageRainfall", currentRainfall);
            tree.SetDouble("averageHumidityModifier", GetHumidityModifier(currentRainfall));

            tree.SetString("climateStyle", GetClimateStyle(currentTemp, currentRainfall));
            tree.SetString("maturationDescriptor", "Raw");
            tree.SetString("ageTier", "white");
            tree.SetString("specialStyle", "");

            liquidSlot.MarkDirty();
            barrel.MarkDirty(true);
            api.World.BlockAccessor.MarkBlockEntityDirty(barrel.Pos);

            api.Logger.Notification(
                "[Angel's Share] Initialized aging for {0}: sealedAt={1:F2}, temp={2:F1}, rainfall={3:F2}, caskTrait={4}, caskVariance={5:F2}",
                liquidStack.Collectible.Code,
                nowTotalHours,
                currentTemp,
                currentRainfall,
                profile.Trait,
                profile.CaskVariance
            );
        }

        public static void FinalizeAgingOnUnseal(BlockEntityBarrel barrel, ItemSlot liquidSlot, ItemStack liquidStack)
        {
            if (barrel?.Api == null || liquidSlot?.Itemstack == null || liquidStack == null) return;

            ICoreAPI api = barrel.Api;
            ITreeAttribute tree = liquidStack.Attributes.GetOrAddTreeAttribute("maturationData");

            if (!tree.HasAttribute("sealedAtTotalHours"))
            {
                api.Logger.Warning(
                    "[Angel's Share] Finalizing {0}, but no sealedAtTotalHours existed. Initializing late; previous sealed time cannot be recovered.",
                    liquidStack.Collectible.Code
                );

                InitializeAgingOnSeal(barrel, liquidSlot, liquidStack);
            }

            double nowTotalHours = api.World.Calendar.ElapsedHours;
            double sealedAtTotalHours = tree.GetDouble("sealedAtTotalHours", nowTotalHours);

            CaskProfile profile = GetStoredCaskProfile(tree);

            AgingSnapshot result = CalculateIntegratedAging(
                barrel,
                liquidStack,
                sealedAtTotalHours,
                nowTotalHours,
                profile
            );

            WriteAgingResultToTree(tree, result, nowTotalHours);

            liquidSlot.MarkDirty();
            barrel.MarkDirty(true);
            api.World.BlockAccessor.MarkBlockEntityDirty(barrel.Pos);

            api.Logger.Notification(
                "[Angel's Share] Finalized aging for {0}: sealedAt={1:F2}, unsealedAt={2:F2}, totalHours={3:F2}, ageDays={4:F2}, safeWindow={5:F2}, maturity={6:F3}, quality={7:F2}, intensity={8:F1}, smoothness={9:F1}, avgTemp={10:F1}, avgRain={11:F2}, trait={12}, tier={13}, special={14}",
                liquidStack.Collectible.Code,
                sealedAtTotalHours,
                nowTotalHours,
                result.TotalHours,
                result.AgeDays,
                result.SafeWindowDays,
                result.MaturityRatio,
                result.Quality,
                result.Intensity,
                result.Smoothness,
                result.AverageTemperature,
                result.AverageRainfall,
                result.CaskTrait,
                result.Tier,
                result.SpecialStyle
            );
        }

        public static AgingSnapshot GetProjectedAging(BlockEntityBarrel barrel, ItemStack liquidStack)
        {
            ITreeAttribute tree = liquidStack.Attributes.GetOrAddTreeAttribute("maturationData");

            double nowTotalHours = barrel.Api.World.Calendar.ElapsedHours;
            double sealedAtTotalHours = tree.GetDouble("sealedAtTotalHours", nowTotalHours);

            CaskProfile profile = GetStoredCaskProfile(tree);

            return CalculateIntegratedAging(
                barrel,
                liquidStack,
                sealedAtTotalHours,
                nowTotalHours,
                profile
            );
        }

        private static void WriteAgingResultToTree(ITreeAttribute tree, AgingSnapshot result, double nowTotalHours)
        {
            tree.SetDouble("ageHours", result.AgeHours);
            tree.SetDouble("ageDays", result.AgeDays);
            tree.SetDouble("ageHoursTotal", result.TotalHours);

            tree.SetDouble("safeWindowDays", result.SafeWindowDays);
            tree.SetDouble("maturityRatio", result.MaturityRatio);
            tree.SetDouble("overAgeRatio", result.OverAgeRatio);

            tree.SetDouble("quality", result.Quality);
            tree.SetDouble("intensity", result.Intensity);
            tree.SetDouble("smoothness", result.Smoothness);
            tree.SetDouble("balance", result.Balance);

            tree.SetDouble("averageTemperature", result.AverageTemperature);
            tree.SetDouble("averageRainfall", result.AverageRainfall);
            tree.SetDouble("averageHumidityModifier", result.AverageHumidityModifier);

            tree.SetString("climateStyle", result.ClimateStyle);
            tree.SetString("maturationDescriptor", result.MaturationDescriptor);
            tree.SetString("ageTier", result.Tier);
            tree.SetString("specialStyle", result.SpecialStyle);

            tree.SetDouble("proof", result.Proof);
            tree.SetDouble("ageStatementYears", result.AgeStatementYears);

            tree.SetDouble("unsealedAtTotalHours", nowTotalHours);
        }

        private static double GetTemperatureSpeedMultiplier(double temp)
        {
            // Cold climates should age slower, but not become inert.
            // Hot climates should still accelerate maturation noticeably.
            double raw = Math.Pow(1.012, temp - 20.0);

            return Clamp(raw, 1.00, 1.80);
        }

        private static AgingSnapshot CalculateIntegratedAging(
            BlockEntityBarrel barrel,
            ItemStack liquidStack,
            double sealedAtTotalHours,
            double unsealedAtTotalHours,
            CaskProfile profile
        )
        {
            ICoreAPI api = barrel.Api;
            BlockPos pos = barrel.Pos;

            double totalHours = Math.Max(0.0, unsealedAtTotalHours - sealedAtTotalHours);

            if (totalHours <= 0.0)
            {
                return new AgingSnapshot
                {
                    TotalHours = 0.0,
                    AgeHours = 0.0,
                    AgeDays = 0.0,
                    SafeWindowDays = 60.0,
                    MaturityRatio = 0.0,
                    OverAgeRatio = 0.0,
                    Quality = 0.0,
                    Intensity = 0.0,
                    Smoothness = 0.0,
                    Balance = 0.0,
                    AverageTemperature = 20.0,
                    AverageRainfall = 0.5,
                    AverageHumidityModifier = 1.0,
                    ClimateStyle = "Standard Continental Maturation",
                    MaturationDescriptor = "Raw",
                    CaskTrait = profile.Trait,
                    Tier = "white",
                    SpecialStyle = "",
                    Proof = 0.0,
                    AgeStatementYears = 0.0
                };
            }

            double sampleStepHours = 24.0;

            if (totalHours > 24.0 * 180.0)
            {
                sampleStepHours = 72.0;
            }

            double accumulatedAcceleratedHours = 0.0;

            double accumulatedTemperature = 0.0;
            double accumulatedRainfall = 0.0;
            double accumulatedHumidityModifier = 0.0;
            double accumulatedWeightedHours = 0.0;

            double cursor = sealedAtTotalHours;

            while (cursor < unsealedAtTotalHours)
            {
                double next = Math.Min(cursor + sampleStepHours, unsealedAtTotalHours);
                double chunkHours = next - cursor;

                if (chunkHours <= 0.0)
                {
                    break;
                }

                double sampleHour = cursor + (chunkHours / 2.0);
                AgingClimateSample sample = GetClimateSampleAtWorldHour(api, pos, sampleHour);

                float temp = sample.Temperature;
                float rainfall = sample.Rainfall;

                double humidityModifier = GetHumidityModifier(rainfall);

                double tempSpeedMultiplier = GetTemperatureSpeedMultiplier(temp);

                double chunkAcceleratedHours =
                    chunkHours
                    * tempSpeedMultiplier
                    * humidityModifier
                    * profile.CaskVariance;

                accumulatedAcceleratedHours += chunkAcceleratedHours;

                accumulatedTemperature += temp * chunkHours;
                accumulatedRainfall += rainfall * chunkHours;
                accumulatedHumidityModifier += humidityModifier * chunkHours;
                accumulatedWeightedHours += chunkHours;

                cursor = next;
            }

            double ageDays = accumulatedAcceleratedHours / 24.0;

            double averageTemp = accumulatedWeightedHours > 0.0
                ? accumulatedTemperature / accumulatedWeightedHours
                : 20.0;

            double averageRainfall = accumulatedWeightedHours > 0.0
                ? accumulatedRainfall / accumulatedWeightedHours
                : 0.5;

            double averageHumidityModifier = accumulatedWeightedHours > 0.0
                ? accumulatedHumidityModifier / accumulatedWeightedHours
                : 1.0;

            double safeWindowDays = GetSafeWindowDaysFromClimate(averageTemp, averageRainfall, profile);
            double maturityRatio = safeWindowDays > 0.0 ? ageDays / safeWindowDays : 0.0;
            double overAgeRatio = Math.Max(0.0, maturityRatio - 1.0);

            double intensity = CalculateIntensity(averageTemp, averageRainfall, maturityRatio, profile);
            double smoothness = CalculateSmoothness(averageTemp, averageRainfall, maturityRatio, profile);
            double balance = CalculateBalance(intensity, smoothness);

            double quality = CalculateQuality(
                maturityRatio,
                overAgeRatio,
                intensity,
                smoothness,
                balance,
                averageRainfall,
                profile
            );

            string tier = BarrelAgingUtil.GetAgeTierFromMaturity(
                liquidStack,
                maturityRatio,
                quality,
                intensity,
                smoothness
            );

            string specialStyle = GetSpecialStyle(
                tier,
                quality,
                intensity,
                smoothness,
                maturityRatio,
                safeWindowDays,
                ageDays,
                averageTemp,
                averageRainfall,
                profile
            );

            double proof = CalculateProofFromIntensity(intensity);
            double ageStatementYears = CalculateAgeStatementYears(ageDays);

            return new AgingSnapshot
            {
                TotalHours = totalHours,
                AgeHours = accumulatedAcceleratedHours,
                AgeDays = ageDays,
                SafeWindowDays = safeWindowDays,
                MaturityRatio = maturityRatio,
                OverAgeRatio = overAgeRatio,
                Quality = quality,
                Intensity = intensity,
                Smoothness = smoothness,
                Balance = balance,
                AverageTemperature = averageTemp,
                AverageRainfall = averageRainfall,
                AverageHumidityModifier = averageHumidityModifier,
                ClimateStyle = GetClimateStyle(averageTemp, averageRainfall),
                MaturationDescriptor = GetMaturationDescriptor(maturityRatio),
                CaskTrait = profile.Trait,
                Tier = tier,
                SpecialStyle = specialStyle,
                Proof = proof,
                AgeStatementYears = ageStatementYears
            };
        }

        private static double GetSafeWindowDaysFromClimate(double averageTemp, double averageRainfall, CaskProfile profile)
        {
            double safeWindowDays = 75.0;

            if (averageTemp > 20.0)
            {
                safeWindowDays -= (averageTemp - 20.0) * 1.7;
            }
            else if (averageTemp < 20.0)
            {
                safeWindowDays += (20.0 - averageTemp) * 0.45;
            }

            if (averageRainfall > 0.65)
            {
                safeWindowDays += 12.0;
            }
            else if (averageRainfall < 0.30)
            {
                safeWindowDays -= 12.0;
            }

            safeWindowDays *= profile.SafeWindowMultiplier;

            return Clamp(safeWindowDays, 25.0, 220.0);
        }

        private static double CalculateIntensity(double averageTemp, double averageRainfall, double maturityRatio, CaskProfile profile)
        {
            double hotContribution = Clamp((averageTemp - 16.0) * 3.5, 0.0, 45.0);
            double dryContribution = Clamp((0.60 - averageRainfall) * 60.0, 0.0, 35.0);
            double maturityContribution = Clamp(maturityRatio, 0.0, 1.0) * 25.0;

            double intensity =
                10.0
                + hotContribution
                + dryContribution
                + maturityContribution
                + profile.IntensityBonus;

            return Clamp(intensity, 0.0, 100.0);
        }

        private static double CalculateSmoothness(double averageTemp, double averageRainfall, double maturityRatio, CaskProfile profile)
        {
            double coolContribution = Clamp((22.0 - averageTemp) * 3.0, 0.0, 40.0);
            double humidContribution = Clamp((averageRainfall - 0.35) * 60.0, 0.0, 35.0);
            double maturityContribution = Clamp(maturityRatio, 0.0, 1.0) * 25.0;

            double smoothness =
                10.0
                + coolContribution
                + humidContribution
                + maturityContribution
                + profile.SmoothnessBonus;

            return Clamp(smoothness, 0.0, 100.0);
        }

        private static double CalculateBalance(double intensity, double smoothness)
        {
            double lower = Math.Min(intensity, smoothness);
            double higher = Math.Max(intensity, smoothness);

            if (higher <= 0.0) return 0.0;

            double closeness = lower / higher;
            double strength = lower / 100.0;

            return Clamp(closeness * strength * 100.0, 0.0, 100.0);
        }

        private static double CalculateQuality(
            double maturityRatio,
            double overAgeRatio,
            double intensity,
            double smoothness,
            double balance,
            double averageRainfall,
            CaskProfile profile
        )
        {
            double cappedMaturity = Clamp(maturityRatio, 0.0, 1.0);
            double maturityScore = cappedMaturity * 100.0;

            double primaryExpression = Math.Max(intensity, smoothness);
            double expressionBonus = primaryExpression * 0.18;

            double balanceBonus = 0.0;

            if (intensity >= 70.0 && smoothness >= 70.0)
            {
                balanceBonus = 18.0;
            }
            else
            {
                balanceBonus = balance * 0.08;
            }

            double dullnessPenalty = 0.0;

            if (intensity < 35.0 && smoothness < 35.0)
            {
                dullnessPenalty = 25.0;
            }
            else if (primaryExpression < 50.0)
            {
                dullnessPenalty = 10.0;
            }

            double overOakPenalty = 0.0;

            if (overAgeRatio > 0.0)
            {
                overOakPenalty =
                    (10.0 * overAgeRatio)
                    + (70.0 * overAgeRatio * overAgeRatio);

                overOakPenalty *= GetDrynessRiskModifier((float)averageRainfall);
                overOakPenalty *= profile.OverOakResistance;
            }

            double quality =
                maturityScore
                + expressionBonus
                + balanceBonus
                + profile.QualityBonus
                - dullnessPenalty
                - overOakPenalty;

            return Clamp(quality, 0.0, 100.0);
        }

        private static string GetSpecialStyle(
            string tier,
            double quality,
            double intensity,
            double smoothness,
            double maturityRatio,
            double safeWindowDays,
            double ageDays,
            double averageTemp,
            double averageRainfall,
            CaskProfile profile
        )
        {
            if (tier != "reserve")
                return "";

            bool exceptionalBalance =
                quality >= 88.0 &&
                intensity >= 75.0 &&
                smoothness >= 75.0;

            bool caskStrengthCandidate =
                quality >= 82.0 &&
                intensity >= 82.0 &&
                maturityRatio >= 0.78 &&
                (averageTemp >= 22.0 || averageRainfall <= 0.35);

            bool ageStatedCandidate =
                quality >= 90.0 &&
                smoothness >= 70.0 &&
                maturityRatio >= 0.90 &&
                ageDays >= 60.0;

            bool unicornAgeStatedCandidate =
                quality >= 90.0 &&
                smoothness >= 82.0 &&
                safeWindowDays >= 115.0 &&
                ageDays >= 100.0;

            bool unicornTrait = profile.Trait == "unicorn";
            bool tightGrainTrait = profile.Trait == "tight-grain";

            if (exceptionalBalance && (unicornTrait || caskStrengthCandidate))
                return "Unicorn: Cask-Strength Reserve";

            if (unicornAgeStatedCandidate && (unicornTrait || tightGrainTrait || safeWindowDays >= 130.0))
            {
                int years = (int)CalculateAgeStatementYears(ageDays);
                return "Unicorn: " + years + "-Year Old Reserve";
            }

            if (caskStrengthCandidate)
                return "Cask-Strength Reserve";

            if (ageStatedCandidate)
            {
                int years = (int)CalculateAgeStatementYears(ageDays);
                return years + "-Year Old Reserve";
            }

            return "";
        }

        private static double CalculateProofFromIntensity(double intensity)
        {
            if (intensity < 75.0) return 0.0;

            double proof = 120.0 + ((intensity - 75.0) / 25.0) * 35.0;

            return Clamp(proof, 120.0, 155.0);
        }

        private static double CalculateAgeStatementYears(double ageDays)
        {
            if (ageDays < 60.0)
            {
                return 0.0;
            }

            double years = 8.0 + ((ageDays - 60.0) / 10.0);

            return Math.Min(25.0, Math.Floor(years));
        }

        private static string GetMaturationDescriptor(double maturityRatio)
        {
            if (maturityRatio >= 1.12)
                return "Over-Oaked";

            if (maturityRatio > 1.03)
                return "Heavy Oak";

            if (maturityRatio >= 0.98)
                return "At the Edge";

            if (maturityRatio >= 0.80)
                return "Near Peak";

            if (maturityRatio >= 0.55)
                return "Maturing";

            if (maturityRatio >= 0.25)
                return "Developing";

            if (maturityRatio >= 0.05)
                return "Resting";

            return "Raw";
        }

        private class CaskTraitRule
        {
            public double UpperRoll { get; set; }
            public System.Func<Random, CaskProfile> CreateProfile { get; set; }
        }

        private static readonly CaskTraitRule[] CaskTraitRules =
        {
            new CaskTraitRule
            {
                UpperRoll = 0.005,
                CreateProfile = rand => new CaskProfile
                {
                    Trait = "flawed",
                    CaskVariance = 0.85 + (rand.NextDouble() * 0.08),
                    IntensityBonus = -12.0,
                    SmoothnessBonus = -12.0,
                    QualityBonus = -18.0,
                    SafeWindowMultiplier = 0.90,
                    OverOakResistance = 1.15
                }
            },

            new CaskTraitRule
            {
                UpperRoll = 0.025,
                CreateProfile = rand => new CaskProfile
                {
                    Trait = "unicorn",
                    CaskVariance = 1.02 + (rand.NextDouble() * 0.16),
                    IntensityBonus = 22.0,
                    SmoothnessBonus = 22.0,
                    QualityBonus = 14.0 + (rand.NextDouble() * 8.0),
                    SafeWindowMultiplier = 1.12,
                    OverOakResistance = 0.85,
                }
            },

            new CaskTraitRule
            {
                UpperRoll = 0.060,
                CreateProfile = rand => new CaskProfile
                {
                    Trait = "tight-grain",
                    CaskVariance = 1.02 + (rand.NextDouble() * 0.16),
                    IntensityBonus = 22.0,
                    SmoothnessBonus = 22.0,
                    QualityBonus = 14.0 + (rand.NextDouble() * 8.0),
                    SafeWindowMultiplier = 1.12,
                    OverOakResistance = 0.85,
                }
            },

            new CaskTraitRule
            {
                UpperRoll = 0.100,
                CreateProfile = rand => new CaskProfile
                {
                    Trait = "wide-grain",
                    CaskVariance = 1.05 + (rand.NextDouble() * 0.14),
                    IntensityBonus = 16.0,
                    SmoothnessBonus = -4.0,
                    QualityBonus = 2.0 + (rand.NextDouble() * 5.0),
                    SafeWindowMultiplier = 0.85,
                    OverOakResistance = 1.18,
                }
            },

            new CaskTraitRule
            {
                UpperRoll = 0.170,
                CreateProfile = rand => new CaskProfile
                {
                    Trait = "gentle",
                    CaskVariance = 0.94 + (rand.NextDouble() * 0.12),
                    IntensityBonus = -2.0,
                    SmoothnessBonus = 16.0,
                    QualityBonus = 4.0 + (rand.NextDouble() * 6.0),
                    SafeWindowMultiplier = 1.08,
                    OverOakResistance = 0.92,
                }
            },

            new CaskTraitRule
            {
                UpperRoll = 0.240,
                CreateProfile = rand => new CaskProfile
                {
                    Trait = "expressive",
                    CaskVariance = 0.98 + (rand.NextDouble() * 0.14),
                    IntensityBonus = 16.0,
                    SmoothnessBonus = 0.0,
                    QualityBonus = 4.0 + (rand.NextDouble() * 6.0),
                    SafeWindowMultiplier = 1.0,
                    OverOakResistance = 1.0,
                }
            }
        };

        private static CaskProfile CreateStandardCaskProfile(Random rand)
        {
            return new CaskProfile
            {
                Trait = "standard",
                CaskVariance = 0.92 + (rand.NextDouble() * 0.16),
                IntensityBonus = 0.0,
                SmoothnessBonus = 0.0,
                QualityBonus = -2.0 + (rand.NextDouble() * 7.0),
                SafeWindowMultiplier = 1.0,
                OverOakResistance = 1.0,
            };
        }

        private static CaskProfile RollCaskProfile(BlockEntityBarrel barrel, ItemStack liquidStack, double sealedAtTotalHours)
        {
            int seed = MakeCaskSeed(barrel, liquidStack, sealedAtTotalHours);
            Random rand = new Random(seed);

            double roll = rand.NextDouble();

            CaskTraitRule selectedRule = CaskTraitRules
                .FirstOrDefault(rule => roll < rule.UpperRoll);

            if (selectedRule != null)
            {
                return selectedRule.CreateProfile(rand);
            }

            return CreateStandardCaskProfile(rand);
        }

        private static CaskProfile GetStoredCaskProfile(ITreeAttribute tree)
        {
            return new CaskProfile
            {
                Trait = tree.GetString("caskTrait", "standard"),
                CaskVariance = tree.GetDouble("caskVarianceSeed", 1.0),
                IntensityBonus = tree.GetDouble("caskIntensityBonus", 0.0),
                SmoothnessBonus = tree.GetDouble("caskSmoothnessBonus", 0.0),
                QualityBonus = tree.GetDouble("caskQualityBonus", 0.0),
                SafeWindowMultiplier = tree.GetDouble("caskSafeWindowMultiplier", 1.0),
                OverOakResistance = tree.GetDouble("caskOverOakResistance", 1.0)
            };
        }

        private static int MakeCaskSeed(BlockEntityBarrel barrel, ItemStack liquidStack, double sealedAtTotalHours)
        {
            unchecked
            {
                int hash = 17;

                hash = hash * 31 + barrel.Pos.X;
                hash = hash * 31 + barrel.Pos.Y;
                hash = hash * 31 + barrel.Pos.Z;
                hash = hash * 31 + (int)Math.Round(sealedAtTotalHours * 10.0);
                hash = hash * 31 + liquidStack.StackSize;

                string code = liquidStack.Collectible.Code.ToString();

                for (int i = 0; i < code.Length; i++)
                {
                    hash = hash * 31 + code[i];
                }

                return hash;
            }
        }

        private static AgingClimateSample GetClimateSampleAtWorldHour(ICoreAPI api, BlockPos pos, double worldHour)
        {
            ClimateCondition climate = null;

            try
            {
                climate = TryGetSuppliedDateClimate(api, pos, worldHour);
            }
            catch
            {
                climate = null;
            }

            if (climate == null)
            {
                api.Logger.Warning(
                    "[Angel's Share] Historical climate lookup failed; falling back to current climate."
                );

                climate = api.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.NowValues);
            }

            return new AgingClimateSample
            {
                Temperature = climate != null ? climate.Temperature : 20f,
                Rainfall = climate != null ? climate.Rainfall : 0.5f
            };
        }

        private static ClimateCondition TryGetSuppliedDateClimate(ICoreAPI api, BlockPos pos, double worldHour)
        {
            object blockAccessor = api.World.BlockAccessor;

            var candidateMethods = blockAccessor
                .GetType()
                .GetMethods()
                .Where(method => method.Name == "GetClimateAt")
                .Select(method => new
                {
                    Method = method,
                    Parameters = method.GetParameters()
                })
                .Where(candidate => candidate.Parameters.Length == 3);

            foreach (var candidate in candidateMethods)
            {
                object thirdArgument = ConvertWorldHourToParameter(
                    worldHour,
                    candidate.Parameters[2].ParameterType
                );

                if (thirdArgument == null) continue;

                try
                {
                    object result = candidate.Method.Invoke(
                        blockAccessor,
                        new object[]
                        {
                            pos,
                            EnumGetClimateMode.ForSuppliedDate_TemperatureRainfallOnly,
                            thirdArgument
                        }
                    );

                    if (result is ClimateCondition climate)
                        return climate;
                }
                catch
                {
                    // Try the next overload if this one fails.
                }
            }

            return null;
        }

        private static object ConvertWorldHourToParameter(double worldHour, Type parameterType)
        {
            if (parameterType == typeof(double))
                return worldHour;

            if (parameterType == typeof(float))
                return (float)worldHour;

            if (parameterType == typeof(int))
                return (int)worldHour;

            if (parameterType == typeof(long))
                return (long)worldHour;

            return null;
        }

        private static double GetHumidityModifier(float rainfall)
        {
            double clampedRainfall = Clamp(rainfall, 0.0, 1.0);

            return 1.15 - (clampedRainfall * 0.25);
        }

        private static double GetDrynessRiskModifier(float rainfall)
        {
            double clampedRainfall = Clamp(rainfall, 0.0, 1.0);

            return 1.25 - (clampedRainfall * 0.40);
        }

        private static string GetClimateStyle(double averageTemp, double averageRainfall)
        {
            if (averageTemp >= 28.0 && averageRainfall <= 0.35)
                return "Hot Dry Fast-Maturation";

            if (averageTemp >= 28.0 && averageRainfall > 0.65)
                return "Hot Humid Tropical Maturation";

            if (averageTemp <= 12.0 && averageRainfall > 0.65)
                return "Cool Humid Slow-Aged";

            if (averageTemp <= 12.0 && averageRainfall <= 0.35)
                return "Cool Dry Concentrated";

            if (averageRainfall > 0.70)
                return "Humid Continental Maturation";

            if (averageRainfall < 0.30)
                return "Dry Continental Maturation";

            return "Standard Continental Maturation";
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }
    }
}