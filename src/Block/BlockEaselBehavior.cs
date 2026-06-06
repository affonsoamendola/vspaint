using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using static OpenTK.Graphics.OpenGL.GL;

namespace VSpaint
{
    public class BlockEaselBehavior : BlockBehavior
    {
        public BlockEaselBehavior(Block block) : base(block) { }

        public override void OnBlockPlaced(IWorldAccessor world, BlockPos pos, ref EnumHandling handling)
        {
            //world.BlockAccessor.GetBlock(pos).SelectionBoxes[0].RotatedCopy(0,)
        }
    }
}
