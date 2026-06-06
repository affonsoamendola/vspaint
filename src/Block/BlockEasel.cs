using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VSpaint
{
    public class BlockEasel : Block
    {
        const double easel_bot_start_y = 0.414f;

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            BlockEntityEasel BEEaselRef = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityEasel;
            if (BEEaselRef == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);

            if (world.Side == EnumAppSide.Server)
            {
                ItemSlot heldSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
                bool shiftHeld = byPlayer.Entity.Controls.ShiftKey;
                bool heldCanvas = IsCanvasItem(heldSlot.Itemstack);
                bool heldBrush = heldSlot.Itemstack?.Collectible is ItemPaintbrush;
                
                world.Logger.Event("InteracStartServer");

                if (!BEEaselRef.HasCanvas && heldCanvas)
                {
                    if (world.Side == EnumAppSide.Server)
                    {
                        BEEaselRef.MountCanvas();
                        heldSlot.TakeOut(1);
                        heldSlot.MarkDirty();
                    }
                    return true;
                }

                // Shift-click to take a finished canvas off the easel.
                if (BEEaselRef.HasCanvas && shiftHeld)
                {
                    if (BEEaselRef.IsFinished && world.Side == EnumAppSide.Server)
                        BEEaselRef.TakeCanvas(byPlayer);
                    return true;
                }

                /*// Open painting GUI; requires a wet brush.
                if (be.HasCanvas && !be.IsFinished && heldBrush)
                {
                    if (world.Side == EnumAppSide.Client)
                    {
                        ItemPaintbrush.CheckDrying(heldSlot, world);

                        if (!ItemPaintbrush.IsWet(heldSlot.Itemstack))
                        {
                            string hint = ItemPaintbrush.IsDry(heldSlot.Itemstack)
                                ? Lang.Get("vspaint:brush-hint-dry")
                                : Lang.Get("vspaint:brush-hint-clean");
                            (world.Api as ICoreClientAPI)?.TriggerIngameError(this, "notwet", hint);
                        }
                        else
                        {
                            be.OpenPaintGui(byPlayer, GetHotbarWetColors(byPlayer, world));
                        }
                    }
                    return true;
                }
                */
                return true;
            }
            else
            {
                Vec3d converted_hit_pos = Vec3d.Zero;
                world.Logger.Event("InteracStartClient");
                if (blockSel.Block == null)
                {
                    converted_hit_pos = blockSel.HitPosition.Add(0.0f, 1.0f - easel_bot_start_y, 0.0f);
                }
                else
                {
                    converted_hit_pos = blockSel.HitPosition.Add(0.0f, -easel_bot_start_y, 0.0f);
                }
                world.Logger.Event("Drawing at Unconverted= {0},{1},{2}", converted_hit_pos.X, converted_hit_pos.Y, converted_hit_pos.Z);

                converted_hit_pos.X = (converted_hit_pos.X - 0.03f) / (0.97f - 0.03f);
                converted_hit_pos.Z = (converted_hit_pos.Z - 0.03f) / (0.97f - 0.03f);
                world.Logger.Event("Drawing at Normalized = {0},{1},{2}", converted_hit_pos.X, converted_hit_pos.Y, converted_hit_pos.Z);

                if (converted_hit_pos.X < 0.0f) converted_hit_pos.X = 0.0f;
                if (converted_hit_pos.Y < 0.0f) converted_hit_pos.Y = 0.0f;
                if (converted_hit_pos.Z < 0.0f) converted_hit_pos.Z = 0.0f;
                if (converted_hit_pos.X > 1.0f) converted_hit_pos.X = 1.0f;
                if (converted_hit_pos.Y > 1.0f) converted_hit_pos.Y = 1.0f;
                if (converted_hit_pos.Z > 1.0f) converted_hit_pos.Z = 1.0f;
                
                var canvas_space = BEEaselRef.ToCanvasSpace(converted_hit_pos);

                
                BEEaselRef.DrawBrushStamp(canvas_space.X, canvas_space.Y);
                BEEaselRef.MarkDirty();
                BEEaselRef.SaveToServer(byPlayer);
                return true;
            }
            return true;
        }


        // Side-effect: also runs CheckDrying on each hotbar brush, so opening the
        // easel can transition wet brushes to dry if drying is enabled.
        public static HashSet<int> GetHotbarWetColors(IPlayer player, IWorldAccessor world)
        {
            var colors = new HashSet<int>();
            var inv = player.InventoryManager.GetHotbarInventory();
            if (inv == null) return colors;

            for (int i = 0; i < inv.Count; i++)
            {
                var slot = inv[i];
                if (slot?.Itemstack?.Collectible is ItemPaintbrush)
                {
                    ItemPaintbrush.CheckDrying(slot, world);
                    if (ItemPaintbrush.IsWet(slot.Itemstack))
                    {
                        int ci = ItemPaintbrush.GetColor(slot.Itemstack);
                        if (ci >= 0 && ci < 16) colors.Add(ci);
                    }
                }
            }
            return colors;
        }

        private static bool IsCanvasItem(ItemStack stack)
        {
            if (stack == null) return false;
            return stack.Collectible?.Code?.Domain == "vspaint"
                && stack.Collectible.Code.Path == "canvas"
                && !stack.Attributes.HasAttribute("pixelData");
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(
            IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            var be = world.BlockAccessor.GetBlockEntity(selection.Position) as BlockEntityEasel;
            if (be == null) return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);

            if (!be.HasCanvas)
                return new WorldInteraction[]
                {
                    new WorldInteraction
                    {
                        ActionLangCode = "vspaint:interaction-mountcanvas",
                        MouseButton    = EnumMouseButton.Right,
                        Itemstacks     = new ItemStack[]
                            { new ItemStack(world.GetItem(new AssetLocation("vspaint", "canvas"))) }
                    }
                };

            if (be.IsFinished)
                return new WorldInteraction[]
                {
                    new WorldInteraction
                    {
                        ActionLangCode = "vspaint:interaction-takecanvas",
                        MouseButton    = EnumMouseButton.Right,
                        HotKeyCode     = "shift"
                    }
                };

            return new WorldInteraction[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "vspaint:interaction-openpainting",
                    MouseButton    = EnumMouseButton.Right,
                },
                new WorldInteraction
                {
                    ActionLangCode = "vspaint:interaction-takecanvas",
                    MouseButton    = EnumMouseButton.Right,
                    HotKeyCode     = "shift"
                }
            };
        }
    }
}
