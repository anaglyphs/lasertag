/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */


using UnityEngine;
using UnityEngine.UI;

namespace Meta.XR.ImmersiveDebugger.UserInterface.Generic
{
    public class Cursor : OVRCursor
    {
        private Vector3 _startPoint;
        private Vector3 _forward;
        private Vector3 _endPoint;
        private Vector3 _normal;
        private bool _hit;

        public GameObject GameObject { get; private set; }
        public Transform Transform { get; private set; }

        public void Awake()
        {
            GameObject = gameObject;

            GameObject.layer = RuntimeSettings.Instance.PanelLayer;
            var canvasGroup = GameObject.AddComponent<CanvasGroup>();
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
            var image = GameObject.AddComponent<RawImage>();
            image.texture = Resources.Load<Texture2D>("Textures/pointer");
            image.rectTransform.sizeDelta = (new Vector2(20, 20));
            image.raycastTarget = false;

            Transform = transform;
        }

        public override void SetCursorStartDest(Vector3 start, Vector3 dest, Vector3 normal)
        {
            _startPoint = start;
            _endPoint = dest;
            _normal = normal;
            _hit = true;
        }

        public override void SetCursorRay(Transform t)
        {
            _startPoint = t.position;
            _forward = t.forward;
            _normal = _forward;
            _hit = false;
        }

        private void LateUpdate()
        {
            if (_hit)
            {
                Transform.position = _endPoint;
                Transform.rotation = Quaternion.LookRotation(_normal, Vector3.up);
            }
            else
            {
                GameObject.SetActive(false);
            }
        }

        public void Attach(Panel panel)
        {
            if (panel == null)
            {
                return;
            }

            GameObject.SetActive(true);
            Transform.SetParent(panel.Transform, false);
        }
    }
}

