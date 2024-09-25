using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.XRTemplate
{
    public class DemoGun : MonoBehaviour
    {
        public float speed;
        public Rigidbody prefab;

        public void Shoot()
        {
            GameObject projectile = Instantiate(prefab.gameObject, transform.position, transform.rotation);
            projectile.GetComponent<Rigidbody>().velocity = transform.forward * speed;
        }
    }
}
