using System.Collections.Generic;
using UnityEngine;

namespace Amlos.AI.PathFinder
{
    /// <summary>
    /// Path finder base class
    /// </summary>
    public abstract class PathFinder
    {
        public List<Vector2Int> FindPath(Vector3 startPoint, Vector3 endPoint) => FindPath(Vector2Int.FloorToInt(startPoint), Vector2Int.FloorToInt(endPoint));

        /// <summary>
        /// find the path between to point
        /// </summary>
        /// <param name="startPoint"></param>
        /// <param name="endPoint"></param>
        /// <returns></returns>
        public abstract List<Vector2Int> FindPath(Vector2Int startPoint, Vector2Int endPoint);
    }
}
