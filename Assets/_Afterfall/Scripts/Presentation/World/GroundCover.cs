using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>Ground detail around the camera (grass, flowers, pebbles). Not implemented yet.</summary>
    public sealed class GroundCover : MonoBehaviour
    {
        private GameController _gc;

        public void Initialize(GameController gc)
        {
            _gc = gc;
        }
    }
}
