using System.Collections.Generic;

using Vintagestory.API.Datastructures;

namespace AngelsShare
{
    public static class MaturationSchema
    {
        public const string RootAttributeName = "maturationData";
        public const int LegacyUnversionedVersion = 0;
        public const int CurrentVersion = 1;
    }

    public enum MaturationRecordState
    {
        Active = 1,
        Finalized = 2
    }

    public sealed class MaturationRecord
    {
        public int SchemaVersion { get; set; } = MaturationSchema.CurrentVersion;
        public MaturationRecordState State { get; set; }
        public DistillationProvenance Provenance { get; set; } = new DistillationProvenance();
        public ActiveMaturationSession ActiveSession { get; set; }
        public FinalizedMaturationProduct FinalizedProduct { get; set; }
        public List<CompletedMaturationSession> CompletedSessions { get; set; }
            = new List<CompletedMaturationSession>();
        public TreeAttribute Extensions { get; set; } = new TreeAttribute();
    }

    public sealed class ActiveMaturationSession
    {
        public int Sequence { get; set; }
        public string InputLiquidCode { get; set; }
        public double SealedAtCalendarHours { get; set; }
        public double LastIntegratedAtCalendarHours { get; set; }
        public double ActualElapsedHours { get; set; }
        public double EffectiveMaturationHours { get; set; }
        public ClimateAccumulators Climate { get; set; } = new ClimateAccumulators();
        public MaturationCaskProfile Cask { get; set; } = new MaturationCaskProfile();
        public MaturationVolumeState Volume { get; set; } = new MaturationVolumeState();
        public WhiskeyThiefState WhiskeyThief { get; set; } = new WhiskeyThiefState();
        public MaturationOutcome ProjectedOutcome { get; set; } = new MaturationOutcome();
        public TreeAttribute Extensions { get; set; } = new TreeAttribute();
    }

    public sealed class CompletedMaturationSession
    {
        public int Sequence { get; set; }
        public string InputLiquidCode { get; set; }
        public string OutputLiquidCode { get; set; }
        public double SealedAtCalendarHours { get; set; }
        public double UnsealedAtCalendarHours { get; set; }
        public double ActualElapsedHours { get; set; }
        public double EffectiveMaturationHours { get; set; }
        public ClimateAccumulators Climate { get; set; } = new ClimateAccumulators();
        public MaturationCaskProfile Cask { get; set; } = new MaturationCaskProfile();
        public MaturationVolumeState Volume { get; set; } = new MaturationVolumeState();
        public WhiskeyThiefState WhiskeyThief { get; set; } = new WhiskeyThiefState();
        public MaturationOutcome Outcome { get; set; } = new MaturationOutcome();
        public TreeAttribute Extensions { get; set; } = new TreeAttribute();
    }

    public sealed class FinalizedMaturationProduct
    {
        public string ProductLiquidCode { get; set; }
        public double FinalizedAtCalendarHours { get; set; }
        public int CompletedSessionCount { get; set; }
        public double TotalActualElapsedHours { get; set; }
        public double TotalEffectiveMaturationHours { get; set; }
        public ClimateAccumulators Climate { get; set; } = new ClimateAccumulators();
        public MaturationVolumeState Volume { get; set; } = new MaturationVolumeState();
        public WhiskeyThiefState WhiskeyThief { get; set; } = new WhiskeyThiefState();
        public MaturationOutcome Outcome { get; set; } = new MaturationOutcome();
        public TreeAttribute Extensions { get; set; } = new TreeAttribute();
    }

    public sealed class ClimateAccumulators
    {
        public double ObservedHours { get; set; }
        public double TemperatureHourIntegral { get; set; }
        public double RainfallHourIntegral { get; set; }
        public double HumidityModifierHourIntegral { get; set; }
        public double MaturationRateHourIntegral { get; set; }
        public double EvaporationRateHourIntegral { get; set; }
        public int SampleCount { get; set; }
        public TreeAttribute Extensions { get; set; } = new TreeAttribute();
    }

    public sealed class MaturationVolumeState
    {
        public double StartingVolumeLitres { get; set; }
        public double CurrentVolumeLitres { get; set; }
        public double AngelsShareLostLitres { get; set; }
        public double WhiskeyThiefSampledLitres { get; set; }
        public double OtherLossLitres { get; set; }
        public double FractionalAngelsShareRemainderLitres { get; set; }
        public TreeAttribute Extensions { get; set; } = new TreeAttribute();
    }

    public sealed class WhiskeyThiefState
    {
        public int SampleCount { get; set; }
        public double Exposure { get; set; }
        public double? LastSampledAtCalendarHours { get; set; }
        public TreeAttribute Extensions { get; set; } = new TreeAttribute();
    }

    public sealed class MaturationCaskProfile
    {
        public string ProfileCode { get; set; }
        public string CaskId { get; set; }
        public int RollSeed { get; set; }
        public double MaturationRateMultiplier { get; set; }
        public double IntensityBonus { get; set; }
        public double SmoothnessBonus { get; set; }
        public double QualityBonus { get; set; }
        public double ExtractionMultiplier { get; set; }
        public double SafeWindowMultiplier { get; set; }
        public double OverOakResistance { get; set; }
        public TreeAttribute Extensions { get; set; } = new TreeAttribute();
    }

    public sealed class MaturationOutcome
    {
        public double Quality { get; set; }
        public double Intensity { get; set; }
        public double Smoothness { get; set; }
        public double Balance { get; set; }
        public double Extraction { get; set; }
        public double Oak { get; set; }
        public string TierCode { get; set; }
        public string MaturationStageCode { get; set; }
        public string ClimateStyleCode { get; set; }
        public List<MaturationDesignation> Designations { get; set; }
            = new List<MaturationDesignation>();
        public double? AlcoholByVolume { get; set; }
        public TreeAttribute Extensions { get; set; } = new TreeAttribute();
    }

    public sealed class MaturationDesignation
    {
        public string Code { get; set; }
        public double? NumericValue { get; set; }
        public string UnitCode { get; set; }
        public TreeAttribute Extensions { get; set; } = new TreeAttribute();
    }

    public sealed class DistillationProvenance
    {
        public string MethodCode { get; set; }
        public string SourceLiquidCode { get; set; }
        public string DistilledSpiritCode { get; set; }
        public string RecipeCode { get; set; }
        public string BatchId { get; set; }
        public int DistillationPasses { get; set; }
        public double? DistilledAtCalendarHours { get; set; }
        public double? InitialAlcoholByVolume { get; set; }
        public TreeAttribute Extensions { get; set; } = new TreeAttribute();
    }
}
