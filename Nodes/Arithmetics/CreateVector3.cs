using System;
using UnityEngine;

namespace Amlos.AI
{
    [Serializable]
    [NodeTip("create a Vector3")]
    public sealed class CreateVector3 : Arithmetic
    {
        [NumericTypeLimit]
        public VariableField x;
        [NumericTypeLimit]
        public VariableField y;
        [NumericTypeLimit]
        public VariableField z;

        public VariableReference<Vector3> vector;

        public override void Execute()
        {
            if (!vector.IsVector)
            {
                End(false);
            }
            try
            {
                var vx = x.HasValue ? x.NumericValue : 0;
                var vy = y.HasValue ? y.NumericValue : 0;
                var vz = z.HasValue ? z.NumericValue : 0;

                vector.Value = new Vector3(vx, vy, vz);

            }
            catch (Exception)
            {
                End(false);
                throw;
            }
        }

    }
}