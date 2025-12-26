using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MXR.SDK.Samples
{
    // Attach this to any Button inside a ScrollRect
    public class ForwardScrollToParent : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private ScrollRect parentScrollRect;

        void Awake()
        {
            parentScrollRect = GetComponentInParent<ScrollRect>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (parentScrollRect != null)
                parentScrollRect.OnBeginDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (parentScrollRect != null)
                parentScrollRect.OnDrag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (parentScrollRect != null)
                parentScrollRect.OnEndDrag(eventData);
        }
    }
}