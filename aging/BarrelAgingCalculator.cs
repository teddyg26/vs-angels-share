using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AngelsShare
{
    public static class BarrelAgingCalculator
    {
        private const double ClimateSampleStepHours = 12.0;

        private sealed class ProjectedAgingCacheEntry
        {
            public long HalfDayBucket { get; set; } = long.MinValue;
            public double SealedAtCalendarHours { get; set; }
            public string LiquidCode { get; set; }
            public int StackSize { get; set; }
            public int PositionX { get; set; }
            public int PositionY { get; set; }
            public int PositionZ { get; set; }
            public AgingSnapshot Snapshot { get; set; }
        }

        // Cache only live barrel instances. Entries disappear automatically when a
        // barrel unloads, so projections never become part of saved item data.
        private static readonly ConditionalWeakTable<BlockEntityBarrel, ProjectedAgingCacheEntry>
            ProjectedAgingCache = new ConditionalWeakTable<BlockEntityBarrel, ProjectedAgingCacheEntry>();

        public static void InitializeAgingOnSeal(BlockEntityBarrel barrel, ItemSlot liquidSlot, ItemStack liquidStack)
        {
            if (barrel?.Api == null || liquidSlot?.Itemstack == null || liquidStack == null) return;

            ICoreAPI api = barrel.Api;
            MaturationRecord record;

            if (MaturationRecordCodec.TryRead(liquidStack, out record))
            {
                if (record.State == MaturationRecordState.Active && record.ActiveSession != null)
                    return;
            }
            else
            {
                if (MaturationRecordCodec.HasStoredRecord(liquidStack))
                {
                    api.Logger.Error(
                        "[Angel's Share] Refusing to overwrite unsupported maturation schema version {0} on {1}.",
                        MaturationRecordCodec.GetStoredSchemaVersion(liquidStack),
                        liquidStack.Collectible.Code
                    );
                    return;
                }

                record = new MaturationRecord
                {
                    SchemaVersion = MaturationSchema.CurrentVersion,
                    Provenance = CreateInitialProvenance(liquidStack)
                };
            }

            double nowTotalHours = api.World.Calendar.ElapsedHours;

            BlockPos pos = barrel.Pos;
            ClimateCondition climate = api.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.NowValues);

            float currentTemp = climate != null ? climate.Temperature : 20f;
            float currentRainfall = climate != null ? climate.Rainfall : 0.5f;

            CaskProfile profile = RollCaskProfile(barrel, liquidStack, nowTotalHours);

            double volumeLitres = MaturationRecordCodec.GetStackVolumeLitres(liquidStack);
            int sequence = record.CompletedSessions?.Count + 1 ?? 1;

            record.State = MaturationRecordState.Active;
            record.ActiveSession = new ActiveMaturationSession
            {
                Sequence = sequence,
                InputLiquidCode = liquidStack.Collectible.Code.ToString(),
                SealedAtCalendarHours = nowTotalHours,
                LastIntegratedAtCalendarHours = nowTotalHours,
                ActualElapsedHours = 0.0,
                EffectiveMaturationHours = 0.0,
                Climate = new ClimateAccumulators(),
                Cask = ToStoredCaskProfile(profile),
                Volume = new MaturationVolumeState
                {
                    StartingVolumeLitres = volumeLitres,
                    CurrentVolumeLitres = volumeLitres,
                    FractionalAngelsShareRemainderLitres =
                        record.FinalizedProduct?.Volume?.FractionalAngelsShareRemainderLitres ?? 0.0
                },
                WhiskeyThief = new WhiskeyThiefState(),
                ProjectedOutcome = MaturationRecordCodec.CreateOutcome(
                    CreateEmptySnapshot(profile, currentTemp, currentRainfall)
                )
            };

            MaturationRecordCodec.Write(liquidStack, record);

            InvalidateProjectedAging(barrel);

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

            if (
                !MaturationRecordCodec.TryRead(liquidStack, out MaturationRecord record) ||
                record.State != MaturationRecordState.Active ||
                record.ActiveSession == null
            )
            {
                api.Logger.Warning(
                    "[Angel's Share] Finalizing {0}, but no active maturation session existed. Initializing late; previous sealed time cannot be recovered.",
                    liquidStack.Collectible.Code
                );

                InitializeAgingOnSeal(barrel, liquidSlot, liquidStack);

                if (!MaturationRecordCodec.TryRead(liquidStack, out record) || record.ActiveSession == null)
                    return;
            }

            double nowTotalHours = api.World.Calendar.ElapsedHours;
            ActiveMaturationSession session = record.ActiveSession;
            CaskProfile profile = FromStoredCaskProfile(session.Cask);
            AgingSnapshot result = IntegrateAndCalculate(barrel, liquidStack, record, session, nowTotalHours);
            session.ProjectedOutcome = MaturationRecordCodec.CreateOutcome(result);

            AssetLocation outputLocation;
            string outputCode = BarrelAgingUtil.TryGetAgedOutputCode(liquidStack, out outputLocation)
                ? outputLocation.ToString()
                : liquidStack.Collectible.Code.ToString();

            CompletedMaturationSession completed = new CompletedMaturationSession
            {
                Sequence = session.Sequence,
                InputLiquidCode = session.InputLiquidCode,
                OutputLiquidCode = outputCode,
                SealedAtCalendarHours = session.SealedAtCalendarHours,
                UnsealedAtCalendarHours = nowTotalHours,
                ActualElapsedHours = session.ActualElapsedHours,
                EffectiveMaturationHours = session.EffectiveMaturationHours,
                Climate = MaturationRecordCodec.CloneClimate(session.Climate),
                Cask = MaturationRecordCodec.CloneCask(session.Cask),
                Volume = MaturationRecordCodec.CloneVolume(session.Volume),
                WhiskeyThief = MaturationRecordCodec.CloneWhiskeyThief(session.WhiskeyThief),
                Outcome = MaturationRecordCodec.CloneOutcome(session.ProjectedOutcome)
            };

            record.CompletedSessions.Add(completed);
            record.FinalizedProduct = CreateFinalizedProduct(record, session, result, outputCode, nowTotalHours);
            record.ActiveSession = null;
            record.State = MaturationRecordState.Finalized;
            MaturationRecordCodec.Write(liquidStack, record);

            InvalidateProjectedAging(barrel);

            liquidSlot.MarkDirty();
            barrel.MarkDirty(true);
            api.World.BlockAccessor.MarkBlockEntityDirty(barrel.Pos);

            double elapsedCalendarDays = result.TotalHours / 24.0;

            double averageAgingSpeed = elapsedCalendarDays > 0.0
                ? result.AgeDays / elapsedCalendarDays
                : 1.0;

            double estimatedCalendarDaysToPeak = averageAgingSpeed > 0.0
                ? result.SafeWindowDays / averageAgingSpeed
                : result.SafeWindowDays;

            api.Logger.Notification(
                "[Angel's Share] Finalized aging for {0}: sealedAt={1:F2}, unsealedAt={2:F2}, totalHours={3:F2}, elapsedCalendarDays={4:F2}, estimatedCalendarDaysToPeak={5:F2}, ageDays={6:F2}, safeWindow={7:F2}, maturity={8:F3}, quality={9:F2}, intensity={10:F1}, smoothness={11:F1}, avgTemp={12:F1}, avgRain={13:F2}, trait={14}, tier={15}, special={16}",
                liquidStack.Collectible.Code,
                session.SealedAtCalendarHours,
                nowTotalHours,
                result.TotalHours,
                elapsedCalendarDays,
                estimatedCalendarDaysToPeak,
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
            if (
                barrel?.Api == null ||
                !MaturationRecordCodec.TryRead(liquidStack, out MaturationRecord record) ||
                record.State != MaturationRecordState.Active ||
                record.ActiveSession == null
            )
            {
                return null;
            }

            double nowTotalHours = barrel.Api.World.Calendar.ElapsedHours;
            double sealedAtTotalHours = record.ActiveSession.SealedAtCalendarHours;

            long halfDayBucket = (long)Math.Floor(nowTotalHours / ClimateSampleStepHours);
            string liquidCode = liquidStack.Collectible?.Code?.ToString() ?? string.Empty;

            ProjectedAgingCacheEntry cacheEntry = ProjectedAgingCache.GetValue(
                barrel,
                _ => new ProjectedAgingCacheEntry()
            );

            if (
                cacheEntry.Snapshot != null &&
                cacheEntry.HalfDayBucket == halfDayBucket &&
                cacheEntry.SealedAtCalendarHours == sealedAtTotalHours &&
                cacheEntry.LiquidCode == liquidCode &&
                cacheEntry.StackSize == liquidStack.StackSize &&
                cacheEntry.PositionX == barrel.Pos.X &&
                cacheEntry.PositionY == barrel.Pos.Y &&
                cacheEntry.PositionZ == barrel.Pos.Z
            )
            {
                return cacheEntry.Snapshot;
            }

            AgingSnapshot snapshot = IntegrateAndCalculate(
                barrel,
                liquidStack,
                record,
                record.ActiveSession,
                nowTotalHours
            );

            record.ActiveSession.ProjectedOutcome = MaturationRecordCodec.CreateOutcome(snapshot);

            if (barrel.Api.Side == EnumAppSide.Server)
            {
                MaturationRecordCodec.Write(liquidStack, record);
                ItemSlot liquidSlot = barrel.Inventory[BarrelAgingUtil.LiquidSlotId];
                liquidSlot?.MarkDirty();
                barrel.MarkDirty(true);
            }

            cacheEntry.HalfDayBucket = halfDayBucket;
            cacheEntry.SealedAtCalendarHours = sealedAtTotalHours;
            cacheEntry.LiquidCode = liquidCode;
            cacheEntry.StackSize = liquidStack.StackSize;
            cacheEntry.PositionX = barrel.Pos.X;
            cacheEntry.PositionY = barrel.Pos.Y;
            cacheEntry.PositionZ = barrel.Pos.Z;
            cacheEntry.Snapshot = snapshot;

            return snapshot;
        }

        private static void InvalidateProjectedAging(BlockEntityBarrel barrel)
        {
            if (barrel != null)
            {
                ProjectedAgingCache.Remove(barrel);
            }
        }

        private static double GetTemperatureSpeedMultiplier(double temp)
        {
            // Cold climates should age slower, but not become inert.
            // Hot climates should still accelerate maturation noticeably.
            double raw = Math.Pow(1.012, temp - 20.0);

            return Clamp(raw, 1.00, 1.80);
        }

        private static AgingSnapshot IntegrateAndCalculate(
            BlockEntityBarrel barrel,
            ItemStack liquidStack,
            MaturationRecord record,
            ActiveMaturationSession session,
            double throughCalendarHours
        )
        {
            ICoreAPI api = barrel.Api;
            BlockPos pos = barrel.Pos;
            CaskProfile profile = FromStoredCaskProfile(session.Cask);

            session.Climate ??= new ClimateAccumulators();
            session.Volume ??= new MaturationVolumeState();

            double cursor = Math.Max(
                session.SealedAtCalendarHours,
                session.LastIntegratedAtCalendarHours
            );

            if (cursor > throughCalendarHours)
                cursor = throughCalendarHours;

            while (cursor < throughCalendarHours)
            {
                double next = Math.Min(cursor + ClimateSampleStepHours, throughCalendarHours);
                double chunkHours = next - cursor;
                if (chunkHours <= 0.0) break;

                double sampleHour = cursor + (chunkHours / 2.0);
                AgingClimateSample sample = GetClimateSampleAtWorldHour(api, pos, sampleHour);
                float temp = sample.Temperature;
                float rainfall = sample.Rainfall;
                double humidityModifier = GetHumidityModifier(rainfall);
                double maturationRate =
                    GetTemperatureSpeedMultiplier(temp)
                    * humidityModifier
                    * profile.CaskVariance;
                double effectiveHours = chunkHours * maturationRate;

                session.EffectiveMaturationHours += effectiveHours;
                session.Climate.ObservedHours += chunkHours;
                session.Climate.TemperatureHourIntegral += temp * chunkHours;
                session.Climate.RainfallHourIntegral += rainfall * chunkHours;
                session.Climate.HumidityModifierHourIntegral += humidityModifier * chunkHours;
                session.Climate.MaturationRateHourIntegral += effectiveHours;
                session.Climate.SampleCount++;

                cursor = next;
            }

            session.LastIntegratedAtCalendarHours = throughCalendarHours;
            session.ActualElapsedHours = Math.Max(
                0.0,
                throughCalendarHours - session.SealedAtCalendarHours
            );
            session.Volume.CurrentVolumeLitres = MaturationRecordCodec.GetStackVolumeLitres(liquidStack);

            FinalizedMaturationProduct prior = record.FinalizedProduct;
            double totalHours = (prior?.TotalActualElapsedHours ?? 0.0) + session.ActualElapsedHours;
            double accumulatedAcceleratedHours =
                (prior?.TotalEffectiveMaturationHours ?? 0.0)
                + session.EffectiveMaturationHours;

            ClimateAccumulators aggregateClimate = prior == null
                ? MaturationRecordCodec.CloneClimate(session.Climate)
                : MergeClimate(prior.Climate, session.Climate);

            if (totalHours <= 0.0)
                return CreateEmptySnapshot(profile, 20.0, 0.5);

            double ageDays = accumulatedAcceleratedHours / 24.0;
            double averageTemp = aggregateClimate.ObservedHours > 0.0
                ? aggregateClimate.TemperatureHourIntegral / aggregateClimate.ObservedHours
                : 20.0;
            double averageRainfall = aggregateClimate.ObservedHours > 0.0
                ? aggregateClimate.RainfallHourIntegral / aggregateClimate.ObservedHours
                : 0.5;
            double averageHumidityModifier = aggregateClimate.ObservedHours > 0.0
                ? aggregateClimate.HumidityModifierHourIntegral / aggregateClimate.ObservedHours
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
            double ageStatementYears = CalculateAgeStatementYears(
                ageDays,
                safeWindowDays,
                maturityRatio,
                smoothness,
                averageTemp,
                averageRainfall
            );

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
                Extraction = Clamp(maturityRatio * 100.0, 0.0, 100.0),
                Oak = Clamp((overAgeRatio / 0.12) * 100.0, 0.0, 100.0),
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

        private static AgingSnapshot CreateEmptySnapshot(
            CaskProfile profile,
            double temperature,
            double rainfall
        )
        {
            return new AgingSnapshot
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
                AverageHumidityModifier = GetHumidityModifier((float)rainfall),
                ClimateStyle = GetClimateStyle(temperature, rainfall),
                MaturationDescriptor = "Raw",
                CaskTrait = profile?.Trait ?? "standard",
                Tier = "white",
                SpecialStyle = string.Empty,
                Proof = 0.0,
                AgeStatementYears = 0.0
            };
        }

        private static DistillationProvenance CreateInitialProvenance(ItemStack liquidStack)
        {
            string code = liquidStack?.Collectible?.Code?.ToString() ?? string.Empty;

            return new DistillationProvenance
            {
                MethodCode = "angels-share:legacy-still",
                SourceLiquidCode = string.Empty,
                DistilledSpiritCode = code,
                DistillationPasses = 1
            };
        }

        private static FinalizedMaturationProduct CreateFinalizedProduct(
            MaturationRecord record,
            ActiveMaturationSession session,
            AgingSnapshot result,
            string outputCode,
            double finalizedAtCalendarHours
        )
        {
            FinalizedMaturationProduct prior = record.FinalizedProduct;
            MaturationVolumeState volume = new MaturationVolumeState
            {
                StartingVolumeLitres = prior?.Volume?.StartingVolumeLitres > 0.0
                    ? prior.Volume.StartingVolumeLitres
                    : session.Volume.StartingVolumeLitres,
                CurrentVolumeLitres = session.Volume.CurrentVolumeLitres,
                AngelsShareLostLitres =
                    (prior?.Volume?.AngelsShareLostLitres ?? 0.0)
                    + session.Volume.AngelsShareLostLitres,
                WhiskeyThiefSampledLitres =
                    (prior?.Volume?.WhiskeyThiefSampledLitres ?? 0.0)
                    + session.Volume.WhiskeyThiefSampledLitres,
                OtherLossLitres =
                    (prior?.Volume?.OtherLossLitres ?? 0.0)
                    + session.Volume.OtherLossLitres,
                FractionalAngelsShareRemainderLitres =
                    session.Volume.FractionalAngelsShareRemainderLitres
            };

            return new FinalizedMaturationProduct
            {
                ProductLiquidCode = outputCode,
                FinalizedAtCalendarHours = finalizedAtCalendarHours,
                CompletedSessionCount = record.CompletedSessions.Count,
                TotalActualElapsedHours = result.TotalHours,
                TotalEffectiveMaturationHours = result.AgeHours,
                Climate = prior == null
                    ? MaturationRecordCodec.CloneClimate(session.Climate)
                    : MergeClimate(prior.Climate, session.Climate),
                Volume = volume,
                WhiskeyThief = MergeWhiskeyThief(prior?.WhiskeyThief, session.WhiskeyThief),
                Outcome = MaturationRecordCodec.CreateOutcome(result)
            };
        }

        private static ClimateAccumulators MergeClimate(
            ClimateAccumulators first,
            ClimateAccumulators second
        )
        {
            first ??= new ClimateAccumulators();
            second ??= new ClimateAccumulators();

            return new ClimateAccumulators
            {
                ObservedHours = first.ObservedHours + second.ObservedHours,
                TemperatureHourIntegral =
                    first.TemperatureHourIntegral + second.TemperatureHourIntegral,
                RainfallHourIntegral = first.RainfallHourIntegral + second.RainfallHourIntegral,
                HumidityModifierHourIntegral =
                    first.HumidityModifierHourIntegral + second.HumidityModifierHourIntegral,
                MaturationRateHourIntegral =
                    first.MaturationRateHourIntegral + second.MaturationRateHourIntegral,
                EvaporationRateHourIntegral =
                    first.EvaporationRateHourIntegral + second.EvaporationRateHourIntegral,
                SampleCount = first.SampleCount + second.SampleCount
            };
        }

        private static WhiskeyThiefState MergeWhiskeyThief(
            WhiskeyThiefState first,
            WhiskeyThiefState second
        )
        {
            first ??= new WhiskeyThiefState();
            second ??= new WhiskeyThiefState();

            return new WhiskeyThiefState
            {
                SampleCount = first.SampleCount + second.SampleCount,
                Exposure = first.Exposure + second.Exposure,
                LastSampledAtCalendarHours = second.LastSampledAtCalendarHours
                    ?? first.LastSampledAtCalendarHours
            };
        }

        private static MaturationCaskProfile ToStoredCaskProfile(CaskProfile profile)
        {
            profile ??= new CaskProfile { Trait = "standard", CaskVariance = 1.0 };

            return new MaturationCaskProfile
            {
                ProfileCode = "angels-share:" + (profile.Trait ?? "standard"),
                RollSeed = profile.RollSeed,
                MaturationRateMultiplier = profile.CaskVariance,
                IntensityBonus = profile.IntensityBonus,
                SmoothnessBonus = profile.SmoothnessBonus,
                QualityBonus = profile.QualityBonus,
                ExtractionMultiplier = 1.0,
                SafeWindowMultiplier = profile.SafeWindowMultiplier,
                OverOakResistance = profile.OverOakResistance
            };
        }

        private static CaskProfile FromStoredCaskProfile(MaturationCaskProfile stored)
        {
            stored ??= new MaturationCaskProfile();
            string trait = stored.ProfileCode ?? "standard";
            int separator = trait.IndexOf(':');
            if (separator >= 0 && separator < trait.Length - 1)
                trait = trait.Substring(separator + 1);

            return new CaskProfile
            {
                RollSeed = stored.RollSeed,
                Trait = trait,
                CaskVariance = stored.MaturationRateMultiplier > 0.0
                    ? stored.MaturationRateMultiplier
                    : 1.0,
                IntensityBonus = stored.IntensityBonus,
                SmoothnessBonus = stored.SmoothnessBonus,
                QualityBonus = stored.QualityBonus,
                SafeWindowMultiplier = stored.SafeWindowMultiplier > 0.0
                    ? stored.SafeWindowMultiplier
                    : 1.0,
                OverOakResistance = stored.OverOakResistance > 0.0
                    ? stored.OverOakResistance
                    : 1.0
            };
        }

        private static double GetSafeWindowDaysFromClimate(double averageTemp, double averageRainfall, CaskProfile profile)
        {
            // Singleplayer default:
            // Hot/dry barrels peak quickly,
            // Temperate barrels take a moderate amount of time,
            // Cold/humid barrels remain slower
            double safeWindowDays = 18.0;

            if (averageTemp > 20.0)
            {
                safeWindowDays -= (averageTemp - 20.0) * 0.70;
            }
            else if (averageTemp < 20.0)
            {
                safeWindowDays += (20.0 - averageTemp) * 0.65;
            }

            if (averageRainfall > 0.65)
            {
                safeWindowDays += 4.0 + ((averageRainfall - 0.5) * 10.0);
            }
            else if (averageRainfall < 0.30)
            {
                safeWindowDays -= 4.0 + ((0.30 - averageRainfall) * 8.0);
            }

            safeWindowDays *= profile.SafeWindowMultiplier;

            return Clamp(safeWindowDays, 6.0, 42.0);
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

            bool unicornTrait = profile.Trait == "unicorn";
            bool tightGrainTrait = profile.Trait == "tight-grain";

            double ageStatementYears = CalculateAgeStatementYears(
                ageDays,
                safeWindowDays,
                maturityRatio,
                smoothness,
                averageTemp,
                averageRainfall
            );

            bool caskStrengthCandidate =
                quality >= 82.0 &&
                intensity >= 82.0 &&
                maturityRatio >= 0.78 &&
                (averageTemp >= 22.0 || averageRainfall <= 0.35);

            bool ageStatedCandidate =
                quality >= 88.0 &&
                smoothness >= 70.0 &&
                maturityRatio >= 0.88 &&
                ageStatementYears >= 8.0;

            bool exceptionalBalance =
                quality >= 92.0 &&
                intensity >= 75.0 &&
                smoothness >= 75.0;

            bool unicornAgeStatedCandidate =
                ageStatedCandidate &&
                quality >= 94.0 &&
                smoothness >= 82.0 &&
                ageStatementYears >= 14.0 &&
                (unicornTrait || tightGrainTrait || safeWindowDays >= 32.0);

            bool unicornCaskStrengthCandidate =
                caskStrengthCandidate &&
                quality >= 94.0 &&
                intensity >= 90.0 &&
                (unicornTrait || exceptionalBalance);

            if (ageStatedCandidate && caskStrengthCandidate)
            {
                int years = (int)ageStatementYears;

                if (unicornAgeStatedCandidate || unicornCaskStrengthCandidate)
                {
                    return "Unicorn: " + years + "-Year Old Cask-Strength Reserve";
                }

                return years + "-Year Old Cask-Strength Reserve";
            }

            if (ageStatedCandidate)
            {
                int years = (int)ageStatementYears;

                if (unicornAgeStatedCandidate)
                {
                    return "Unicorn: " + years + "-Year Old Reserve";
                }

                return years + "-Year Old Reserve";
            }

            if (caskStrengthCandidate)
            {
                if (unicornCaskStrengthCandidate)
                {
                    return "Unicorn: Cask-Strength Reserve";
                }
                
                return "Cask-Strength Reserve";
            }

            return "";
        }

        private static double CalculateProofFromIntensity(double intensity)
        {
            if (intensity < 75.0) return 0.0;

            double proof = 120.0 + ((intensity - 75.0) / 25.0) * 35.0;

            return Clamp(proof, 120.0, 155.0);
        }

        private static double CalculateAgeStatementYears(
            double ageDays,
            double safeWindowDays,
            double maturityRatio,
            double smoothness,
            double averageTemp,
            double averageRainfall
        )
        {
            if (safeWindowDays < 18.0)
                return 0.0;

            if (maturityRatio < 0.88)
                return 0.0;

            if (smoothness < 65.0)
                return 0.0;

            double slowWindowScore = Clamp((safeWindowDays - 18.0) / 24.0, 0.0, 1.0);
            double smoothnessScore = Clamp((smoothness - 65.0) / 35.0, 0.0, 1.0);
            double maturityScore = Clamp((maturityRatio - 0.88) / 0.17, 0.0, 1.0);

            double coldBonus = Clamp((16.0 - averageTemp) / 18.0, 0.0, 1.0);
            double humidBonus = Clamp((averageRainfall - 0.35) / 0.45, 0.0, 1.0);

            double prestigeScore =
                (slowWindowScore * 0.45) +
                (smoothnessScore * 0.25) +
                (maturityScore * 0.20) +
                (coldBonus * 0.05) +
                (humidBonus * 0.05);

            // A minimum age statement of 8 years is applied
            double years = 8.0 + (prestigeScore * 17.0);

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
                    OverOakResistance = 0.70,
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
                    OverOakResistance = 0.78,
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
                    OverOakResistance = 1.22,
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
                    OverOakResistance = 0.90,
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
                    OverOakResistance = 1.03,
                }
            }
        };

        private static CaskProfile CreateStandardCaskProfile(Random rand)
        {
            return new CaskProfile
            {
                Trait = "standard",
                CaskVariance = 0.94 + (rand.NextDouble() * 0.12),
                IntensityBonus = 0.0,
                SmoothnessBonus = 0.0,
                QualityBonus = -3.0 + (rand.NextDouble() * 6.0),
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
                CaskProfile selected = selectedRule.CreateProfile(rand);
                selected.RollSeed = seed;
                return selected;
            }

            CaskProfile standard = CreateStandardCaskProfile(rand);
            standard.RollSeed = seed;
            return standard;
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
