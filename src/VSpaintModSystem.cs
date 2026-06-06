using Vintagestory.API.Common;
using VSpaint.Network;

namespace VSpaint
{
    public class VSpaintModSystem : ModSystem
    {
        // Per-side: a static field races in singleplayer where both sides share
        // the process; the GUI's send would silently target whichever Start ran last.
        public PaintNetworkHandler NetworkHandler { get; private set; }

        public override void StartPre(ICoreAPI api)
        {
            base.StartPre(api);
            VSpaintConfig.Load(api);
        }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            api.RegisterBlockClass("BlockEasel",          typeof(BlockEasel));
            api.RegisterBlockClass("BlockCanvasPainting", typeof(BlockCanvasPainting));
            api.RegisterBlockBehaviorClass("BlockEaselBehavior", typeof(BlockEaselBehavior));

            api.RegisterBlockEntityClass("BlockEntityEasel",          typeof(BlockEntityEasel));
            api.RegisterBlockEntityClass("BlockEntityCanvasPainting", typeof(BlockEntityCanvasPainting));

            api.RegisterItemClass("ItemPaintbrush", typeof(ItemPaintbrush));
            api.RegisterCollectibleBehaviorClass("PaintbrushDip", typeof(CollectibleBehaviorPaintbrushDip));

            NetworkHandler = new PaintNetworkHandler(api);
            NetworkHandler.RegisterChannel();
        }
    }
}
