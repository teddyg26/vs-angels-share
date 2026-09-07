using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace AngelsShare
{
    [HarmonyPatch(typeof(BlockEntityBarrel), "OnReceivedClientPacket")]
    public static class BarrelManualUnsealPatch
    {
        public const int UnsealPacketId = 19681;

        [HarmonyPrefix]
        public static bool Prefix(
            BlockEntityBarrel __instance,
            IPlayer player,
            int packetid
        )
        {
            if (packetid != UnsealPacketId) return true;

            __instance?.Api?.Logger.Debug(
                "[Angel's Share] Received quick-unseal request at {0} from {1}.",
                __instance.Pos,
                player?.PlayerName
            );
            TryUnseal(__instance, player);
            return false;
        }

        private static void TryUnseal(BlockEntityBarrel barrel, IPlayer byPlayer)
        {
            if (
                barrel?.Api == null ||
                barrel.Api.Side != EnumAppSide.Server ||
                byPlayer == null ||
                !barrel.Sealed
            )
            {
                return;
            }

            ItemSlot liquidSlot = barrel.Inventory[BarrelAgingUtil.LiquidSlotId];
            ItemStack liquidStack = liquidSlot?.Itemstack;
            if (!BarrelAgingUtil.IsAgeableSpirit(liquidStack)) return;

            if (!barrel.Api.World.Claims.TryAccess(
                byPlayer,
                barrel.Pos,
                EnumBlockAccessFlags.Use
            ))
            {
                return;
            }

            bool finalized;
            string failureReason;

            try
            {
                finalized = BarrelAgingCalculator.TryFinalizeAgingOnUnseal(
                    barrel,
                    liquidSlot,
                    out failureReason
                );
            }
            catch (System.Exception exception)
            {
                barrel.Api.Logger.Error(
                    "[Angel's Share] Unexpected error while unsealing {0} at {1}. The barrel remains sealed. {2}",
                    liquidStack.Collectible?.Code,
                    barrel.Pos,
                    exception
                );
                SendFailureMessage(
                    byPlayer,
                    "An unexpected error prevented maturation from finishing."
                );
                return;
            }

            if (!finalized)
            {
                ReportFailure(barrel, byPlayer, liquidStack, failureReason);
                return;
            }

            try
            {
                barrel.Api.World.PlaySoundAt(
                    new AssetLocation("sounds/block/barrelopen"),
                    barrel.Pos.X,
                    barrel.Pos.Y,
                    barrel.Pos.Z,
                    byPlayer
                );
            }
            catch (System.Exception exception)
            {
                barrel.Api.Logger.Warning(
                    "[Angel's Share] Unsealed {0} at {1}, but could not play its sound: {2}",
                    liquidStack.Collectible?.Code,
                    barrel.Pos,
                    exception
                );
            }
        }

        private static void ReportFailure(
            BlockEntityBarrel barrel,
            IPlayer byPlayer,
            ItemStack liquidStack,
            string failureReason
        )
        {
            string reason = string.IsNullOrWhiteSpace(failureReason)
                ? "Maturation could not be finalized."
                : failureReason;

            barrel.Api.Logger.Warning(
                "[Angel's Share] Could not unseal {0} at {1}: {2} The barrel remains sealed with its active session intact.",
                liquidStack.Collectible?.Code,
                barrel.Pos,
                reason
            );
            SendFailureMessage(byPlayer, reason);
        }

        private static void SendFailureMessage(IPlayer byPlayer, string reason)
        {
            if (byPlayer is IServerPlayer serverPlayer)
            {
                serverPlayer.SendMessage(
                    GlobalConstants.InfoLogChatGroup,
                    "[Angel's Share] " + reason + " The barrel remains sealed.",
                    EnumChatType.Notification
                );
            }
        }
    }
}
