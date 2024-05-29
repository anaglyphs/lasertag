using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MenuXR
{
    [SelectionBase]
    public class SlideToConfirm : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
    {
        [SerializeField] private Transform handle;
        
        public UnityEvent onConfirm;

        private RectTransform _rectTransform;
        private RectTransform _handleRectTransform;

        private float _grabOffset;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _handleRectTransform = handle.GetComponent<RectTransform>();
        }

        private bool _dragging;

        public void OnBeginDrag(PointerEventData eventData)
        {
            _dragging = true;
            _grabOffset = eventData.position.x - handle.localPosition.x;
        }

        public void OnDrag(PointerEventData eventData)
        {
            float x = eventData.position.x - _grabOffset;

            float max = _rectTransform.rect.width - _handleRectTransform.rect.width;

            if (x > max)
            {
                onConfirm.Invoke();

                eventData.dragging = false;
                eventData.pointerDrag = null;
                OnEndDrag(eventData);
            }
            
            x = Mathf.Clamp(x, 0, max);

            Vector3 p = handle.localPosition;
            p.x = x;
            handle.localPosition = p;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragging = false;
        }

        private void Update()
        {
            if (!_dragging)
            {
                float x = handle.localPosition.x - 5000f * Time.deltaTime;

                x = Mathf.Clamp(x, 0, _rectTransform.rect.width - _handleRectTransform.rect.width);

                Vector3 p = handle.localPosition;
                p.x = x;
                handle.localPosition = p;
            }
        }
    }
}