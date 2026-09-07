using System;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace AngelsShare
{
    public static class BarrelAgingUtil
    {
        public const int LiquidSlotId = 1;
        public const double MinimumAgingVolumeLitres = 2.0;

        public static bool IsAgeableSpirit(ItemStack stack)
        {
            return TryGetAgedOutputCode(stack, out _);
        }

        public static bool TryGetAgedOutputCode(ItemStack oldStack, out AssetLocation outputCode)
        {
            outputCode = null;

            if (oldStack?.Collectible?.Code == null)
            {
                return false;
            }

            AssetLocation oldCode = oldStack.Collectible.Code;
            string domain = oldCode.Domain;
            string path = oldCode.Path;

            if (domain == "angels-share" && path.StartsWith("whitespiritportion-"))
            {
                string variant = path.Substring("whitespiritportion-".Length);
                outputCode = new AssetLocation("angels-share", "spiritportion-" + variant);
                return true;
            }

            if (domain == "angels-share" && path.StartsWith("ginportion-"))
            {
                outputCode = oldCode.Clone();
                return true;
            }

            return false;
        }

        public static bool TryResolveAgedOutput(
            ICoreAPI api,
            ItemStack inputStack,
            out AssetLocation outputCode,
            out Item outputItem,
            out int outputStackSize,
            out string failureReason
        )
        {
            outputCode = null;
            outputItem = null;
            outputStackSize = 0;
            failureReason = null;

            if (api?.World == null || inputStack?.Collectible == null)
            {
                failureReason = "The barrel contents could not be inspected.";
                return false;
            }

            if (!TryGetAgedOutputCode(inputStack, out outputCode))
            {
                failureReason = "This liquid has no supported aged output.";
                return false;
            }

            outputItem = api.World.GetItem(outputCode);
            if (outputItem == null)
            {
                failureReason = "The aged output item " + outputCode + " is unavailable.";
                return false;
            }

            WaterTightContainableProps inputProps =
                BlockLiquidContainerBase.GetContainableProps(inputStack);
            if (inputProps == null || inputProps.ItemsPerLitre <= 0.0f)
            {
                failureReason = "The barrel contents do not define a usable liquid volume.";
                return false;
            }

            double inputLitres = inputStack.StackSize / (double)inputProps.ItemsPerLitre;
            if (inputLitres + 0.000001 < MinimumAgingVolumeLitres)
            {
                failureReason = string.Format(
                    "The barrel contains only {0:0.##} litres; at least {1:0.##} litres are required.",
                    inputLitres,
                    MinimumAgingVolumeLitres
                );
                return false;
            }

            ItemStack outputProbe = new ItemStack(outputItem, 1);
            WaterTightContainableProps outputProps =
                BlockLiquidContainerBase.GetContainableProps(outputProbe);
            if (outputProps == null || outputProps.ItemsPerLitre <= 0.0f)
            {
                failureReason = "The aged output item does not define a usable liquid volume.";
                return false;
            }

            double exactOutputSize = inputLitres * outputProps.ItemsPerLitre;
            outputStackSize = (int)Math.Round(exactOutputSize);
            if (
                outputStackSize <= 0 ||
                Math.Abs(outputStackSize - exactOutputSize) > 0.000001
            )
            {
                failureReason = "The aged output cannot represent the barrel's liquid volume exactly.";
                return false;
            }

            return true;
        }

        public static string GetAgeTier(ItemStack stack, double ageDays, double quality)
        {
            double approximateSafeWindow = 60.0;
            double maturityRatio = approximateSafeWindow > 0.0 ? ageDays / approximateSafeWindow : 0.0;

            return GetAgeTierFromMaturity(stack, maturityRatio, quality, 0.0, 0.0);
        }

        public static string GetAgeTier(ItemStack stack, double ageDays, double quality, double safeWindowDays)
        {
            double maturityRatio = safeWindowDays > 0.0 ? ageDays / safeWindowDays : 0.0;

            return GetAgeTierFromMaturity(stack, maturityRatio, quality, 0.0, 0.0);
        }

        public static string GetAgeTierFromMaturity(ItemStack stack, double maturityRatio, double quality, double intensity, double smoothness)
        {
            if (stack?.Collectible?.Code == null)
            {
                return "unknown";
            }

            bool isGin =
                stack.Collectible.Code.Domain == "angels-share" &&
                stack.Collectible.Code.Path.StartsWith("ginportion-");

            return MaturationMath.GetAgeTierFromMaturity(
                isGin,
                maturityRatio,
                quality,
                intensity,
                smoothness
            );
        }

        public static string GetLangKeyForAgeTier(string tier)
        {
            switch (tier)
            {
                case "reserve":
                    return "angels-share:age-reserve";

                case "over-oaked":
                    return "angels-share:age-overoaked";

                case "aged":
                    return "angels-share:age-aged";

                case "young":
                    return "angels-share:age-young";

                case "rested":
                    return "angels-share:age-rested";

                case "white":
                default:
                    return "angels-share:age-white";
            }
        }
    }
}
