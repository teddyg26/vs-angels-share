using System;
using System.Collections.Generic;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace AngelsShare
{
    public static class MaturationRecordCodec
    {
        private const string ActiveStateCode = "active";
        private const string FinalizedStateCode = "finalized";

        public static bool HasStoredRecord(ItemStack stack)
        {
            return stack?.Attributes?.GetTreeAttribute(MaturationSchema.RootAttributeName) != null;
        }

        public static int GetStoredSchemaVersion(ItemStack stack)
        {
            ITreeAttribute tree = stack?.Attributes?.GetTreeAttribute(MaturationSchema.RootAttributeName);
            return tree?.GetInt(
                "schemaVersion",
                MaturationSchema.LegacyUnversionedVersion
            ) ?? MaturationSchema.LegacyUnversionedVersion;
        }

        public static bool TryRead(ItemStack stack, out MaturationRecord record)
        {
            record = null;

            ITreeAttribute tree = stack?.Attributes?.GetTreeAttribute(MaturationSchema.RootAttributeName);
            if (tree == null) return false;

            int version = tree.GetInt("schemaVersion", MaturationSchema.LegacyUnversionedVersion);

            if (version == MaturationSchema.LegacyUnversionedVersion)
            {
                record = ReadLegacyRecord(stack, tree);
                return record != null;
            }

            if (version != MaturationSchema.CurrentVersion)
            {
                return false;
            }

            record = ReadVersionOne(tree);
            return record != null;
        }

        public static bool HasActiveSession(ItemStack stack)
        {
            return TryRead(stack, out MaturationRecord record)
                && record.State == MaturationRecordState.Active
                && record.ActiveSession != null;
        }

        public static bool HasFinalizedProduct(ItemStack stack)
        {
            return TryRead(stack, out MaturationRecord record)
                && record.FinalizedProduct != null;
        }

        public static void Write(ItemStack stack, MaturationRecord record)
        {
            if (stack?.Attributes == null)
                throw new ArgumentNullException(nameof(stack));

            ValidateForWrite(record);

            stack.Attributes.RemoveAttribute(MaturationSchema.RootAttributeName);
            ITreeAttribute tree = stack.Attributes.GetOrAddTreeAttribute(MaturationSchema.RootAttributeName);

            tree.SetInt("schemaVersion", MaturationSchema.CurrentVersion);
            tree.SetString("state", ToStateCode(record.State));

            WriteProvenance(NewChild(tree, "provenance"), record.Provenance);

            if (record.ActiveSession != null)
            {
                WriteActiveSession(NewChild(tree, "activeSession"), record.ActiveSession);
            }

            if (record.FinalizedProduct != null)
            {
                WriteFinalizedProduct(NewChild(tree, "finalizedProduct"), record.FinalizedProduct);
            }

            WriteCompletedSessions(NewChild(tree, "completedSessions"), record.CompletedSessions);
            WriteExtensions(tree, record.Extensions);
        }

        public static double GetStackVolumeLitres(ItemStack stack)
        {
            if (stack == null) return 0.0;

            WaterTightContainableProps props = BlockLiquidContainerBase.GetContainableProps(stack);
            if (props == null || props.ItemsPerLitre <= 0.0f) return 0.0;

            return stack.StackSize / (double)props.ItemsPerLitre;
        }

        public static MaturationOutcome CreateOutcome(AgingSnapshot snapshot)
        {
            MaturationOutcome outcome = new MaturationOutcome
            {
                Quality = snapshot?.Quality ?? 0.0,
                Intensity = snapshot?.Intensity ?? 0.0,
                Smoothness = snapshot?.Smoothness ?? 0.0,
                Balance = snapshot?.Balance ?? 0.0,
                Extraction = snapshot?.Extraction ?? 0.0,
                Oak = snapshot?.Oak ?? 0.0,
                TierCode = snapshot?.Tier ?? "white",
                MaturationStageCode = snapshot?.MaturationDescriptor ?? "Raw",
                ClimateStyleCode = snapshot?.ClimateStyle ?? "Standard Continental Maturation"
            };

            if (snapshot == null) return outcome;

            AddLegacyDesignations(
                outcome,
                snapshot.SpecialStyle,
                snapshot.Proof,
                snapshot.AgeStatementYears
            );

            outcome.Extensions.SetDouble("safeWindowDays", snapshot.SafeWindowDays);
            outcome.Extensions.SetDouble("maturityRatio", snapshot.MaturityRatio);
            outcome.Extensions.SetDouble("overAgeRatio", snapshot.OverAgeRatio);

            return outcome;
        }

        public static double GetDesignationValue(
            MaturationOutcome outcome,
            string designationCode,
            double defaultValue = 0.0
        )
        {
            if (outcome?.Designations == null) return defaultValue;

            foreach (MaturationDesignation designation in outcome.Designations)
            {
                if (designation?.Code == designationCode && designation.NumericValue.HasValue)
                {
                    return designation.NumericValue.Value;
                }
            }

            return defaultValue;
        }

        public static bool HasDesignation(MaturationOutcome outcome, string designationCode)
        {
            if (outcome?.Designations == null) return false;

            foreach (MaturationDesignation designation in outcome.Designations)
            {
                if (designation?.Code == designationCode) return true;
            }

            return false;
        }

        private static MaturationRecord ReadVersionOne(ITreeAttribute tree)
        {
            MaturationRecordState state = FromStateCode(tree.GetString("state", string.Empty));
            if (state == 0) return null;

            MaturationRecord record = new MaturationRecord
            {
                SchemaVersion = MaturationSchema.CurrentVersion,
                State = state,
                Provenance = ReadProvenance(tree.GetTreeAttribute("provenance")),
                ActiveSession = ReadActiveSession(tree.GetTreeAttribute("activeSession")),
                FinalizedProduct = ReadFinalizedProduct(tree.GetTreeAttribute("finalizedProduct")),
                CompletedSessions = ReadCompletedSessions(tree.GetTreeAttribute("completedSessions")),
                Extensions = ReadExtensions(tree)
            };

            if (record.State == MaturationRecordState.Active && record.ActiveSession == null)
                return null;

            if (record.State == MaturationRecordState.Finalized && record.FinalizedProduct == null)
                return null;

            return record;
        }

        private static MaturationRecord ReadLegacyRecord(ItemStack stack, ITreeAttribute tree)
        {
            bool legacyFinalized =
                tree.GetBool("angelsshareAged", false) ||
                tree.HasAttribute("unsealedAtTotalHours");

            bool legacyActive = tree.HasAttribute("sealedAtTotalHours") && !legacyFinalized;
            if (!legacyActive && !legacyFinalized) return null;

            string stackCode = stack?.Collectible?.Code?.ToString() ?? string.Empty;
            string inputCode = tree.GetString("agedFrom", stackCode);
            string outputCode = tree.GetString("agedInto", stackCode);

            double sealedAt = tree.GetDouble("sealedAtTotalHours", 0.0);
            double actualHours = Math.Max(0.0, tree.GetDouble("ageHoursTotal", 0.0));
            double effectiveHours = Math.Max(0.0, tree.GetDouble("ageHours", 0.0));
            double unsealedAt = tree.GetDouble("unsealedAtTotalHours", sealedAt + actualHours);
            double volumeLitres = GetStackVolumeLitres(stack);

            ClimateAccumulators climate = ReadLegacyClimate(tree, actualHours, effectiveHours);
            MaturationCaskProfile cask = ReadLegacyCask(tree);
            MaturationOutcome outcome = ReadLegacyOutcome(tree);

            MaturationRecord record = new MaturationRecord
            {
                SchemaVersion = MaturationSchema.CurrentVersion,
                State = legacyActive ? MaturationRecordState.Active : MaturationRecordState.Finalized,
                Provenance = new DistillationProvenance
                {
                    MethodCode = "angels-share:legacy-unknown",
                    SourceLiquidCode = inputCode,
                    DistilledSpiritCode = inputCode,
                    DistillationPasses = 0
                }
            };

            if (legacyActive)
            {
                record.ActiveSession = new ActiveMaturationSession
                {
                    Sequence = 1,
                    InputLiquidCode = inputCode,
                    SealedAtCalendarHours = sealedAt,
                    LastIntegratedAtCalendarHours = sealedAt + actualHours,
                    ActualElapsedHours = actualHours,
                    EffectiveMaturationHours = effectiveHours,
                    Climate = climate,
                    Cask = cask,
                    Volume = CreateVolume(volumeLitres, volumeLitres),
                    ProjectedOutcome = outcome
                };
            }
            else
            {
                CompletedMaturationSession completed = new CompletedMaturationSession
                {
                    Sequence = 1,
                    InputLiquidCode = inputCode,
                    OutputLiquidCode = outputCode,
                    SealedAtCalendarHours = sealedAt,
                    UnsealedAtCalendarHours = unsealedAt,
                    ActualElapsedHours = actualHours,
                    EffectiveMaturationHours = effectiveHours,
                    Climate = climate,
                    Cask = cask,
                    Volume = CreateVolume(volumeLitres, volumeLitres),
                    Outcome = outcome
                };

                record.CompletedSessions.Add(completed);
                record.FinalizedProduct = new FinalizedMaturationProduct
                {
                    ProductLiquidCode = outputCode,
                    FinalizedAtCalendarHours = unsealedAt,
                    CompletedSessionCount = 1,
                    TotalActualElapsedHours = actualHours,
                    TotalEffectiveMaturationHours = effectiveHours,
                    Climate = CloneClimate(climate),
                    Volume = CreateVolume(volumeLitres, volumeLitres),
                    Outcome = CloneOutcome(outcome)
                };
            }

            record.Extensions.SetBool("migratedFromLegacyUnversioned", true);
            return record;
        }

        private static ClimateAccumulators ReadLegacyClimate(
            ITreeAttribute tree,
            double actualHours,
            double effectiveHours
        )
        {
            double averageTemperature = tree.GetDouble("averageTemperature", 20.0);
            double averageRainfall = tree.GetDouble("averageRainfall", 0.5);
            double averageHumidity = tree.GetDouble("averageHumidityModifier", 1.0);

            return new ClimateAccumulators
            {
                ObservedHours = actualHours,
                TemperatureHourIntegral = averageTemperature * actualHours,
                RainfallHourIntegral = averageRainfall * actualHours,
                HumidityModifierHourIntegral = averageHumidity * actualHours,
                MaturationRateHourIntegral = effectiveHours,
                SampleCount = actualHours > 0.0 ? Math.Max(1, (int)Math.Ceiling(actualHours / 12.0)) : 0
            };
        }

        private static MaturationCaskProfile ReadLegacyCask(ITreeAttribute tree)
        {
            string trait = tree.GetString("caskTrait", "standard");

            return new MaturationCaskProfile
            {
                ProfileCode = NamespacedCode(trait),
                RollSeed = 0,
                MaturationRateMultiplier = tree.GetDouble("caskVarianceSeed", 1.0),
                IntensityBonus = tree.GetDouble("caskIntensityBonus", 0.0),
                SmoothnessBonus = tree.GetDouble("caskSmoothnessBonus", 0.0),
                QualityBonus = tree.GetDouble("caskQualityBonus", 0.0),
                ExtractionMultiplier = 1.0,
                SafeWindowMultiplier = tree.GetDouble("caskSafeWindowMultiplier", 1.0),
                OverOakResistance = tree.GetDouble("caskOverOakResistance", 1.0)
            };
        }

        private static MaturationOutcome ReadLegacyOutcome(ITreeAttribute tree)
        {
            double maturityRatio = tree.GetDouble("maturityRatio", 0.0);
            double overAgeRatio = tree.GetDouble("overAgeRatio", 0.0);

            MaturationOutcome outcome = new MaturationOutcome
            {
                Quality = tree.GetDouble("quality", 0.0),
                Intensity = tree.GetDouble("intensity", 0.0),
                Smoothness = tree.GetDouble("smoothness", 0.0),
                Balance = tree.GetDouble("balance", 0.0),
                Extraction = Clamp(maturityRatio * 100.0, 0.0, 100.0),
                Oak = Clamp((overAgeRatio / 0.12) * 100.0, 0.0, 100.0),
                TierCode = tree.GetString("ageTier", "white"),
                MaturationStageCode = tree.GetString("maturationDescriptor", "Raw"),
                ClimateStyleCode = tree.GetString(
                    "climateStyle",
                    "Standard Continental Maturation"
                )
            };

            AddLegacyDesignations(
                outcome,
                tree.GetString("specialStyle", string.Empty),
                tree.GetDouble("proof", 0.0),
                tree.GetDouble("ageStatementYears", 0.0)
            );

            outcome.Extensions.SetDouble("safeWindowDays", tree.GetDouble("safeWindowDays", 18.0));
            outcome.Extensions.SetDouble("maturityRatio", tree.GetDouble("maturityRatio", 0.0));
            outcome.Extensions.SetDouble("overAgeRatio", tree.GetDouble("overAgeRatio", 0.0));

            return outcome;
        }

        private static void AddLegacyDesignations(
            MaturationOutcome outcome,
            string specialStyle,
            double proof,
            double ageStatementYears
        )
        {
            bool hasCaskStrength =
                proof > 0.0 ||
                (!string.IsNullOrEmpty(specialStyle) &&
                    (specialStyle.IndexOf("Cask-Strength", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     specialStyle.IndexOf("Cask-Strength", StringComparison.OrdinalIgnoreCase) >= 0));

            bool hasAgeStatement =
                ageStatementYears > 0.0 ||
                (!string.IsNullOrEmpty(specialStyle) &&
                    specialStyle.IndexOf("Age-Stated", StringComparison.OrdinalIgnoreCase) >= 0);

            if (hasAgeStatement)
            {
                outcome.Designations.Add(new MaturationDesignation
                {
                    Code = "angels-share:age-stated",
                    NumericValue = ageStatementYears > 0.0 ? ageStatementYears : null,
                    UnitCode = "years"
                });
            }

            if (hasCaskStrength)
            {
                outcome.Designations.Add(new MaturationDesignation
                {
                    Code = "angels-share:cask-strength",
                    NumericValue = proof > 0.0 ? proof : null,
                    UnitCode = "proof"
                });
            }

            if (!string.IsNullOrEmpty(specialStyle) && !hasAgeStatement && !hasCaskStrength)
            {
                outcome.Designations.Add(new MaturationDesignation
                {
                    Code = "angels-share:legacy-special",
                    UnitCode = string.Empty,
                    Extensions = CreateLegacyTextExtension(specialStyle)
                });
            }
        }

        private static TreeAttribute CreateLegacyTextExtension(string value)
        {
            TreeAttribute extension = new TreeAttribute();
            extension.SetString("displayName", value ?? string.Empty);
            return extension;
        }

        private static MaturationRecordState FromStateCode(string state)
        {
            if (state == ActiveStateCode) return MaturationRecordState.Active;
            if (state == FinalizedStateCode) return MaturationRecordState.Finalized;
            return 0;
        }

        private static string ToStateCode(MaturationRecordState state)
        {
            if (state == MaturationRecordState.Active) return ActiveStateCode;
            if (state == MaturationRecordState.Finalized) return FinalizedStateCode;
            throw new InvalidOperationException("Unknown maturation record state: " + state);
        }

        private static void ValidateForWrite(MaturationRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (record.SchemaVersion != MaturationSchema.CurrentVersion)
                throw new InvalidOperationException("Only the current maturation schema can be written.");

            if (record.State == MaturationRecordState.Active && record.ActiveSession == null)
                throw new InvalidOperationException("An active maturation record requires an active session.");

            if (record.State == MaturationRecordState.Finalized)
            {
                if (record.ActiveSession != null)
                    throw new InvalidOperationException("A finalized maturation record cannot retain an active session.");

                if (record.FinalizedProduct == null)
                    throw new InvalidOperationException("A finalized record requires finalized product data.");
            }

            record.Provenance ??= new DistillationProvenance();
            record.CompletedSessions ??= new List<CompletedMaturationSession>();
            record.Extensions ??= new TreeAttribute();
        }

        private static void WriteActiveSession(ITreeAttribute tree, ActiveMaturationSession value)
        {
            tree.SetInt("sequence", value.Sequence);
            tree.SetString("inputLiquidCode", value.InputLiquidCode ?? string.Empty);
            tree.SetDouble("sealedAtCalendarHours", value.SealedAtCalendarHours);
            tree.SetDouble("lastIntegratedAtCalendarHours", value.LastIntegratedAtCalendarHours);
            tree.SetDouble("actualElapsedHours", value.ActualElapsedHours);
            tree.SetDouble("effectiveMaturationHours", value.EffectiveMaturationHours);
            WriteClimate(NewChild(tree, "climate"), value.Climate);
            WriteCask(NewChild(tree, "cask"), value.Cask);
            WriteVolume(NewChild(tree, "volume"), value.Volume);
            WriteWhiskeyThief(NewChild(tree, "whiskeyThief"), value.WhiskeyThief);
            WriteOutcome(NewChild(tree, "projectedOutcome"), value.ProjectedOutcome);
            WriteExtensions(tree, value.Extensions);
        }

        private static ActiveMaturationSession ReadActiveSession(ITreeAttribute tree)
        {
            if (tree == null) return null;

            return new ActiveMaturationSession
            {
                Sequence = tree.GetInt("sequence", 1),
                InputLiquidCode = tree.GetString("inputLiquidCode", string.Empty),
                SealedAtCalendarHours = tree.GetDouble("sealedAtCalendarHours", 0.0),
                LastIntegratedAtCalendarHours = tree.GetDouble("lastIntegratedAtCalendarHours", 0.0),
                ActualElapsedHours = tree.GetDouble("actualElapsedHours", 0.0),
                EffectiveMaturationHours = tree.GetDouble("effectiveMaturationHours", 0.0),
                Climate = ReadClimate(tree.GetTreeAttribute("climate")),
                Cask = ReadCask(tree.GetTreeAttribute("cask")),
                Volume = ReadVolume(tree.GetTreeAttribute("volume")),
                WhiskeyThief = ReadWhiskeyThief(tree.GetTreeAttribute("whiskeyThief")),
                ProjectedOutcome = ReadOutcome(tree.GetTreeAttribute("projectedOutcome")),
                Extensions = ReadExtensions(tree)
            };
        }

        private static void WriteCompletedSessions(
            ITreeAttribute tree,
            List<CompletedMaturationSession> sessions
        )
        {
            sessions ??= new List<CompletedMaturationSession>();
            tree.SetInt("count", sessions.Count);

            for (int i = 0; i < sessions.Count; i++)
            {
                WriteCompletedSession(NewChild(tree, i.ToString()), sessions[i]);
            }
        }

        private static List<CompletedMaturationSession> ReadCompletedSessions(ITreeAttribute tree)
        {
            List<CompletedMaturationSession> sessions = new List<CompletedMaturationSession>();
            if (tree == null) return sessions;

            int count = Math.Max(0, tree.GetInt("count", 0));

            for (int i = 0; i < count; i++)
            {
                CompletedMaturationSession session = ReadCompletedSession(
                    tree.GetTreeAttribute(i.ToString())
                );

                if (session != null) sessions.Add(session);
            }

            return sessions;
        }

        private static void WriteCompletedSession(ITreeAttribute tree, CompletedMaturationSession value)
        {
            tree.SetInt("sequence", value.Sequence);
            tree.SetString("inputLiquidCode", value.InputLiquidCode ?? string.Empty);
            tree.SetString("outputLiquidCode", value.OutputLiquidCode ?? string.Empty);
            tree.SetDouble("sealedAtCalendarHours", value.SealedAtCalendarHours);
            tree.SetDouble("unsealedAtCalendarHours", value.UnsealedAtCalendarHours);
            tree.SetDouble("actualElapsedHours", value.ActualElapsedHours);
            tree.SetDouble("effectiveMaturationHours", value.EffectiveMaturationHours);
            WriteClimate(NewChild(tree, "climate"), value.Climate);
            WriteCask(NewChild(tree, "cask"), value.Cask);
            WriteVolume(NewChild(tree, "volume"), value.Volume);
            WriteWhiskeyThief(NewChild(tree, "whiskeyThief"), value.WhiskeyThief);
            WriteOutcome(NewChild(tree, "outcome"), value.Outcome);
            WriteExtensions(tree, value.Extensions);
        }

        private static CompletedMaturationSession ReadCompletedSession(ITreeAttribute tree)
        {
            if (tree == null) return null;

            return new CompletedMaturationSession
            {
                Sequence = tree.GetInt("sequence", 1),
                InputLiquidCode = tree.GetString("inputLiquidCode", string.Empty),
                OutputLiquidCode = tree.GetString("outputLiquidCode", string.Empty),
                SealedAtCalendarHours = tree.GetDouble("sealedAtCalendarHours", 0.0),
                UnsealedAtCalendarHours = tree.GetDouble("unsealedAtCalendarHours", 0.0),
                ActualElapsedHours = tree.GetDouble("actualElapsedHours", 0.0),
                EffectiveMaturationHours = tree.GetDouble("effectiveMaturationHours", 0.0),
                Climate = ReadClimate(tree.GetTreeAttribute("climate")),
                Cask = ReadCask(tree.GetTreeAttribute("cask")),
                Volume = ReadVolume(tree.GetTreeAttribute("volume")),
                WhiskeyThief = ReadWhiskeyThief(tree.GetTreeAttribute("whiskeyThief")),
                Outcome = ReadOutcome(tree.GetTreeAttribute("outcome")),
                Extensions = ReadExtensions(tree)
            };
        }

        private static void WriteFinalizedProduct(ITreeAttribute tree, FinalizedMaturationProduct value)
        {
            tree.SetString("productLiquidCode", value.ProductLiquidCode ?? string.Empty);
            tree.SetDouble("finalizedAtCalendarHours", value.FinalizedAtCalendarHours);
            tree.SetInt("completedSessionCount", value.CompletedSessionCount);
            tree.SetDouble("totalActualElapsedHours", value.TotalActualElapsedHours);
            tree.SetDouble("totalEffectiveMaturationHours", value.TotalEffectiveMaturationHours);
            WriteClimate(NewChild(tree, "climate"), value.Climate);
            WriteVolume(NewChild(tree, "volume"), value.Volume);
            WriteWhiskeyThief(NewChild(tree, "whiskeyThief"), value.WhiskeyThief);
            WriteOutcome(NewChild(tree, "outcome"), value.Outcome);
            WriteExtensions(tree, value.Extensions);
        }

        private static FinalizedMaturationProduct ReadFinalizedProduct(ITreeAttribute tree)
        {
            if (tree == null) return null;

            return new FinalizedMaturationProduct
            {
                ProductLiquidCode = tree.GetString("productLiquidCode", string.Empty),
                FinalizedAtCalendarHours = tree.GetDouble("finalizedAtCalendarHours", 0.0),
                CompletedSessionCount = tree.GetInt("completedSessionCount", 0),
                TotalActualElapsedHours = tree.GetDouble("totalActualElapsedHours", 0.0),
                TotalEffectiveMaturationHours = tree.GetDouble("totalEffectiveMaturationHours", 0.0),
                Climate = ReadClimate(tree.GetTreeAttribute("climate")),
                Volume = ReadVolume(tree.GetTreeAttribute("volume")),
                WhiskeyThief = ReadWhiskeyThief(tree.GetTreeAttribute("whiskeyThief")),
                Outcome = ReadOutcome(tree.GetTreeAttribute("outcome")),
                Extensions = ReadExtensions(tree)
            };
        }

        private static void WriteClimate(ITreeAttribute tree, ClimateAccumulators value)
        {
            value ??= new ClimateAccumulators();
            tree.SetDouble("observedHours", value.ObservedHours);
            tree.SetDouble("temperatureHourIntegral", value.TemperatureHourIntegral);
            tree.SetDouble("rainfallHourIntegral", value.RainfallHourIntegral);
            tree.SetDouble("humidityModifierHourIntegral", value.HumidityModifierHourIntegral);
            tree.SetDouble("maturationRateHourIntegral", value.MaturationRateHourIntegral);
            tree.SetDouble("evaporationRateHourIntegral", value.EvaporationRateHourIntegral);
            tree.SetInt("sampleCount", value.SampleCount);
            WriteExtensions(tree, value.Extensions);
        }

        private static ClimateAccumulators ReadClimate(ITreeAttribute tree)
        {
            if (tree == null) return new ClimateAccumulators();

            return new ClimateAccumulators
            {
                ObservedHours = tree.GetDouble("observedHours", 0.0),
                TemperatureHourIntegral = tree.GetDouble("temperatureHourIntegral", 0.0),
                RainfallHourIntegral = tree.GetDouble("rainfallHourIntegral", 0.0),
                HumidityModifierHourIntegral = tree.GetDouble("humidityModifierHourIntegral", 0.0),
                MaturationRateHourIntegral = tree.GetDouble("maturationRateHourIntegral", 0.0),
                EvaporationRateHourIntegral = tree.GetDouble("evaporationRateHourIntegral", 0.0),
                SampleCount = tree.GetInt("sampleCount", 0),
                Extensions = ReadExtensions(tree)
            };
        }

        private static void WriteVolume(ITreeAttribute tree, MaturationVolumeState value)
        {
            value ??= new MaturationVolumeState();
            tree.SetDouble("startingVolumeLitres", value.StartingVolumeLitres);
            tree.SetDouble("currentVolumeLitres", value.CurrentVolumeLitres);
            tree.SetDouble("angelsShareLostLitres", value.AngelsShareLostLitres);
            tree.SetDouble("whiskeyThiefSampledLitres", value.WhiskeyThiefSampledLitres);
            tree.SetDouble("otherLossLitres", value.OtherLossLitres);
            tree.SetDouble(
                "fractionalAngelsShareRemainderLitres",
                value.FractionalAngelsShareRemainderLitres
            );
            WriteExtensions(tree, value.Extensions);
        }

        private static MaturationVolumeState ReadVolume(ITreeAttribute tree)
        {
            if (tree == null) return new MaturationVolumeState();

            return new MaturationVolumeState
            {
                StartingVolumeLitres = tree.GetDouble("startingVolumeLitres", 0.0),
                CurrentVolumeLitres = tree.GetDouble("currentVolumeLitres", 0.0),
                AngelsShareLostLitres = tree.GetDouble("angelsShareLostLitres", 0.0),
                WhiskeyThiefSampledLitres = tree.GetDouble("whiskeyThiefSampledLitres", 0.0),
                OtherLossLitres = tree.GetDouble("otherLossLitres", 0.0),
                FractionalAngelsShareRemainderLitres = tree.GetDouble(
                    "fractionalAngelsShareRemainderLitres",
                    0.0
                ),
                Extensions = ReadExtensions(tree)
            };
        }

        private static void WriteWhiskeyThief(ITreeAttribute tree, WhiskeyThiefState value)
        {
            value ??= new WhiskeyThiefState();
            tree.SetInt("sampleCount", value.SampleCount);
            tree.SetDouble("exposure", value.Exposure);
            if (value.LastSampledAtCalendarHours.HasValue)
                tree.SetDouble("lastSampledAtCalendarHours", value.LastSampledAtCalendarHours.Value);
            WriteExtensions(tree, value.Extensions);
        }

        private static WhiskeyThiefState ReadWhiskeyThief(ITreeAttribute tree)
        {
            if (tree == null) return new WhiskeyThiefState();

            return new WhiskeyThiefState
            {
                SampleCount = tree.GetInt("sampleCount", 0),
                Exposure = tree.GetDouble("exposure", 0.0),
                LastSampledAtCalendarHours = tree.HasAttribute("lastSampledAtCalendarHours")
                    ? tree.GetDouble("lastSampledAtCalendarHours", 0.0)
                    : null,
                Extensions = ReadExtensions(tree)
            };
        }

        private static void WriteCask(ITreeAttribute tree, MaturationCaskProfile value)
        {
            value ??= new MaturationCaskProfile();
            tree.SetString("profileCode", value.ProfileCode ?? string.Empty);
            tree.SetString("caskId", value.CaskId ?? string.Empty);
            tree.SetInt("rollSeed", value.RollSeed);
            tree.SetDouble("maturationRateMultiplier", value.MaturationRateMultiplier);
            tree.SetDouble("intensityBonus", value.IntensityBonus);
            tree.SetDouble("smoothnessBonus", value.SmoothnessBonus);
            tree.SetDouble("qualityBonus", value.QualityBonus);
            tree.SetDouble("extractionMultiplier", value.ExtractionMultiplier);
            tree.SetDouble("safeWindowMultiplier", value.SafeWindowMultiplier);
            tree.SetDouble("overOakResistance", value.OverOakResistance);
            WriteExtensions(tree, value.Extensions);
        }

        private static MaturationCaskProfile ReadCask(ITreeAttribute tree)
        {
            if (tree == null) return new MaturationCaskProfile();

            return new MaturationCaskProfile
            {
                ProfileCode = tree.GetString("profileCode", "angels-share:standard"),
                CaskId = tree.GetString("caskId", string.Empty),
                RollSeed = tree.GetInt("rollSeed", 0),
                MaturationRateMultiplier = tree.GetDouble("maturationRateMultiplier", 1.0),
                IntensityBonus = tree.GetDouble("intensityBonus", 0.0),
                SmoothnessBonus = tree.GetDouble("smoothnessBonus", 0.0),
                QualityBonus = tree.GetDouble("qualityBonus", 0.0),
                ExtractionMultiplier = tree.GetDouble("extractionMultiplier", 1.0),
                SafeWindowMultiplier = tree.GetDouble("safeWindowMultiplier", 1.0),
                OverOakResistance = tree.GetDouble("overOakResistance", 1.0),
                Extensions = ReadExtensions(tree)
            };
        }

        private static void WriteOutcome(ITreeAttribute tree, MaturationOutcome value)
        {
            value ??= new MaturationOutcome();
            tree.SetDouble("quality", value.Quality);
            tree.SetDouble("intensity", value.Intensity);
            tree.SetDouble("smoothness", value.Smoothness);
            tree.SetDouble("balance", value.Balance);
            tree.SetDouble("extraction", value.Extraction);
            tree.SetDouble("oak", value.Oak);
            tree.SetString("tierCode", value.TierCode ?? "white");
            tree.SetString("maturationStageCode", value.MaturationStageCode ?? "Raw");
            tree.SetString(
                "climateStyleCode",
                value.ClimateStyleCode ?? "Standard Continental Maturation"
            );

            if (value.AlcoholByVolume.HasValue)
                tree.SetDouble("alcoholByVolume", value.AlcoholByVolume.Value);

            WriteDesignations(NewChild(tree, "designations"), value.Designations);
            WriteExtensions(tree, value.Extensions);
        }

        private static MaturationOutcome ReadOutcome(ITreeAttribute tree)
        {
            if (tree == null) return new MaturationOutcome();

            return new MaturationOutcome
            {
                Quality = tree.GetDouble("quality", 0.0),
                Intensity = tree.GetDouble("intensity", 0.0),
                Smoothness = tree.GetDouble("smoothness", 0.0),
                Balance = tree.GetDouble("balance", 0.0),
                Extraction = tree.GetDouble("extraction", 0.0),
                Oak = tree.GetDouble("oak", 0.0),
                TierCode = tree.GetString("tierCode", "white"),
                MaturationStageCode = tree.GetString("maturationStageCode", "Raw"),
                ClimateStyleCode = tree.GetString(
                    "climateStyleCode",
                    "Standard Continental Maturation"
                ),
                AlcoholByVolume = tree.HasAttribute("alcoholByVolume")
                    ? tree.GetDouble("alcoholByVolume", 0.0)
                    : null,
                Designations = ReadDesignations(tree.GetTreeAttribute("designations")),
                Extensions = ReadExtensions(tree)
            };
        }

        private static void WriteDesignations(
            ITreeAttribute tree,
            List<MaturationDesignation> designations
        )
        {
            designations ??= new List<MaturationDesignation>();
            tree.SetInt("count", designations.Count);

            for (int i = 0; i < designations.Count; i++)
            {
                MaturationDesignation value = designations[i] ?? new MaturationDesignation();
                ITreeAttribute item = NewChild(tree, i.ToString());
                item.SetString("code", value.Code ?? string.Empty);
                item.SetString("unitCode", value.UnitCode ?? string.Empty);
                if (value.NumericValue.HasValue)
                    item.SetDouble("numericValue", value.NumericValue.Value);
                WriteExtensions(item, value.Extensions);
            }
        }

        private static List<MaturationDesignation> ReadDesignations(ITreeAttribute tree)
        {
            List<MaturationDesignation> values = new List<MaturationDesignation>();
            if (tree == null) return values;

            int count = Math.Max(0, tree.GetInt("count", 0));

            for (int i = 0; i < count; i++)
            {
                ITreeAttribute item = tree.GetTreeAttribute(i.ToString());
                if (item == null) continue;

                values.Add(new MaturationDesignation
                {
                    Code = item.GetString("code", string.Empty),
                    UnitCode = item.GetString("unitCode", string.Empty),
                    NumericValue = item.HasAttribute("numericValue")
                        ? item.GetDouble("numericValue", 0.0)
                        : null,
                    Extensions = ReadExtensions(item)
                });
            }

            return values;
        }

        private static void WriteProvenance(ITreeAttribute tree, DistillationProvenance value)
        {
            value ??= new DistillationProvenance();
            tree.SetString("methodCode", value.MethodCode ?? string.Empty);
            tree.SetString("sourceLiquidCode", value.SourceLiquidCode ?? string.Empty);
            tree.SetString("distilledSpiritCode", value.DistilledSpiritCode ?? string.Empty);
            tree.SetString("recipeCode", value.RecipeCode ?? string.Empty);
            tree.SetString("batchId", value.BatchId ?? string.Empty);
            tree.SetInt("distillationPasses", value.DistillationPasses);
            if (value.DistilledAtCalendarHours.HasValue)
                tree.SetDouble("distilledAtCalendarHours", value.DistilledAtCalendarHours.Value);
            if (value.InitialAlcoholByVolume.HasValue)
                tree.SetDouble("initialAlcoholByVolume", value.InitialAlcoholByVolume.Value);
            WriteExtensions(tree, value.Extensions);
        }

        private static DistillationProvenance ReadProvenance(ITreeAttribute tree)
        {
            if (tree == null) return new DistillationProvenance();

            return new DistillationProvenance
            {
                MethodCode = tree.GetString("methodCode", string.Empty),
                SourceLiquidCode = tree.GetString("sourceLiquidCode", string.Empty),
                DistilledSpiritCode = tree.GetString("distilledSpiritCode", string.Empty),
                RecipeCode = tree.GetString("recipeCode", string.Empty),
                BatchId = tree.GetString("batchId", string.Empty),
                DistillationPasses = tree.GetInt("distillationPasses", 0),
                DistilledAtCalendarHours = tree.HasAttribute("distilledAtCalendarHours")
                    ? tree.GetDouble("distilledAtCalendarHours", 0.0)
                    : null,
                InitialAlcoholByVolume = tree.HasAttribute("initialAlcoholByVolume")
                    ? tree.GetDouble("initialAlcoholByVolume", 0.0)
                    : null,
                Extensions = ReadExtensions(tree)
            };
        }

        private static MaturationVolumeState CreateVolume(double starting, double current)
        {
            return new MaturationVolumeState
            {
                StartingVolumeLitres = starting,
                CurrentVolumeLitres = current
            };
        }

        public static ClimateAccumulators CloneClimate(ClimateAccumulators source)
        {
            source ??= new ClimateAccumulators();

            return new ClimateAccumulators
            {
                ObservedHours = source.ObservedHours,
                TemperatureHourIntegral = source.TemperatureHourIntegral,
                RainfallHourIntegral = source.RainfallHourIntegral,
                HumidityModifierHourIntegral = source.HumidityModifierHourIntegral,
                MaturationRateHourIntegral = source.MaturationRateHourIntegral,
                EvaporationRateHourIntegral = source.EvaporationRateHourIntegral,
                SampleCount = source.SampleCount,
                Extensions = CloneExtensions(source.Extensions)
            };
        }

        public static MaturationVolumeState CloneVolume(MaturationVolumeState source)
        {
            source ??= new MaturationVolumeState();

            return new MaturationVolumeState
            {
                StartingVolumeLitres = source.StartingVolumeLitres,
                CurrentVolumeLitres = source.CurrentVolumeLitres,
                AngelsShareLostLitres = source.AngelsShareLostLitres,
                WhiskeyThiefSampledLitres = source.WhiskeyThiefSampledLitres,
                OtherLossLitres = source.OtherLossLitres,
                FractionalAngelsShareRemainderLitres = source.FractionalAngelsShareRemainderLitres,
                Extensions = CloneExtensions(source.Extensions)
            };
        }

        public static WhiskeyThiefState CloneWhiskeyThief(WhiskeyThiefState source)
        {
            source ??= new WhiskeyThiefState();

            return new WhiskeyThiefState
            {
                SampleCount = source.SampleCount,
                Exposure = source.Exposure,
                LastSampledAtCalendarHours = source.LastSampledAtCalendarHours,
                Extensions = CloneExtensions(source.Extensions)
            };
        }

        public static MaturationCaskProfile CloneCask(MaturationCaskProfile source)
        {
            source ??= new MaturationCaskProfile();

            return new MaturationCaskProfile
            {
                ProfileCode = source.ProfileCode,
                CaskId = source.CaskId,
                RollSeed = source.RollSeed,
                MaturationRateMultiplier = source.MaturationRateMultiplier,
                IntensityBonus = source.IntensityBonus,
                SmoothnessBonus = source.SmoothnessBonus,
                QualityBonus = source.QualityBonus,
                ExtractionMultiplier = source.ExtractionMultiplier,
                SafeWindowMultiplier = source.SafeWindowMultiplier,
                OverOakResistance = source.OverOakResistance,
                Extensions = CloneExtensions(source.Extensions)
            };
        }

        public static MaturationOutcome CloneOutcome(MaturationOutcome source)
        {
            source ??= new MaturationOutcome();
            MaturationOutcome clone = new MaturationOutcome
            {
                Quality = source.Quality,
                Intensity = source.Intensity,
                Smoothness = source.Smoothness,
                Balance = source.Balance,
                Extraction = source.Extraction,
                Oak = source.Oak,
                TierCode = source.TierCode,
                MaturationStageCode = source.MaturationStageCode,
                ClimateStyleCode = source.ClimateStyleCode,
                AlcoholByVolume = source.AlcoholByVolume,
                Extensions = CloneExtensions(source.Extensions)
            };

            if (source.Designations != null)
            {
                foreach (MaturationDesignation designation in source.Designations)
                {
                    if (designation == null) continue;
                    clone.Designations.Add(new MaturationDesignation
                    {
                        Code = designation.Code,
                        NumericValue = designation.NumericValue,
                        UnitCode = designation.UnitCode,
                        Extensions = CloneExtensions(designation.Extensions)
                    });
                }
            }

            return clone;
        }

        private static string NamespacedCode(string value)
        {
            if (string.IsNullOrEmpty(value)) return "angels-share:standard";
            return value.IndexOf(':') >= 0 ? value : "angels-share:" + value;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static ITreeAttribute NewChild(ITreeAttribute parent, string key)
        {
            parent.RemoveAttribute(key);
            return parent.GetOrAddTreeAttribute(key);
        }

        private static void WriteExtensions(ITreeAttribute parent, TreeAttribute extensions)
        {
            parent.RemoveAttribute("extensions");
            parent["extensions"] = CloneExtensions(extensions);
        }

        private static TreeAttribute ReadExtensions(ITreeAttribute parent)
        {
            return CloneExtensions(parent?.GetTreeAttribute("extensions") as TreeAttribute);
        }

        private static TreeAttribute CloneExtensions(TreeAttribute extensions)
        {
            return extensions?.Clone() as TreeAttribute ?? new TreeAttribute();
        }
    }
}
