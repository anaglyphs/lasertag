using UnityEngine;

namespace Anaglyph.XRTemplate
{
    public class EnvironmentMapperActions : MonoBehaviour
    {
        public void ClearMap() => EnvironmentMapper.Instance?.ClearMap();
    }
}
