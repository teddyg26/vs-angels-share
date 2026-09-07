using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AngelsShare
{
    public sealed class BarrelQuickUnsealGesture : IDisposable
    {
        public const double MaximumHoldSeconds = 0.2;
        private const string CarryOnPickupHotkeyCode = "carryonpickupkey";

        private readonly ICoreClientAPI api;
        private readonly long tickListenerId;
        private BlockPos pendingBarrelPos;
        private long pressedAtMilliseconds;

        public BarrelQuickUnsealGesture(ICoreClientAPI api)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            api.Input.InWorldAction += OnInWorldAction;
            tickListenerId = api.Event.RegisterGameTickListener(
                OnGameTick,
                OnGameTickError,
                0
            );
            api.Logger.Debug("[Angel's Share] Registered quick-unseal input handler.");
        }

        public void Dispose()
        {
            api.Input.InWorldAction -= OnInWorldAction;
            api.Event.UnregisterGameTickListener(tickListenerId);
            ClearPendingGesture();
        }

        public static bool IsQuickRelease(long elapsedMilliseconds)
        {
            return elapsedMilliseconds >= 0L
                && elapsedMilliseconds <= MaximumHoldSeconds * 1000.0;
        }

        private void OnInWorldAction(
            EnumEntityAction action,
            bool on,
            ref EnumHandling handled
        )
        {
            if (action == EnumEntityAction.InWorldRightMouseDown && on)
                BeginGestureIfEligible(handled);
        }

        private void BeginGestureIfEligible(EnumHandling handled)
        {
            ClearPendingGesture();

            IClientPlayer player = api.World?.Player;
            if (!IsUnsealModifierPressed(player) && handled != EnumHandling.PreventDefault)
            {
                return;
            }

            BlockSelection selection = player.CurrentBlockSelection;
            if (selection?.Position == null) return;

            BlockEntityBarrel barrel = api.World.BlockAccessor.GetBlockEntity(
                selection.Position
            ) as BlockEntityBarrel;
            if (barrel == null || !barrel.Sealed) return;

            ItemStack liquidStack =
                barrel.Inventory[BarrelAgingUtil.LiquidSlotId]?.Itemstack;
            if (!BarrelAgingUtil.IsAgeableSpirit(liquidStack)) return;

            pendingBarrelPos = selection.Position.Copy();
            pressedAtMilliseconds = Environment.TickCount64;
            api.Logger.Debug(
                "[Angel's Share] Armed quick unseal for barrel at {0}.",
                pendingBarrelPos
            );
        }

        private void OnGameTick(float deltaTime)
        {
            if (pendingBarrelPos == null) return;

            long elapsedMilliseconds = GetElapsedGestureMilliseconds();

            if (!api.Input.InWorldMouseButton.Right)
            {
                CompleteGestureOnRelease(elapsedMilliseconds);
                return;
            }

            if (!IsQuickRelease(elapsedMilliseconds))
            {
                api.Logger.Debug(
                    "[Angel's Share] Barrel interaction at {0} became a hold after {1} ms.",
                    pendingBarrelPos,
                    elapsedMilliseconds
                );
                ClearPendingGesture();
            }
        }

        private void OnGameTickError(Exception exception)
        {
            api.Logger.Error(
                "[Angel's Share] Quick-unseal input polling failed: {0}",
                exception
            );
            ClearPendingGesture();
        }

        private void CompleteGestureOnRelease(long elapsedMilliseconds)
        {
            if (pendingBarrelPos == null) return;

            BlockPos targetPos = pendingBarrelPos;
            ClearPendingGesture();

            bool isQuickRelease = IsQuickRelease(elapsedMilliseconds);
            api.Logger.Debug(
                "[Angel's Share] Released barrel interaction at {0} after {1} ms; quickRelease={2}.",
                targetPos,
                elapsedMilliseconds,
                isQuickRelease
            );
            if (!isQuickRelease) return;

            BlockEntityBarrel barrel = api.World.BlockAccessor.GetBlockEntity(
                targetPos
            ) as BlockEntityBarrel;
            if (barrel == null || !barrel.Sealed) return;

            ItemStack liquidStack =
                barrel.Inventory[BarrelAgingUtil.LiquidSlotId]?.Itemstack;
            if (!BarrelAgingUtil.IsAgeableSpirit(liquidStack)) return;

            // Do not change `handled`: a long press must remain available to CarryOn
            // and any other mod using the same input gesture.
            api.Network.SendBlockEntityPacket(
                targetPos,
                BarrelManualUnsealPatch.UnsealPacketId,
                null
            );
        }

        private long GetElapsedGestureMilliseconds()
        {
            return Math.Max(0L, Environment.TickCount64 - pressedAtMilliseconds);
        }

        private bool IsUnsealModifierPressed(IClientPlayer player)
        {
            if (player?.Entity?.Controls?.Sneak == true
                || player?.WorldData?.EntityControls?.ShiftKey == true)
            {
                return true;
            }

            try
            {
                if (api.Input.IsHotKeyPressed(CarryOnPickupHotkeyCode)) return true;
            }
            catch (Exception)
            {
                // The hotkey is absent when CarryOn is not installed.
            }

            bool[] keyState = api.Input.KeyboardKeyState;
            return IsKeyPressed(keyState, GlKeys.LShift)
                || IsKeyPressed(keyState, GlKeys.RShift);
        }

        private static bool IsKeyPressed(bool[] keyState, GlKeys key)
        {
            int keyCode = (int)key;
            return keyState != null
                && keyCode >= 0
                && keyCode < keyState.Length
                && keyState[keyCode];
        }

        private void ClearPendingGesture()
        {
            pendingBarrelPos = null;
            pressedAtMilliseconds = 0L;
        }
    }
}
