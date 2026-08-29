using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace AngelsShare
{
    public class ItemLiquidPortionOverride : Item
    {
        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            ItemStack stack = inSlot.Itemstack;

            if (
                !AgingDisplayUtil.TryGetMaturationRecord(stack, out MaturationRecord record) ||
                !AgingDisplayUtil.HasFinalizedAgingData(record)
            )
            {
                return;
            }

            AgingDisplayUtil.AppendFinalizedShort(dsc, record);

            if (withDebugInfo)
            {
                AgingDisplayUtil.AppendDebug(dsc, record);
            }
        }
    }
}
