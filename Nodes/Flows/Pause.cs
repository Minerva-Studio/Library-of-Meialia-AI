using System;

namespace Amlos.AI
{
    [NodeTip("Pause the behaviour tree")]
    [Serializable]
    public sealed class Pause : Flow
    {
        public override void Execute()
        {
            behaviourTree.Pause();
        }


        public override void Initialize()
        {
        }
    }
}