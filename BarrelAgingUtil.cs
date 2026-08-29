using System;
using Vintagestory.API.Common;

namespace AngelsShare
{
    public static class BarrelAgingUtil
    {
        public const int LiquidSlotId = 1;

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

        public static bool ConvertAgingSpiritOnUnseal(ICoreAPI api, ItemSlot liquidSlot)
        {
            if (api == null || liquidSlot?.Itemstack == null)
            {
                return false;
            }

            ItemStack oldStack = liquidSlot.Itemstack;

            if (!TryGetAgedOutputCode(oldStack, out AssetLocation outputCode))
            {
                return false;
            }

            Item newItem = api.World.GetItem(outputCode);

            if (newItem == null)
            {
                api.Logger.Warning("[Angel's Share] Could not find aged output item: " + outputCode);
                return false;
            }

            ItemStack newStack = new ItemStack(newItem, oldStack.StackSize);

            if (oldStack.Attributes != null)
            {
                newStack.Attributes = oldStack.Attributes.Clone();
            }

            if (!MaturationRecordCodec.TryRead(newStack, out MaturationRecord record))
            {
                api.Logger.Warning(
                    "[Angel's Share] Converted {0}, but its maturation record could not be read.",
                    oldStack.Collectible.Code
                );
                return false;
            }

            if (record.FinalizedProduct == null)
            {
                api.Logger.Warning(
                    "[Angel's Share] Refusing to convert {0}: maturation was not finalized.",
                    oldStack.Collectible.Code
                );
                return false;
            }

            record.FinalizedProduct.ProductLiquidCode = outputCode.ToString();

            if (record.CompletedSessions.Count > 0)
            {
                record.CompletedSessions[record.CompletedSessions.Count - 1].OutputLiquidCode =
                    outputCode.ToString();
            }

            MaturationRecordCodec.Write(newStack, record);

            MaturationOutcome outcome = record.FinalizedProduct.Outcome;
            double effectiveDays = record.FinalizedProduct.TotalEffectiveMaturationHours / 24.0;

            api.Logger.Notification(
                "[Angel's Share] Converted {0} -> {1}, effectiveDays={2:F2}, quality={3:F2}, intensity={4:F1}, smoothness={5:F1}, tier={6}, designations={7}, newClass={8}",
                oldStack.Collectible.Code,
                outputCode,
                effectiveDays,
                outcome?.Quality ?? 0.0,
                outcome?.Intensity ?? 0.0,
                outcome?.Smoothness ?? 0.0,
                outcome?.TierCode ?? "unknown",
                AgingDisplayUtil.GetDesignationSummary(outcome),
                newStack.Collectible.GetType().FullName
            );

            liquidSlot.Itemstack = newStack;
            liquidSlot.MarkDirty();

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

            string path = stack.Collectible.Code.Path;

            bool isGin =
                stack.Collectible.Code.Domain == "angels-share" &&
                path.StartsWith("ginportion-");

            bool hasGrace =
                quality >= 85.0 &&
                smoothness >= 68.0 &&
                maturityRatio <= 1.08;

            if (maturityRatio > 1.0 && !hasGrace)
            {
                return "over-oaked";
            }

            double reserveThreshold = isGin ? 0.80 : 0.85;

            bool generalReserve =
                maturityRatio >= reserveThreshold &&
                quality >= 78.0 &&
                Math.Max(intensity, smoothness) >= 75.0;

            bool coldReserve =
                maturityRatio >= 0.90 &&
                quality >= 90.0 &&
                smoothness >= 70.0;

            bool hotReserve =
                maturityRatio >= 0.78 &&
                quality >= 82.0 &&
                intensity >= 82.0;

            if (generalReserve || coldReserve || hotReserve)
            {
                return "reserve";
            }

            if (maturityRatio >= 0.55)
            {
                return "aged";
            }

            if (maturityRatio >= 0.25)
            {
                return "young";
            }

            if (maturityRatio >= 0.05)
            {
                return "rested";
            }

            return "white";
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
