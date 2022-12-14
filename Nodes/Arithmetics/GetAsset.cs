namespace Amlos.AI
{
    public sealed class GetAsset : Arithmetic
    {
        public AssetReference assetReference;
        [TypeLimit(VariableType.UnityObject)]
        public VariableReference result;


        public override void Execute()
        {
            if (result.HasValue)
            {
                result.Value = assetReference.GetAsset();
            }
            End(true);
        }
    }
}