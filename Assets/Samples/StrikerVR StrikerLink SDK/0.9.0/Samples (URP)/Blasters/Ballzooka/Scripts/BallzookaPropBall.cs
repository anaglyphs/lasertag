using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrikerLink.Unity.Runtime.Samples
{
    public class BallzookaPropBall : MonoBehaviour
    {
        public bool isSmall = false;
        public BallzookaController ballzookaController;

        private void OnCollisionEnter(Collision collision)
        {
            if(isSmall && collision.collider.gameObject.name.Contains("BallzookaBall_Prop_Small")) // Most performant way that doesn't require tags or layers
            {
                ballzookaController.OnSmallBallCollision();
            } else if(!isSmall && (collision.collider.gameObject.name.Contains("BallzookaBall_Prop_Large") || collision.collider.gameObject.name.Contains("SM_ballzooka_glass_coll")))
            {
                ballzookaController.OnLargeBallCollision();
            }
        }
    }
}