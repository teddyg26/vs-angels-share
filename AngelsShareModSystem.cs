using Vintagestory.API.Common;
using Vintagestory.API.Server;
using HarmonyLib;

namespace AngelsShare
{
    public class AngelsShareModSystem : ModSystem
    {
        private Harmony harmonyInstance;
        public const string HarmonyId = "com.teddyg.angelsshare.agingpatch";

        public override bool ShouldLoad(EnumAppSide side) => true;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            api.RegisterItemClass("ItemLiquidPortionOverride", typeof(ItemLiquidPortionOverride));

            harmonyInstance = new Harmony(HarmonyId);
            harmonyInstance.PatchAll(typeof(AngelsShareModSystem).Assembly);
        }

        public override void Dispose()
        {
            // Clean up the footprint cleanly if server context terminates
            harmonyInstance?.UnpatchAll(HarmonyId);
            base.Dispose();
        }
    }
}
