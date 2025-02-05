using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrikerLink.Unity.Runtime.Samples
{
    public class BallzookaProjectile : MonoBehaviour
    {
        public float life = 5f;
        [Header("Physics")]
        public Rigidbody rigidBody;

        [Header("Audio")]
        public AudioSource bounceSource;
        public AudioSource flySource;
        public List<AudioClip> bounceClips;
        public float collisionSfxCooldown = 0.04f;

        float lastBounceTime; 

        // Start is called before the first frame update
        void Start()
        {
            Destroy(gameObject, life);
        }

        // Update is called once per frame
        void Update()
        {

        }

        public void ApplyForce(Vector3 direction, float speed)
        {
            rigidBody.AddForce(direction.normalized * speed, ForceMode.VelocityChange);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (lastBounceTime > Time.time - collisionSfxCooldown)
                return;

            lastBounceTime = Time.time;

            bounceSource.PlayOneShot(bounceClips[Random.Range(0, bounceClips.Count)]);
        }
    }
}