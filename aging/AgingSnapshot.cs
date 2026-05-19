using System;
using System.Collections.Generic;
using System.Text;

namespace AngelsShare
{
    public class AgingSnapshot
    {
        public double TotalHours { get; set; }
        public double AgeHours { get; set; }
        public double AgeDays { get; set; }

        public double SafeWindowDays { get; set; }
        public double MaturityRatio { get; set; }
        public double OverAgeRatio { get; set; }

        public double Quality { get; set; }
        public double Intensity { get; set; }
        public double Smoothness { get; set; }
        public double Balance { get; set; }

        public double AverageTemperature { get; set; }
        public double AverageRainfall { get; set; }
        public double AverageHumidityModifier { get; set; }

        public string ClimateStyle { get; set; }
        public string MaturationDescriptor { get; set; }
        public string CaskTrait { get; set; }
        public string Tier { get; set; }
        public string SpecialStyle { get; set; }

        public double Proof { get; set; }
        public double AgeStatementYears { get; set; }
    }
}
