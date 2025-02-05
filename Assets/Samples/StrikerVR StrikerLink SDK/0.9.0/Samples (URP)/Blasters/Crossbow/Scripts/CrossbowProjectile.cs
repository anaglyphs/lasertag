using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrikerLink.Unity.Runtime.Samples
{
    public class CrossbowProjectile : MonoBehaviour
    {
        [Header("Rendering")]
        public SkinnedMeshRenderer skinnedMesh;
        public AnimationCurve morphCurveX;
        public AnimationCurve morphCurveY;
        public AnimationCurve morphCurveZ;
        public float morphWeightScale = 100f;

        [Header("Physics")]
        public Rigidbody rigidBody;
        public List<Collider> colliders;
        public LayerMask explosionLayerMask;
        public float explosionRadius;
        public float explosionForce;

        [Header("Projectile Settings")]
        public bool isExplosive = false;
        public GameObject explosionPrefab;
        public Vector3 attachAreaOrigin = new Vector3(0f, 0.1f, 0f);
        public float attachAreaRadius = 0.1f;
        public float lifeExpectancy = 5f;

        [Header("Audio")]
        public AudioSource audioSource;
        public AudioClip clipAttach;

        bool hasImpacted = false;
        bool hasFired = false;
        bool hasAttached = false;
        bool attachedToImmoveable = false;

        internal float spawnProgress = 0f;
        float life = 0f;
        Vector3 lastHitPoint;
        Vector3 lastHitNormal;

        public CrossbowController firingController;

        // Start is called before the first frame update
        void Start()
        {
            rigidBody.isKinematic = true;
            Spawn();
        }

        void Spawn()
        {
            StartCoroutine(SpawningRoutine());
        }

        IEnumerator SpawningRoutine()
        {
            spawnProgress = 0f;

            while (spawnProgress >= 0 && spawnProgress < 1)
            {
                spawnProgress += Time.deltaTime;

                float valueX = 1 - morphCurveX.Evaluate(spawnProgress);
                float valueY = 1 - morphCurveY.Evaluate(spawnProgress);
                float valueZ = 1 - morphCurveZ.Evaluate(spawnProgress);

                skinnedMesh.SetBlendShapeWeight(4, valueX * morphWeightScale);
                skinnedMesh.SetBlendShapeWeight(1, valueY * morphWeightScale);
                skinnedMesh.SetBlendShapeWeight(2, valueZ * morphWeightScale);

                yield return null;
            }
        }

        // Update is called once per frame
        void Update()
        {
            UpdateOrientation();
            UpdateLife();
        }

        void UpdateLife()
        {
            if (hasFired && !hasAttached)
            {
                life += Time.deltaTime;
                if (life > lifeExpectancy)
                    Destroy(gameObject);
            }
        }

        void UpdateOrientation()
        {
            if(hasFired && !hasAttached && rigidBody.linearVelocity.sqrMagnitude > 0f)
                transform.up = rigidBody.linearVelocity.normalized;
        }

        public void Fire(float speed)
        {
            rigidBody.isKinematic = false;
            rigidBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            transform.SetParent(null);
            rigidBody.AddForce(transform.up * speed, ForceMode.VelocityChange);
            hasFired = true;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!hasFired)
                return;

            if (hasImpacted)
                return;

            hasImpacted = true;

            if(isExplosive)
            {
                Explode();
            } else
            {
                AttemptAttach(collision);
            }
        }

        void Explode()
        {
            if (explosionPrefab != null)
                Instantiate(explosionPrefab, transform.position, transform.rotation);

            CreateExplosionForce();

            if (firingController != null)
                firingController.OnDistantExplosion();

            Destroy(gameObject);
        }

        void CreateExplosionForce()
        {
            Collider[] cols = Physics.OverlapSphere(transform.position, explosionRadius);

            foreach(Collider c in cols)
            {
                if(c.attachedRigidbody != null)
                {
                    c.attachedRigidbody.AddExplosionForce(explosionForce, transform.position, explosionRadius, 1f, ForceMode.Impulse);
                }
            }
        }

        void AttemptAttach(Collision col)
        {
            foreach(ContactPoint hitPoint in col.contacts)
            {
                float dist = Vector3.Distance(hitPoint.point, GetWorldSpaceAttachmentPoint());
                bool inRadius = dist <= attachAreaRadius;
                bool facingOtherWay = Vector3.Dot(hitPoint.normal, transform.up) <= -0.2f;

                // Is it within the attach area and the hit normal is facing the opposite direction to the projectile?
                if(inRadius && facingOtherWay)
                {
                    // Stick!
                    DoAttach(col, hitPoint);
                    break;
                }
            }
        }

        void DoAttach(Collision col, ContactPoint point)
        {
            audioSource.PlayOneShot(clipAttach);

            if (col.rigidbody && !col.rigidbody.isKinematic) // Keep physics going
            { 
                transform.up = -point.normal;
                transform.position = point.point;

                FixedJoint joint = gameObject.AddComponent<FixedJoint>();
                joint.connectedBody = col.rigidbody;
                joint.autoConfigureConnectedAnchor = true;
                attachedToImmoveable = false;
            }
            else // Parent it, a joint would be a waste here.
            {
                rigidBody.isKinematic = true;
                rigidBody.useGravity = false;
                transform.SetParent(col.transform);
                transform.up = -point.normal;
                transform.position = point.point;

                foreach (Collider c in colliders)
                    c.enabled = false;

                attachedToImmoveable = true;
            }

            hasAttached = true;

            Debug.Log("[CROSSBOW] Attached to " + col.transform.name);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.DrawWireSphere(GetWorldSpaceAttachmentPoint(), attachAreaRadius);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, explosionRadius);
        }

        Vector3 GetWorldSpaceAttachmentPoint()
        {
            return transform.TransformPoint(attachAreaOrigin);
        }
    }
}