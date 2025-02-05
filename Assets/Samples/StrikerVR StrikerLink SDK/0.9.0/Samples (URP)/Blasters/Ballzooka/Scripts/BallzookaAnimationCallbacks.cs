using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrikerLink.Unity.Runtime.Samples
{
    public class BallzookaAnimationCallbacks : MonoBehaviour
    {
        public BallzookaController controller;

        public void OnBarrelAligned()
        {
            controller.OnBarrelAligned();
        }
    }
}