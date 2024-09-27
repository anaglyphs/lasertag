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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OVRRayHelper : MonoBehaviour
{
    public MeshRenderer Renderer;

    public Material NormalMaterial;
    public Material PinchMaterial;

    public GameObject Cursor;
    public SpriteRenderer CursorFill;

    void Start()
    {
        if (Renderer != null)
        {
            _initialScale = Renderer.transform.localScale;
        }
        if (Cursor != null)
        {
            _cursorIntitialSize = Cursor.transform.localScale;
        }
    }

    public void UpdatePointerRay(OVRInputRayData rayData)
    {
        if (Renderer != null)
        {
            float targetLength = rayData.IsOverCanvas ? rayData.DistanceToCanvas : DefaultLength;
            Renderer.transform.localPosition = Vector3.forward * (targetLength * 0.5f + 0.05f);
            Renderer.transform.localScale = new Vector3(_initialScale.x, targetLength * 0.5f - 0.025f, _initialScale.z);
            Renderer.sharedMaterial = rayData.IsActive ? PinchMaterial : NormalMaterial;
        }

        if (Cursor != null)
        {
            Cursor.SetActive(rayData.IsOverCanvas);
            Cursor.transform.localScale = Mathf.Lerp(1f, _cursorSelectedScaleFactor, rayData.ActivationStrength) * _cursorIntitialSize;
            if (CursorFill != null)
            {
                float alpha = Mathf.Lerp(0f, 1f, rayData.ActivationStrength);
                CursorFill.color = new Color(1, 1, 1, alpha);
            }
            if (rayData.IsOverCanvas)
            {
                // Apply a slight offset here to correctly render on top of canvas elements.
                Cursor.transform.position = rayData.WorldPosition + rayData.WorldNormal * 0.05f;
                Cursor.transform.forward = rayData.WorldNormal;
            }
        }
    }

    private Vector3 _initialScale;
    public float DefaultLength;
    private Vector3 _cursorIntitialSize;
    private const float _cursorSelectedScaleFactor = 0.5f;
}

public struct OVRInputRayData
{
    public bool IsActive; // Are we pinching / selecting.
    public float ActivationStrength; // Used to represent pinch strength.
    public bool IsOverCanvas; // Are we over a UI Canvas? Used to controller cursor activation.
    public float DistanceToCanvas;
    public Vector3 WorldPosition;
    public Vector3 WorldNormal;
}
