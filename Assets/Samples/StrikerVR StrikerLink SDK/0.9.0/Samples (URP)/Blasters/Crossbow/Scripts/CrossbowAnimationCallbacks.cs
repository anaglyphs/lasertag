using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrikerLink.Unity.Runtime.Samples
{
    public class CrossbowAnimationCallbacks : MonoBehaviour
    {
        public CrossbowController crossbow;

        public void OnReachedNotch(int index)
        {
            crossbow.OnReachedNotch(index);
        }
    }
}