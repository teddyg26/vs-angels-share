using System;
using System.Collections.Generic;

namespace AngelsShare
{
    /// <summary>
    /// Numeric inputs for a maturation evaluation. This type deliberately has no
    /// block, world, item-stack, or API dependency so calculation scenarios can be
    /// exercised without loading a game world.
    /// </summary>
    public sealed class MaturationCalculationInput
    {
        public string LiquidCode { get; set; } = "angels-share:whitespiritportion-test";
        public double TotalActualElapsedHours { get; set; }
        public double TotalEffectiveMaturationHours { get; set; }
        public double AverageTemperature { get; set; } = 20.0;
        public double AverageRainfall { get; set; } = 0.5;
        public double? AverageHumidityModifier { get; set; }
        public CaskProfile Cask { get; set; } = MaturationMath.CreateBaselineCaskProfile();
        public MaturationLossInput Loss { get; set; } = new MaturationLossInput();
    }

    /// <summary>
    /// Numeric volume and loss counters associated with a calculation.
    /// ConservationErrorLitres in the result exposes discrepancies rather than
    /// silently folding them into another loss category.
    /// </summary>
    public sealed class MaturationLossInput
    {
        public double StartingVolumeLitres { get; set; }
        public double CurrentVolumeLitres { get; set; }
        public double AngelsShareLostLitres { get; set; }
        public double WhiskeyThiefSampledLitres { get; set; }
        public double OtherLossLitres { get; set; }
        public double FractionalAngelsShareRemainderLitres { get; set; }
    }

    public sealed class MaturationLossResult
    {
        public double StartingVolumeLitres { get; set; }
        public double CurrentVolumeLitres { get; set; }
        public double AngelsShareLostLitres { get; set; }
        public double WhiskeyThiefSampledLitres { get; set; }
        public double OtherLossLitres { get; set; }
        public double FractionalAngelsShareRemainderLitres { get; set; }
        public double TotalVolumeChangeLitres { get; set; }
        public double AccountedLossLitres { get; set; }
        public double ConservationErrorLitres { get; set; }
        public double RemainingVolumeFraction { get; set; }
        public double TotalLossFraction { get; set; }
        public double AngelsShareLossFraction { get; set; }
    }

    public sealed class MaturationClimatePeriodResult
    {
        public double DurationHours { get; set; }
        public double Temperature { get; set; }
        public double Rainfall { get; set; }
        public double TargetCalendarDaysToPeak { get; set; }
        public double TemperatureSpeedMultiplier { get; set; }
        public double HumidityModifier { get; set; }
        public double MaturationRateMultiplier { get; set; }
        public double EffectiveMaturationHours { get; set; }
    }

    public sealed class AgingDesignation
    {
        public string Code { get; set; }
        public double? NumericValue { get; set; }
        public string UnitCode { get; set; }
        public string DisplayName { get; set; }
    }

    public sealed class MaturationCalculationResult
    {
        public AgingSnapshot Snapshot { get; set; }
        public MaturationLossResult Loss { get; set; }
    }

    /// <summary>
    /// Authoritative, side-effect-free maturation formulas. Game-facing code is
    /// responsible only for gathering climate/item data and persisting results.
    /// Keep this public input/output boundary stable when formulas are retuned;
    /// update the data-only regression cases to document intended balance changes.
    /// </summary>
    public static class MaturationMath
    {
        private const double BaselineEffectivePeakDays = 18.0;

        private sealed class CaskTraitRule
        {
            public double UpperRoll { get; set; }
            public Func<Random, CaskProfile> CreateProfile { get; set; }
        }

        private static readonly CaskTraitRule[] CaskTraitRules =
        {
            new CaskTraitRule
            {
                UpperRoll = 0.0005,
                CreateProfile = rand => new CaskProfile
                {
                    Trait = "flawed",
                    CaskVariance = 0.82 + (rand.NextDouble() * 0.10),
                    IntensityBonus = -10.0,
                    SmoothnessBonus = -14.0,
                    QualityBonus = -22.0,
                    SafeWindowMultiplier = 0.90,
                    OverOakResistance = 1.20
                }
            },
            new CaskTraitRule
            {
                UpperRoll = 0.0205,
                CreateProfile = rand => new CaskProfile
                {
                    Trait = "unicorn",
                    CaskVariance = 1.00 + (rand.NextDouble() * 0.12),
                    IntensityBonus = 24.0,
                    SmoothnessBonus = 24.0,
                    QualityBonus = 14.0 + (rand.NextDouble() * 8.0),
                    SafeWindowMultiplier = 1.22,
                    OverOakResistance = 0.70
                }
            },
            new CaskTraitRule
            {
                UpperRoll = 0.0605,
                CreateProfile = rand => new CaskProfile
                {
                    Trait = "tight-grain",
                    CaskVariance = 0.86 + (rand.NextDouble() * 0.12),
                    IntensityBonus = -8.0,
                    SmoothnessBonus = 22.0,
                    QualityBonus = 6.0 + (rand.NextDouble() * 6.0),
                    SafeWindowMultiplier = 1.35,
                    OverOakResistance = 0.78
                }
            },
            new CaskTraitRule
            {
                UpperRoll = 0.1005,
                CreateProfile = rand => new CaskProfile
                {
                    Trait = "wide-grain",
                    CaskVariance = 1.08 + (rand.NextDouble() * 0.14),
                    IntensityBonus = 22.0,
                    SmoothnessBonus = -8.0,
                    QualityBonus = 4.0 + (rand.NextDouble() * 5.0),
                    SafeWindowMultiplier = 0.82,
                    OverOakResistance = 1.22
                }
            },
            new CaskTraitRule
            {
                UpperRoll = 0.2105,
                CreateProfile = rand => new CaskProfile
                {
                    Trait = "gentle",
                    CaskVariance = 0.92 + (rand.NextDouble() * 0.12),
                    IntensityBonus = -2.0,
                    SmoothnessBonus = 14.0,
                    QualityBonus = 3.0 + (rand.NextDouble() * 5.0),
                    SafeWindowMultiplier = 1.10,
                    OverOakResistance = 0.90
                }
            },
            new CaskTraitRule
            {
                UpperRoll = 0.3205,
                CreateProfile = rand => new CaskProfile
                {
                    Trait = "expressive",
                    CaskVariance = 1.00 + (rand.NextDouble() * 0.12),
                    IntensityBonus = 14.0,
                    SmoothnessBonus = -1.0,
                    QualityBonus = 3.0 + (rand.NextDouble() * 5.0),
                    SafeWindowMultiplier = 0.98,
                    OverOakResistance = 1.03
                }
            }
        };

        public static CaskProfile CreateBaselineCaskProfile()
        {
            return new CaskProfile
            {
                Trait = "standard",
                CaskVariance = 1.0,
                IntensityBonus = 0.0,
                SmoothnessBonus = 0.0,
                QualityBonus = 0.0,
                SafeWindowMultiplier = 1.0,
                OverOakResistance = 1.0
            };
        }

        public static int MakeCaskSeed(
            int positionX,
            int positionY,
            int positionZ,
            double sealedAtCalendarHours,
            int stackSize,
            string liquidCode
        )
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + positionX;
                hash = hash * 31 + positionY;
                hash = hash * 31 + positionZ;
                hash = hash * 31 + (int)Math.Round(sealedAtCalendarHours * 10.0);
                hash = hash * 31 + stackSize;

                string code = liquidCode ?? string.Empty;
                for (int i = 0; i < code.Length; i++)
                {
                    hash = hash * 31 + code[i];
                }

                return hash;
            }
        }

        public static CaskProfile RollCaskProfile(int seed)
        {
            Random rand = new Random(seed);
            double roll = rand.NextDouble();

            foreach (CaskTraitRule rule in CaskTraitRules)
            {
                if (roll >= rule.UpperRoll) continue;

                CaskProfile selected = rule.CreateProfile(rand);
                selected.RollSeed = seed;
                return selected;
            }

            CaskProfile standard = new CaskProfile
            {
                Trait = "standard",
                CaskVariance = 0.94 + (rand.NextDouble() * 0.12),
                IntensityBonus = 0.0,
                SmoothnessBonus = 0.0,
                QualityBonus = -3.0 + (rand.NextDouble() * 6.0),
                SafeWindowMultiplier = 1.0,
                OverOakResistance = 1.0
            };
            standard.RollSeed = seed;
            return standard;
        }

        public static MaturationClimatePeriodResult CalculateClimatePeriod(
            double durationHours,
            double temperature,
            double rainfall,
            CaskProfile cask
        )
        {
            CaskProfile profile = NormalizeCask(cask);
            double humidityModifier = GetHumidityModifier(rainfall);
            double temperatureSpeedMultiplier = GetTemperatureSpeedMultiplier(temperature);
            double targetCalendarDaysToPeak = GetTargetCalendarDaysToPeak(
                temperature,
                rainfall
            );
            // Climate defines how many calendar days a baseline cask needs to
            // accumulate the neutral effective peak. The effective safe window
            // below must not apply the same climate adjustment a second time.
            double climateMaturationRate =
                BaselineEffectivePeakDays / targetCalendarDaysToPeak;
            double maturationRate = climateMaturationRate * profile.CaskVariance;

            return new MaturationClimatePeriodResult
            {
                DurationHours = durationHours,
                Temperature = temperature,
                Rainfall = rainfall,
                TargetCalendarDaysToPeak = targetCalendarDaysToPeak,
                TemperatureSpeedMultiplier = temperatureSpeedMultiplier,
                HumidityModifier = humidityModifier,
                MaturationRateMultiplier = maturationRate,
                EffectiveMaturationHours = durationHours * maturationRate
            };
        }

        public static MaturationCalculationResult CalculateConstantClimate(
            string liquidCode,
            double actualElapsedHours,
            double temperature,
            double rainfall,
            CaskProfile cask,
            MaturationLossInput loss
        )
        {
            MaturationClimatePeriodResult climate = CalculateClimatePeriod(
                actualElapsedHours,
                temperature,
                rainfall,
                cask
            );

            return Calculate(new MaturationCalculationInput
            {
                LiquidCode = liquidCode,
                TotalActualElapsedHours = actualElapsedHours,
                TotalEffectiveMaturationHours = climate.EffectiveMaturationHours,
                AverageTemperature = temperature,
                AverageRainfall = rainfall,
                AverageHumidityModifier = climate.HumidityModifier,
                Cask = cask,
                Loss = loss ?? new MaturationLossInput()
            });
        }

        public static MaturationCalculationResult Calculate(MaturationCalculationInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            CaskProfile profile = NormalizeCask(input.Cask);
            MaturationLossResult loss = CalculateLoss(input.Loss);

            if (input.TotalActualElapsedHours <= 0.0)
            {
                return new MaturationCalculationResult
                {
                    Snapshot = CreateEmptySnapshot(
                        profile,
                        input.AverageTemperature,
                        input.AverageRainfall
                    ),
                    Loss = loss
                };
            }

            double ageDays = input.TotalEffectiveMaturationHours / 24.0;
            double safeWindowDays = GetSafeWindowDays(
                input.AverageTemperature,
                input.AverageRainfall,
                profile
            );
            double maturityRatio = safeWindowDays > 0.0 ? ageDays / safeWindowDays : 0.0;
            double averageMaturationRate = input.TotalActualElapsedHours > 0.0
                ? input.TotalEffectiveMaturationHours / input.TotalActualElapsedHours
                : 1.0;
            double estimatedCalendarDaysToPeak = averageMaturationRate > 0.0
                ? safeWindowDays / averageMaturationRate
                : safeWindowDays;
            double overAgeRatio = Math.Max(0.0, maturityRatio - 1.0);
            double intensity = CalculateIntensity(
                input.AverageTemperature,
                input.AverageRainfall,
                maturityRatio,
                profile
            );
            double smoothness = CalculateSmoothness(
                input.AverageTemperature,
                input.AverageRainfall,
                maturityRatio,
                profile
            );
            double balance = CalculateBalance(intensity, smoothness);
            double quality = CalculateQuality(
                maturityRatio,
                overAgeRatio,
                intensity,
                smoothness,
                balance,
                input.AverageRainfall,
                profile
            );
            string tier = GetAgeTierFromMaturity(
                IsGinLiquidCode(input.LiquidCode),
                maturityRatio,
                quality,
                intensity,
                smoothness
            );
            double proof = CalculateProofFromIntensity(intensity);
            double ageStatementYears = CalculateAgeStatementYears(
                ageDays,
                estimatedCalendarDaysToPeak,
                maturityRatio,
                smoothness,
                input.AverageTemperature,
                input.AverageRainfall
            );
            string specialStyle = CalculateSpecialStyle(
                tier,
                quality,
                intensity,
                smoothness,
                maturityRatio,
                estimatedCalendarDaysToPeak,
                ageDays,
                input.AverageTemperature,
                input.AverageRainfall,
                profile
            );

            AgingSnapshot snapshot = new AgingSnapshot
            {
                TotalHours = input.TotalActualElapsedHours,
                AgeHours = input.TotalEffectiveMaturationHours,
                AgeDays = ageDays,
                SafeWindowDays = safeWindowDays,
                MaturityRatio = maturityRatio,
                OverAgeRatio = overAgeRatio,
                Quality = quality,
                Intensity = intensity,
                Smoothness = smoothness,
                Balance = balance,
                Extraction = Clamp(maturityRatio * 100.0, 0.0, 100.0),
                Oak = Clamp((overAgeRatio / 0.12) * 100.0, 0.0, 100.0),
                AverageTemperature = input.AverageTemperature,
                AverageRainfall = input.AverageRainfall,
                AverageHumidityModifier = input.AverageHumidityModifier
                    ?? GetHumidityModifier(input.AverageRainfall),
                ClimateStyle = GetClimateStyle(
                    input.AverageTemperature,
                    input.AverageRainfall
                ),
                MaturationDescriptor = GetMaturationDescriptor(maturityRatio),
                CaskTrait = profile.Trait,
                Tier = tier,
                SpecialStyle = specialStyle,
                Proof = proof,
                AgeStatementYears = ageStatementYears
            };

            snapshot.Designations = CalculateDesignations(snapshot);

            return new MaturationCalculationResult
            {
                Snapshot = snapshot,
                Loss = loss
            };
        }

        public static MaturationLossResult CalculateLoss(MaturationLossInput input)
        {
            input ??= new MaturationLossInput();

            double volumeChange = input.StartingVolumeLitres - input.CurrentVolumeLitres;
            double accountedLoss =
                input.AngelsShareLostLitres
                + input.WhiskeyThiefSampledLitres
                + input.OtherLossLitres;
            double startingVolume = input.StartingVolumeLitres;

            return new MaturationLossResult
            {
                StartingVolumeLitres = startingVolume,
                CurrentVolumeLitres = input.CurrentVolumeLitres,
                AngelsShareLostLitres = input.AngelsShareLostLitres,
                WhiskeyThiefSampledLitres = input.WhiskeyThiefSampledLitres,
                OtherLossLitres = input.OtherLossLitres,
                FractionalAngelsShareRemainderLitres =
                    input.FractionalAngelsShareRemainderLitres,
                TotalVolumeChangeLitres = volumeChange,
                AccountedLossLitres = accountedLoss,
                ConservationErrorLitres = volumeChange - accountedLoss,
                RemainingVolumeFraction = startingVolume > 0.0
                    ? input.CurrentVolumeLitres / startingVolume
                    : 0.0,
                TotalLossFraction = startingVolume > 0.0
                    ? volumeChange / startingVolume
                    : 0.0,
                AngelsShareLossFraction = startingVolume > 0.0
                    ? input.AngelsShareLostLitres / startingVolume
                    : 0.0
            };
        }

        public static double ConvertCalendarHoursToDays(double calendarHours)
        {
            return calendarHours / 24.0;
        }

        public static double GetTemperatureSpeedMultiplier(double temperature)
        {
            return BaselineEffectivePeakDays
                / GetTargetCalendarDaysToPeak(temperature, 0.5);
        }

        public static double GetHumidityModifier(double rainfall)
        {
            return BaselineEffectivePeakDays
                / GetTargetCalendarDaysToPeak(20.0, rainfall);
        }

        public static double GetDrynessRiskModifier(double rainfall)
        {
            double clampedRainfall = Clamp(rainfall, 0.0, 1.0);
            return 1.25 - (clampedRainfall * 0.40);
        }

        public static double GetSafeWindowDays(
            double averageTemperature,
            double averageRainfall,
            CaskProfile cask
        )
        {
            CaskProfile profile = NormalizeCask(cask);
            // This is an effective-maturation window. Climate has already been
            // integrated into effective hours; only cask character belongs here.
            return Clamp(
                BaselineEffectivePeakDays * profile.SafeWindowMultiplier,
                6.0,
                42.0
            );
        }

        public static double GetTargetCalendarDaysToPeak(
            double averageTemperature,
            double averageRainfall
        )
        {
            // Singleplayer balance target for a baseline cask. Representative
            // hot/dry and cool/humid climates land near 6 and 30 calendar days;
            // more extreme climates and cask luck may legitimately exceed them.
            double targetCalendarDays = BaselineEffectivePeakDays;

            if (averageTemperature > 20.0)
            {
                targetCalendarDays -= (averageTemperature - 20.0) * 0.70;
            }
            else if (averageTemperature < 20.0)
            {
                targetCalendarDays += (20.0 - averageTemperature) * 0.65;
            }

            if (averageRainfall > 0.65)
            {
                targetCalendarDays += 4.0 + ((averageRainfall - 0.5) * 10.0);
            }
            else if (averageRainfall < 0.30)
            {
                targetCalendarDays -= 4.0 + ((0.30 - averageRainfall) * 8.0);
            }

            return Clamp(targetCalendarDays, 6.0, 42.0);
        }

        public static double CalculateIntensity(
            double averageTemperature,
            double averageRainfall,
            double maturityRatio,
            CaskProfile cask
        )
        {
            CaskProfile profile = NormalizeCask(cask);
            double hotContribution = Clamp((averageTemperature - 16.0) * 3.5, 0.0, 45.0);
            double dryContribution = Clamp((0.60 - averageRainfall) * 60.0, 0.0, 35.0);
            double maturityContribution = Clamp(maturityRatio, 0.0, 1.0) * 25.0;

            return Clamp(
                10.0
                + hotContribution
                + dryContribution
                + maturityContribution
                + profile.IntensityBonus,
                0.0,
                100.0
            );
        }

        public static double CalculateSmoothness(
            double averageTemperature,
            double averageRainfall,
            double maturityRatio,
            CaskProfile cask
        )
        {
            CaskProfile profile = NormalizeCask(cask);
            double coolContribution = Clamp((22.0 - averageTemperature) * 3.0, 0.0, 40.0);
            double humidContribution = Clamp((averageRainfall - 0.35) * 60.0, 0.0, 35.0);
            double maturityContribution = Clamp(maturityRatio, 0.0, 1.0) * 25.0;

            return Clamp(
                10.0
                + coolContribution
                + humidContribution
                + maturityContribution
                + profile.SmoothnessBonus,
                0.0,
                100.0
            );
        }

        public static double CalculateBalance(double intensity, double smoothness)
        {
            double lower = Math.Min(intensity, smoothness);
            double higher = Math.Max(intensity, smoothness);
            if (higher <= 0.0) return 0.0;

            double closeness = lower / higher;
            double strength = lower / 100.0;
            return Clamp(closeness * strength * 100.0, 0.0, 100.0);
        }

        public static double CalculateQuality(
            double maturityRatio,
            double overAgeRatio,
            double intensity,
            double smoothness,
            double balance,
            double averageRainfall,
            CaskProfile cask
        )
        {
            CaskProfile profile = NormalizeCask(cask);
            double cappedMaturity = Clamp(maturityRatio, 0.0, 1.0);
            double maturityScore = cappedMaturity * 100.0;
            double primaryExpression = Math.Max(intensity, smoothness);
            double expressionBonus = primaryExpression * 0.18;
            double balanceBonus = intensity >= 70.0 && smoothness >= 70.0
                ? 18.0
                : balance * 0.08;
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
                overOakPenalty *= GetDrynessRiskModifier(averageRainfall);
                overOakPenalty *= profile.OverOakResistance;
            }

            return Clamp(
                maturityScore
                + expressionBonus
                + balanceBonus
                + profile.QualityBonus
                - dullnessPenalty
                - overOakPenalty,
                0.0,
                100.0
            );
        }

        public static string GetAgeTierFromMaturity(
            bool isGin,
            double maturityRatio,
            double quality,
            double intensity,
            double smoothness
        )
        {
            bool hasGrace =
                quality >= 85.0
                && smoothness >= 68.0
                && maturityRatio <= 1.08;

            if (maturityRatio > 1.0 && !hasGrace)
                return "over-oaked";

            double reserveThreshold = isGin ? 0.80 : 0.85;
            bool generalReserve =
                maturityRatio >= reserveThreshold
                && quality >= 78.0
                && Math.Max(intensity, smoothness) >= 75.0;
            bool coldReserve =
                maturityRatio >= 0.90
                && quality >= 90.0
                && smoothness >= 70.0;
            bool hotReserve =
                maturityRatio >= 0.78
                && quality >= 82.0
                && intensity >= 82.0;

            if (generalReserve || coldReserve || hotReserve)
                return "reserve";
            if (maturityRatio >= 0.55)
                return "aged";
            if (maturityRatio >= 0.25)
                return "young";
            if (maturityRatio >= 0.05)
                return "rested";
            return "white";
        }

        public static string CalculateSpecialStyle(
            string tier,
            double quality,
            double intensity,
            double smoothness,
            double maturityRatio,
            double estimatedCalendarDaysToPeak,
            double ageDays,
            double averageTemperature,
            double averageRainfall,
            CaskProfile cask
        )
        {
            if (tier != "reserve") return string.Empty;

            CaskProfile profile = NormalizeCask(cask);
            bool unicornTrait = profile.Trait == "unicorn";
            bool tightGrainTrait = profile.Trait == "tight-grain";
            double ageStatementYears = CalculateAgeStatementYears(
                ageDays,
                estimatedCalendarDaysToPeak,
                maturityRatio,
                smoothness,
                averageTemperature,
                averageRainfall
            );
            bool caskStrengthCandidate =
                quality >= 82.0
                && intensity >= 82.0
                && maturityRatio >= 0.78
                && (averageTemperature >= 22.0 || averageRainfall <= 0.35);
            bool ageStatedCandidate =
                quality >= 88.0
                && smoothness >= 70.0
                && maturityRatio >= 0.88
                && ageStatementYears >= 8.0;
            bool exceptionalBalance =
                quality >= 92.0
                && intensity >= 75.0
                && smoothness >= 75.0;
            bool unicornAgeStatedCandidate =
                ageStatedCandidate
                && quality >= 94.0
                && smoothness >= 82.0
                && ageStatementYears >= 14.0
                && (
                    unicornTrait
                    || tightGrainTrait
                    || estimatedCalendarDaysToPeak >= 32.0
                );
            bool unicornCaskStrengthCandidate =
                caskStrengthCandidate
                && quality >= 94.0
                && intensity >= 90.0
                && (unicornTrait || exceptionalBalance);

            if (ageStatedCandidate && caskStrengthCandidate)
            {
                int years = (int)ageStatementYears;
                return unicornAgeStatedCandidate || unicornCaskStrengthCandidate
                    ? "Unicorn: " + years + "-Year Old Cask-Strength Reserve"
                    : years + "-Year Old Cask-Strength Reserve";
            }

            if (ageStatedCandidate)
            {
                int years = (int)ageStatementYears;
                return unicornAgeStatedCandidate
                    ? "Unicorn: " + years + "-Year Old Reserve"
                    : years + "-Year Old Reserve";
            }

            if (caskStrengthCandidate)
            {
                return unicornCaskStrengthCandidate
                    ? "Unicorn: Cask-Strength Reserve"
                    : "Cask-Strength Reserve";
            }

            return string.Empty;
        }

        public static double CalculateProofFromIntensity(double intensity)
        {
            if (intensity < 75.0) return 0.0;
            double proof = 120.0 + ((intensity - 75.0) / 25.0) * 35.0;
            return Clamp(proof, 120.0, 155.0);
        }

        public static double CalculateAgeStatementYears(
            double ageDays,
            double estimatedCalendarDaysToPeak,
            double maturityRatio,
            double smoothness,
            double averageTemperature,
            double averageRainfall
        )
        {
            if (
                estimatedCalendarDaysToPeak < 18.0
                || maturityRatio < 0.88
                || smoothness < 65.0
            )
                return 0.0;

            double slowWindowScore = Clamp(
                (estimatedCalendarDaysToPeak - 18.0) / 24.0,
                0.0,
                1.0
            );
            double smoothnessScore = Clamp((smoothness - 65.0) / 35.0, 0.0, 1.0);
            double maturityScore = Clamp((maturityRatio - 0.88) / 0.17, 0.0, 1.0);
            double coldBonus = Clamp((16.0 - averageTemperature) / 18.0, 0.0, 1.0);
            double humidBonus = Clamp((averageRainfall - 0.35) / 0.45, 0.0, 1.0);
            double prestigeScore =
                (slowWindowScore * 0.45)
                + (smoothnessScore * 0.25)
                + (maturityScore * 0.20)
                + (coldBonus * 0.05)
                + (humidBonus * 0.05);

            double years = 8.0 + (prestigeScore * 17.0);
            return Math.Min(25.0, Math.Floor(years));
        }

        public static string GetMaturationDescriptor(double maturityRatio)
        {
            if (maturityRatio >= 1.12) return "Over-Oaked";
            if (maturityRatio > 1.03) return "Heavy Oak";
            if (maturityRatio >= 0.98) return "At the Edge";
            if (maturityRatio >= 0.80) return "Near Peak";
            if (maturityRatio >= 0.55) return "Maturing";
            if (maturityRatio >= 0.25) return "Developing";
            if (maturityRatio >= 0.05) return "Resting";
            return "Raw";
        }

        public static string GetClimateStyle(double averageTemperature, double averageRainfall)
        {
            if (averageTemperature >= 28.0 && averageRainfall <= 0.35)
                return "Hot Dry Fast-Maturation";
            if (averageTemperature >= 28.0 && averageRainfall > 0.65)
                return "Hot Humid Tropical Maturation";
            if (averageTemperature <= 12.0 && averageRainfall > 0.65)
                return "Cool Humid Slow-Aged";
            if (averageTemperature <= 12.0 && averageRainfall <= 0.35)
                return "Cool Dry Concentrated";
            if (averageRainfall > 0.70)
                return "Humid Continental Maturation";
            if (averageRainfall < 0.30)
                return "Dry Continental Maturation";
            return "Standard Continental Maturation";
        }

        public static List<AgingDesignation> CalculateDesignations(AgingSnapshot snapshot)
        {
            List<AgingDesignation> designations = new List<AgingDesignation>();
            if (snapshot == null) return designations;

            string style = snapshot.SpecialStyle ?? string.Empty;
            bool declaresCaskStrength =
                style.IndexOf("Cask-Strength", StringComparison.OrdinalIgnoreCase) >= 0
                || style.IndexOf("Cask-Stregnth", StringComparison.OrdinalIgnoreCase) >= 0;
            bool declaresAgeStatement =
                style.IndexOf("Age-Stated", StringComparison.OrdinalIgnoreCase) >= 0
                || style.IndexOf("-Year Old", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasCaskStrength = snapshot.Proof > 0.0 && declaresCaskStrength;
            bool hasAgeStatement = snapshot.AgeStatementYears >= 8.0 && declaresAgeStatement;

            if (hasAgeStatement)
            {
                designations.Add(new AgingDesignation
                {
                    Code = "angels-share:age-stated",
                    NumericValue = snapshot.AgeStatementYears,
                    UnitCode = "years"
                });
            }

            if (hasCaskStrength)
            {
                designations.Add(new AgingDesignation
                {
                    Code = "angels-share:cask-strength",
                    NumericValue = snapshot.Proof,
                    UnitCode = "proof"
                });
            }

            if (!string.IsNullOrEmpty(style) && !hasAgeStatement && !hasCaskStrength)
            {
                designations.Add(new AgingDesignation
                {
                    Code = "angels-share:legacy-special",
                    UnitCode = string.Empty,
                    DisplayName = style
                });
            }

            return designations;
        }

        public static bool IsGinLiquidCode(string liquidCode)
        {
            if (string.IsNullOrEmpty(liquidCode)) return false;

            int separator = liquidCode.IndexOf(':');
            string domain = separator >= 0 ? liquidCode.Substring(0, separator) : string.Empty;
            string path = separator >= 0 ? liquidCode.Substring(separator + 1) : liquidCode;
            return domain == "angels-share" && path.StartsWith("ginportion-", StringComparison.Ordinal);
        }

        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static CaskProfile NormalizeCask(CaskProfile cask)
        {
            if (cask == null) return CreateBaselineCaskProfile();

            return new CaskProfile
            {
                RollSeed = cask.RollSeed,
                Trait = string.IsNullOrEmpty(cask.Trait) ? "standard" : cask.Trait,
                CaskVariance = cask.CaskVariance > 0.0 ? cask.CaskVariance : 1.0,
                IntensityBonus = cask.IntensityBonus,
                SmoothnessBonus = cask.SmoothnessBonus,
                QualityBonus = cask.QualityBonus,
                SafeWindowMultiplier = cask.SafeWindowMultiplier > 0.0
                    ? cask.SafeWindowMultiplier
                    : 1.0,
                OverOakResistance = cask.OverOakResistance > 0.0
                    ? cask.OverOakResistance
                    : 1.0
            };
        }

        private static AgingSnapshot CreateEmptySnapshot(
            CaskProfile cask,
            double temperature,
            double rainfall
        )
        {
            AgingSnapshot snapshot = new AgingSnapshot
            {
                TotalHours = 0.0,
                AgeHours = 0.0,
                AgeDays = 0.0,
                SafeWindowDays = 18.0,
                MaturityRatio = 0.0,
                OverAgeRatio = 0.0,
                Quality = 0.0,
                Intensity = 0.0,
                Smoothness = 0.0,
                Balance = 0.0,
                Extraction = 0.0,
                Oak = 0.0,
                AverageTemperature = temperature,
                AverageRainfall = rainfall,
                AverageHumidityModifier = GetHumidityModifier(rainfall),
                ClimateStyle = GetClimateStyle(temperature, rainfall),
                MaturationDescriptor = "Raw",
                CaskTrait = cask?.Trait ?? "standard",
                Tier = "white",
                SpecialStyle = string.Empty,
                Proof = 0.0,
                AgeStatementYears = 0.0
            };
            snapshot.Designations = new List<AgingDesignation>();
            return snapshot;
        }
    }
}
