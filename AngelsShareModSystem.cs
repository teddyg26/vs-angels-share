using HarmonyLib;
using System.Reflection;
using System.Linq;

using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace AngelsShare
{
    public class AngelsShareModSystem : ModSystem
    {
        private Harmony harmonyInstance;
        private BarrelQuickUnsealGesture quickUnsealGesture;
        public const string HarmonyId = "com.teddyg.angelsshare.agingpatch";

        public override bool ShouldLoad(EnumAppSide side) => true;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            api.RegisterItemClass("ItemLiquidPortionOverride", typeof(ItemLiquidPortionOverride));

            harmonyInstance = new Harmony(HarmonyId);
            harmonyInstance.PatchAll(typeof(AngelsShareModSystem).Assembly);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            quickUnsealGesture = new BarrelQuickUnsealGesture(api);
        }

        public override void Dispose()
        {
            quickUnsealGesture?.Dispose();
            quickUnsealGesture = null;

            // Clean up the footprint cleanly if server context terminates
            harmonyInstance?.UnpatchAll(HarmonyId);
            base.Dispose();
        }
    }
}
