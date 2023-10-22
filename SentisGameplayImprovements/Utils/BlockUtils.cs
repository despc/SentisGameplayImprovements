using Sandbox.Game.Entities.Cube;

namespace SentisGameplayImprovements
{
    public static class BlockUtils
    {
        public static int GetPCU(MySlimBlock block)
        {
            int num = 0;
            if (block.ComponentStack.IsFunctional)
                num = block.BlockDefinition.PCU;
            return num;
        }
    }
}