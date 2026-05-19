using HarmonyLib;
using System.Reflection;
using System.Linq;

using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

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


            // Debugging barrel methods
            foreach (MethodInfo method in typeof(BlockBarrel).GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic
            ))
            {
                if (
                    method.Name.Contains("Info") ||
                    method.Name.Contains("Held") ||
                    method.Name.Contains("Interaction") ||
                    method.Name.Contains("Selection") ||
                    method.Name.Contains("Get")
                )
                {
                    api.Logger.Notification(
                        "[Angel's Share DEBUG] BlockBarrel method: {0}({1})",
                        method.Name,
                        string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))
                    );
                }
            }
        }

        public override void Dispose()
        {
            // Clean up the footprint cleanly if server context terminates
            harmonyInstance?.UnpatchAll(HarmonyId);
            base.Dispose();
        }
    }
}
