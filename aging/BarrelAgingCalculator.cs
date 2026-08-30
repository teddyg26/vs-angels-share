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
                    MaturationMath.Calculate(new MaturationCalculationInput
                    {
                        LiquidCode = liquidStack.Collectible?.Code?.ToString() ?? string.Empty,
                        AverageTemperature = currentTemp,
                        AverageRainfall = currentRainfall,
                        AverageHumidityModifier = MaturationMath.GetHumidityModifier(currentRainfall),
                        Cask = profile,
                        Loss = new MaturationLossInput
                        {
                            StartingVolumeLitres = volumeLitres,
                            CurrentVolumeLitres = volumeLitres
                        }
                    }).Snapshot
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
            MaturationCalculationResult calculation = IntegrateAndCalculate(
                barrel,
                liquidStack,
                record,
                session,
                nowTotalHours
            );
            AgingSnapshot result = calculation.Snapshot;
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
            record.FinalizedProduct = CreateFinalizedProduct(
                record,
                session,
                result,
                calculation.Loss,
                outputCode,
                nowTotalHours
            );
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
            ).Snapshot;

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

        private static MaturationCalculationResult IntegrateAndCalculate(
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
                MaturationClimatePeriodResult climatePeriod =
                    MaturationMath.CalculateClimatePeriod(
                        chunkHours,
                        temp,
                        rainfall,
                        profile
                    );

                session.EffectiveMaturationHours += climatePeriod.EffectiveMaturationHours;
                session.Climate.ObservedHours += chunkHours;
                session.Climate.TemperatureHourIntegral += temp * chunkHours;
                session.Climate.RainfallHourIntegral += rainfall * chunkHours;
                session.Climate.HumidityModifierHourIntegral +=
                    climatePeriod.HumidityModifier * chunkHours;
                session.Climate.MaturationRateHourIntegral +=
                    climatePeriod.EffectiveMaturationHours;
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

            double averageTemp = aggregateClimate.ObservedHours > 0.0
                ? aggregateClimate.TemperatureHourIntegral / aggregateClimate.ObservedHours
                : 20.0;
            double averageRainfall = aggregateClimate.ObservedHours > 0.0
                ? aggregateClimate.RainfallHourIntegral / aggregateClimate.ObservedHours
                : 0.5;
            double averageHumidityModifier = aggregateClimate.ObservedHours > 0.0
                ? aggregateClimate.HumidityModifierHourIntegral / aggregateClimate.ObservedHours
                : 1.0;

            return MaturationMath.Calculate(new MaturationCalculationInput
            {
                LiquidCode = liquidStack.Collectible?.Code?.ToString() ?? string.Empty,
                TotalActualElapsedHours = totalHours,
                TotalEffectiveMaturationHours = accumulatedAcceleratedHours,
                AverageTemperature = averageTemp,
                AverageRainfall = averageRainfall,
                AverageHumidityModifier = averageHumidityModifier,
                Cask = profile,
                Loss = CreateAggregateLossInput(record.FinalizedProduct, session)
            });
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
            MaturationLossResult loss,
            string outputCode,
            double finalizedAtCalendarHours
        )
        {
            FinalizedMaturationProduct prior = record.FinalizedProduct;
            loss ??= MaturationMath.CalculateLoss(
                CreateAggregateLossInput(prior, session)
            );
            MaturationVolumeState volume = new MaturationVolumeState
            {
                StartingVolumeLitres = loss.StartingVolumeLitres,
                CurrentVolumeLitres = loss.CurrentVolumeLitres,
                AngelsShareLostLitres = loss.AngelsShareLostLitres,
                WhiskeyThiefSampledLitres = loss.WhiskeyThiefSampledLitres,
                OtherLossLitres = loss.OtherLossLitres,
                FractionalAngelsShareRemainderLitres = loss.FractionalAngelsShareRemainderLitres
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

        private static MaturationLossInput CreateAggregateLossInput(
            FinalizedMaturationProduct prior,
            ActiveMaturationSession session
        )
        {
            session ??= new ActiveMaturationSession();
            session.Volume ??= new MaturationVolumeState();

            return new MaturationLossInput
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

        private static CaskProfile RollCaskProfile(BlockEntityBarrel barrel, ItemStack liquidStack, double sealedAtTotalHours)
        {
            int seed = MaturationMath.MakeCaskSeed(
                barrel.Pos.X,
                barrel.Pos.Y,
                barrel.Pos.Z,
                sealedAtTotalHours,
                liquidStack.StackSize,
                liquidStack.Collectible.Code.ToString()
            );

            return MaturationMath.RollCaskProfile(seed);
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
            // GetClimateAt(..., ForSuppliedDate_*, totalDays) expects calendar
            // days, while the maturation record and Vintage Story calendar use
            // elapsed hours as their canonical stored unit.
            double worldDay = MaturationMath.ConvertCalendarHoursToDays(worldHour);

            if (parameterType == typeof(double))
                return worldDay;

            if (parameterType == typeof(float))
                return (float)worldDay;

            if (parameterType == typeof(int))
                return (int)worldDay;

            if (parameterType == typeof(long))
                return (long)worldDay;

            return null;
        }

    }
}
