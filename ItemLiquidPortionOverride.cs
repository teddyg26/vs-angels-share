using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace AngelsShare
{
    public class ItemLiquidPortionOverride : Item
    {
        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            ItemStack stack = inSlot.Itemstack;
            ITreeAttribute tree = AgingDisplayUtil.GetMaturationTree(stack);

            if (!AgingDisplayUtil.HasFinalizedAgingData(tree))
            {
                return;
            }

            AgingDisplayUtil.AppendFinalizedShort(dsc, tree);

            if (withDebugInfo)
            {
                AgingDisplayUtil.AppendDebug(dsc, tree);
            }
        }
    }
}