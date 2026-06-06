using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace VSpaint
{
    public class BlockEntityEasel : BlockEntity
    {
        public bool HasCanvas  { get; private set; }
        public bool IsFinished { get; private set; }
        public byte[] PixelData { get; private set; }

        public int[] pixels = new int[PaintingUtil.PixelCount];

        private const int CanvasW = PaintingUtil.Width;
        private const int CanvasH = PaintingUtil.Height;

        private int selectedColor = 1;
        private int brushRadius = 0;   // 0=1px, 1=3px, 2=5px
        private bool eraserMode = false;
        private bool isDirty = false;
        private bool finishConfirmMode = false;

        // Mesh is built on the main thread, read on the tessellation thread.
        private readonly object meshLock = new object();
        private MeshData clientMesh;
        private bool     needsRebuild;
        private bool     rebuildQueued;

        // Easel content changes every save, so without freeing the previous slot
        // every stroke would leak a texture atlas entry for the world's lifetime.
        private int currentAtlasSubId;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Client && HasCanvas)
                RequestMeshRebuild();

        }

        public void MountCanvas()
        {
            HasCanvas  = true;
            IsFinished = false;
            PixelData  = null;
            pixels.Fill(0);
            lock (meshLock) { clientMesh = null; }
            FreeAtlasSlot();
            MarkDirty(true);
        }

        // Caller must be on the main client thread; atlas mutation is not threadsafe.
        private void FreeAtlasSlot()
        {
            if (currentAtlasSubId == 0) return;
            if (Api is ICoreClientAPI capi)
                capi.BlockTextureAtlas.FreeTextureSpace(currentAtlasSubId);
            currentAtlasSubId = 0;
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            FreeAtlasSlot();
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            FreeAtlasSlot();
        }

        public void TakeCanvas(IPlayer player)
        {
            if (!HasCanvas || !IsFinished) return;

            Block canvasBlock = Api.World.GetBlock(new AssetLocation("vspaint", "canvas-painting-north"));
            if (canvasBlock == null || canvasBlock.Id == 0)
            {
                Api.Logger.Error("[VSpaint] TakeCanvas: could not find block 'vspaint:canvas-painting-north'");
                return;
            }

            var stack = new ItemStack(canvasBlock);
            if (PixelData != null)
                stack.Attributes.SetBytes("pixelData", PixelData);

            if (!player.InventoryManager.TryGiveItemstack(stack, true))
                Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.0, 0.5));

            HasCanvas  = false;
            IsFinished = false;
            PixelData  = null;
            lock (meshLock) { clientMesh = null; }
            FreeAtlasSlot();
            MarkDirty(true);
        }

        public void UpdatePixelData(byte[] data)
        {
            if (!HasCanvas) return;
            PixelData = data;
            MarkDirty(true);
        }

        public void SaveToServer(IPlayer player)
        {
            if (!isDirty) return;
            byte[] encoded = PaintingUtil.EncodePixels(pixels);
            Api.ModLoader.GetModSystem<VSpaintModSystem>()?.NetworkHandler?.SendSave(Pos, encoded);
            isDirty = false;

            if (player != null)
            {
                ItemSlot brushSlot = player.InventoryManager.ActiveHotbarSlot;
                if (brushSlot?.Itemstack?.Collectible is ItemPaintbrush brush)
                    brush.UseBrush(brushSlot, player);
            }
        }


        public Vec3i ToCanvasSpace(Vec3d normalized_in)
        {
            int x = 0;
            int y = 0;

            int facing = EaselFacingRotation();

            if (facing == 0)
            {
                x = (int)(normalized_in.X * (double)(CanvasW - 1));
                y = (CanvasH - 1) - (int)(normalized_in.Y * (double)(CanvasH -1));
            }
            else if(facing == 1)
            {
                x = (int)(normalized_in.Z * (double)(CanvasW - 1));
                y = (CanvasH - 1) - (int)(normalized_in.Y * (double)(CanvasH - 1));
            }
            else if (facing == 2)
            {
                x = (CanvasW - 1) - (int)(normalized_in.X * (double)(CanvasW - 1));
                y = (CanvasH - 1) - (int)(normalized_in.Y * (double)(CanvasH - 1));
            }
            else if (facing == 3)
            {
                x = (CanvasW - 1) - (int)(normalized_in.Z * (double)(CanvasW - 1));
                y = (CanvasH - 1) - (int)(normalized_in.Y * (double)(CanvasH - 1));
            }

            return new Vec3i(x, y, 0);
        }

        public void DrawBrushStamp(int cx, int cy)
        {
            Api.Logger.Event("DrawStamp {0} {1}", cx, cy);
            isDirty = true;
            int colorIdx = eraserMode ? 0 : selectedColor;

            for (int dy = -brushRadius; dy <= brushRadius; dy++)
            {
                for (int dx = -brushRadius; dx <= brushRadius; dx++)
                {
                    if (dx * dx + dy * dy > brushRadius * brushRadius) continue;
                    int px = cx + dx;
                    int py = cy + dy;

                    Api.Logger.Event("px py {0} {1}", px, py);
                    if (px < 0 || px >= CanvasW || py < 0 || py >= CanvasH) continue;
                    pixels[py * CanvasW + px] = colorIdx;
                }
            }
        }
        public void FinishPainting()
        {
            if (!HasCanvas) return;
            IsFinished = true;
            MarkDirty(true);
        }

        public void OpenPaintGui(IPlayer player, HashSet<int> availableColors)
        {
            if (Api.Side != EnumAppSide.Client) return;
            var capi = (ICoreClientAPI)Api;
            var dialog = new GuiDialogPainting(capi, Pos, PixelData, availableColors);
            dialog.TryOpen();
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
        {
            if (HasCanvas && Api?.Side == EnumAppSide.Client)
            {
                if (needsRebuild)
                {
                    needsRebuild = false;
                    RequestMeshRebuild();
                }

                MeshData mesh;
                lock (meshLock) { mesh = clientMesh; }

                if (mesh != null)
                {
                    mesher.AddMeshData(mesh.Clone());
                    return true;
                }
            }

            return false;
        }

        private void RequestMeshRebuild()
        {
            if (Api?.Side != EnumAppSide.Client) return;
            var capi = (ICoreClientAPI)Api;

            lock (meshLock)
            {
                if (rebuildQueued) return;
                rebuildQueued = true;
            }

            capi.Event.EnqueueMainThreadTask(() =>
            {
                lock (meshLock) rebuildQueued = false;
                BuildClientMesh(capi);
            }, "vspaint-easel-rebuild");
        }

        private void BuildClientMesh(ICoreClientAPI capi)
        {
            try
            {
                var shapeAsset = capi.Assets.TryGet(
                    new AssetLocation("vspaint", "shapes/block/canvas_on_easel.json"));

                if (shapeAsset == null)
                {
                    capi.Logger.Error("[VSpaint] BuildClientMesh: canvas_on_easel.json not found");
                    lock (meshLock) { clientMesh = null; }
                    return;
                }

                Shape shape = shapeAsset.ToObject<Shape>();
                if (shape == null)
                {
                    capi.Logger.Error("[VSpaint] BuildClientMesh: failed to parse canvas_on_easel shape");
                    lock (meshLock) { clientMesh = null; }
                    return;
                }

                ITexPositionSource blockSrc = capi.Tesselator.GetTextureSource(Block);
                ITexPositionSource texSrc   = blockSrc;

                if (PixelData != null)
                {
                    byte[] pngBytes = PaintingUtil.PixelsToPng(PixelData);
                    if (pngBytes != null)
                    {
                        // Content hash in the key forces a fresh atlas slot per
                        // unique image; GetOrInsertTexture is cached by key.
                        int hash = 17;
                        foreach (byte b in PixelData) hash = hash * 31 + b;
                        string key = $"vspaint-easel-{Pos.X}-{Pos.Y}-{Pos.Z}-{hash}";
                        capi.BlockTextureAtlas.GetOrInsertTexture(
                            new AssetLocation("vspaint", key),
                            out int newSubId,
                            out TextureAtlasPosition paintingPos,
                            () => capi.Render.BitmapCreateFromPng(pngBytes),
                            0.005f
                        );

                        if (paintingPos != null)
                        {
                            if (currentAtlasSubId != 0 && currentAtlasSubId != newSubId)
                                capi.BlockTextureAtlas.FreeTextureSpace(currentAtlasSubId);
                            currentAtlasSubId = newSubId;

                            texSrc = new PaintingOverlayTexSource(blockSrc, paintingPos, capi.BlockTextureAtlas.Size);
                        }
                    }
                }

                capi.Tesselator.TesselateShape("vspaint-easel-canvas", shape, out MeshData mesh, texSrc);

                if (mesh == null)
                {
                    capi.Logger.Error("[VSpaint] BuildClientMesh: TesselateShape returned null");
                    lock (meshLock) { clientMesh = null; }
                    return;
                }

                float rotY = EaselFacingRotationRad();
                if (rotY != 0f)
                    mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, rotY, 0f);

                lock (meshLock) { clientMesh = mesh; }
                MarkDirty(true);
            }
            catch (Exception ex)
            {
                capi.Logger.Error("[VSpaint] BuildClientMesh failed at {0}: {1}", Pos, ex);
                lock (meshLock) { clientMesh = null; }
            }
        }

        private float EaselFacingRotationRad()
        {
            string path = Block?.Code?.Path ?? "";
            if (path.EndsWith("-east"))  return 0f;
            if (path.EndsWith("-south")) return 3f * GameMath.PIHALF;
            if (path.EndsWith("-west"))  return GameMath.PI;
            return GameMath.PIHALF;
        }

        private int EaselFacingRotation()
        {
            string path = Block?.Code?.Path ?? "";
            if (path.EndsWith("-east")) return 1;
            if (path.EndsWith("-south")) return 2;
            if (path.EndsWith("-west")) return 3;
            return 0;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetBool("hasCanvas",  HasCanvas);
            tree.SetBool("isFinished", IsFinished);
            if (PixelData != null)
                tree.SetBytes("pixelData", PixelData);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolve)
        {
            base.FromTreeAttributes(tree, worldForResolve);
            HasCanvas  = tree.GetBool("hasCanvas");
            IsFinished = tree.GetBool("isFinished");
            PixelData  = tree.GetBytes("pixelData", null);
            lock (meshLock) { clientMesh = null; }

            if (Api?.Side == EnumAppSide.Client)
            {
                needsRebuild = true;
                RequestMeshRebuild();
            }
        }

        // Maps the "painting" texture key to the generated atlas slot; everything
        // else falls through to the block's normal texture source.
        private sealed class PaintingOverlayTexSource : ITexPositionSource
        {
            private readonly ITexPositionSource blockSrc;
            private readonly TextureAtlasPosition paintingPos;
            private readonly Size2i atlasSize;

            public PaintingOverlayTexSource(ITexPositionSource blockSrc, TextureAtlasPosition paintingPos, Size2i atlasSize)
            {
                this.blockSrc    = blockSrc;
                this.paintingPos = paintingPos;
                this.atlasSize   = atlasSize;
            }

            public TextureAtlasPosition this[string textureCode] =>
                textureCode == "painting" ? paintingPos : blockSrc[textureCode];

            public Size2i AtlasSize => atlasSize;
        }
    }
}
