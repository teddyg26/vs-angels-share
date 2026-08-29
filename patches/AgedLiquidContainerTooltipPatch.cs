using HarmonyLib;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace AngelsShare
{
    [HarmonyPatch(typeof(CollectibleObject), "GetHeldItemInfo")]
    public static class AgedLiquidContainerTooltipPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            if (inSlot?.Itemstack == null) return;

            ItemStack containerStack = inSlot.Itemstack;

            if (containerStack.Collectible is not BlockLiquidContainerBase container)
                return;

            ItemStack liquidStack = container.GetContent(containerStack);

            if (liquidStack == null) return;

            if (
                !AgingDisplayUtil.TryGetMaturationRecord(
                    liquidStack,
                    out MaturationRecord record
                ) ||
                !AgingDisplayUtil.HasFinalizedAgingData(record)
            )
                return;

            if (dsc.ToString().Contains("[Angel's Share")) return;

            dsc.AppendLine();
            dsc.AppendLine(AgingDisplayUtil.GetAgedSpiritDisplayName(liquidStack, record));

            bool shiftDown = IsShiftDown(inSlot);

            if (shiftDown)
            {
                AgingDisplayUtil.AppendFinalizedDetailed(dsc, record);
            }
            else
            {
                AgingDisplayUtil.AppendFinalizedShort(dsc, record);
            }

            if (withDebugInfo)
                AgingDisplayUtil.AppendDebug(dsc, record);
        }

        private static bool IsShiftDown(ItemSlot slot)
        {
            ICoreClientAPI capi = slot?.Inventory?.Api as ICoreClientAPI;

            if (capi == null) return false;

            return capi.Input.KeyboardKeyState[(int)GlKeys.ShiftLeft]
                || capi.Input.KeyboardKeyState[(int)GlKeys.ShiftRight];
        }
    }
}
