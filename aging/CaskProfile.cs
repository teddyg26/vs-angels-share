using System;
using System.Collections.Generic;
using System.Text;

namespace AngelsShare
{
    public sealed class CaskProfile
    {
        public int RollSeed { get; set; }
        public string Trait { get; set; }
        public double CaskVariance { get; set; }
        public double IntensityBonus { get; set; }
        public double SmoothnessBonus { get; set; }
        public double QualityBonus { get; set; }
        public double SafeWindowMultiplier { get; set; }
        public double OverOakResistance { get; set; }
    }
}
