using ARNav.Core.Geometry;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ARNav.Runtime.Import
{
    /// Displays one source image while keeping all annotation coordinates in original pixels.
    public sealed class FloorPlanViewer : MonoBehaviour
    {
        [SerializeField] private RectTransform viewport;
        [SerializeField] private RawImage imageDisplay;
        [SerializeField] private float maximumZoom = 8f;
        [SerializeField] private Camera uiCamera;
        private Texture2D texture;
        private float fitScale;
        private float zoom = 1f;
        private ViewerPoint pan;
        private int sourceWidth;
        private int sourceHeight;
        private bool dragging;
        private Vector2 lastPointer;
        private float lastPinchDistance;

        public bool IsLoaded { get { return texture != null; } }
        public Texture2D SourceTexture { get { return texture; } }
        public bool OriginalImageVisible { get { return imageDisplay == null || imageDisplay.enabled; } }
        public int SourceWidthPixels { get { return sourceWidth; } }
        public int SourceHeightPixels { get { return sourceHeight; } }
        public float Zoom { get { return zoom; } }
        public bool NavigationEnabled { get; set; } = true;
        public float ImagePixelsPerViewerUnit { get { return fitScale * zoom <= 0 ? 0 : 1f / (fitScale * zoom); } }

        public void Load(Texture2D source, int width, int height)
        {
            Unload(); texture = source; sourceWidth = width; sourceHeight = height;
            if (imageDisplay != null) { imageDisplay.texture = texture; imageDisplay.color = Color.white; imageDisplay.enabled = true; }
            FitToView();
        }

        public void SetOriginalImageVisible(bool visible)
        {
            if (imageDisplay != null) { imageDisplay.enabled = visible; imageDisplay.color = new Color(1f, 1f, 1f, visible ? 1f : 0f); }
        }

        public void SetOriginalImageOpacity(float opacity)
        {
            if (imageDisplay != null) imageDisplay.color = new Color(1f, 1f, 1f, Mathf.Clamp01(opacity));
        }

        public void Unload(bool destroyTexture = true)
        {
            if (imageDisplay != null) { imageDisplay.texture = null; imageDisplay.color = new Color(1f, 1f, 1f, 0f); }
            if (destroyTexture && texture != null) Destroy(texture);
            texture = null; sourceWidth = 0; sourceHeight = 0;
        }

        public void FitToView()
        {
            if (!IsLoaded || viewport == null) return;
            fitScale = FloorPlanCoordinateMapper.CalculateFitScale(sourceWidth, sourceHeight, viewport.rect.width, viewport.rect.height);
            zoom = 1f; pan = new ViewerPoint(0, 0); ApplyView();
        }

        /// <summary>Fits a source-pixel rectangle, while keeping the shared image viewport transform.</summary>
        public void FitToPixelBounds(float minX, float minY, float maxX, float maxY, float padding = 0.88f)
        {
            if (!IsLoaded || viewport == null) return;
            minX = Mathf.Clamp(minX, 0f, sourceWidth); maxX = Mathf.Clamp(maxX, 0f, sourceWidth);
            minY = Mathf.Clamp(minY, 0f, sourceHeight); maxY = Mathf.Clamp(maxY, 0f, sourceHeight);
            if (maxX <= minX || maxY <= minY) { FitToView(); return; }
            fitScale = FloorPlanCoordinateMapper.CalculateFitScale(sourceWidth, sourceHeight, viewport.rect.width, viewport.rect.height);
            var safePadding = Mathf.Clamp(padding, 0.5f, 1f);
            var desiredScale = Mathf.Min(viewport.rect.width * safePadding / (maxX - minX), viewport.rect.height * safePadding / (maxY - minY));
            zoom = Mathf.Clamp(desiredScale / Mathf.Max(0.0001f, fitScale), 1f, maximumZoom);
            var center = FloorPlanCoordinateMapper.ImagePixelToViewportLocal(new PixelPoint((minX + maxX) * 0.5f, (minY + maxY) * 0.5f), sourceWidth, sourceHeight, fitScale * zoom, new ViewerPoint(0, 0));
            pan = new ViewerPoint(-center.x, -center.y);
            ApplyView();
        }

        public void ResetView() { FitToView(); }

        public bool IsScreenPointInsideImage(Vector2 screenPoint)
        { return TryScreenToImagePixel(screenPoint, out _); }

        public bool TryScreenToImagePixel(Vector2 screenPoint, out PixelPoint pixel)
        {
            pixel = new PixelPoint();
            if (!IsLoaded || viewport == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screenPoint, uiCamera, out var local)) return false;
            return FloorPlanCoordinateMapper.TryViewportLocalToImagePixel(new ViewerPoint(local.x, local.y), sourceWidth, sourceHeight, fitScale * zoom, pan, out pixel);
        }

        public Vector2 ImagePixelToScreen(PixelPoint pixel)
        {
            if (!IsLoaded || viewport == null) return Vector2.zero;
            var local = FloorPlanCoordinateMapper.ImagePixelToViewportLocal(pixel, sourceWidth, sourceHeight, fitScale * zoom, pan);
            return RectTransformUtility.WorldToScreenPoint(uiCamera, viewport.TransformPoint(new Vector3(local.x, local.y, 0)));
        }

        public bool TryImagePixelToViewportLocal(PixelPoint pixel, out Vector2 local)
        {
            local = Vector2.zero;
            if (!IsLoaded) return false;
            var point = FloorPlanCoordinateMapper.ImagePixelToViewportLocal(pixel, sourceWidth, sourceHeight, fitScale * zoom, pan);
            local = new Vector2(point.x, point.y); return true;
        }

        public void ZoomAtScreen(Vector2 screenPoint, float multiplier)
        {
            if (!IsLoaded || fitScale <= 0 || multiplier <= 0) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screenPoint, uiCamera, out var local)) return;
            PixelPoint focus;
            if (!FloorPlanCoordinateMapper.TryViewportLocalToImagePixel(new ViewerPoint(local.x, local.y), sourceWidth, sourceHeight, fitScale * zoom, pan, out focus)) return;
            var newZoom = Mathf.Clamp(zoom * multiplier, 1f, maximumZoom);
            var newLocal = FloorPlanCoordinateMapper.ImagePixelToViewportLocal(focus, sourceWidth, sourceHeight, fitScale * newZoom, new ViewerPoint(0, 0));
            pan = new ViewerPoint(local.x - newLocal.x, local.y - newLocal.y); zoom = newZoom; ApplyView();
        }

        private void Update()
        {
            if (!IsLoaded || viewport == null || !NavigationEnabled) return;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                var pointer = mouse.position.ReadValue();
                if (mouse.leftButton.wasPressedThisFrame) { dragging = true; lastPointer = pointer; }
                if (dragging && mouse.leftButton.isPressed) { Pan(pointer - lastPointer); lastPointer = pointer; }
                if (mouse.leftButton.wasReleasedThisFrame) dragging = false;
                var wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f) ZoomAtScreen(pointer, Mathf.Pow(1.12f, wheel / 120f));
            }
            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                var active = touchscreen.touches;
                if (active.Count == 1 && active[0].press.isPressed) Pan(active[0].delta.ReadValue());
                if (active.Count >= 2 && active[0].press.isPressed && active[1].press.isPressed)
                {
                    var a = active[0].position.ReadValue(); var b = active[1].position.ReadValue(); var distance = Vector2.Distance(a, b);
                    var midpoint = (a + b) * 0.5f;
                    if (lastPinchDistance > 0) ZoomAtScreen(midpoint, distance / lastPinchDistance);
                    lastPinchDistance = distance;
                }
                else lastPinchDistance = 0;
            }
        }

        private void Pan(Vector2 delta) { pan = new ViewerPoint(pan.x + delta.x, pan.y + delta.y); ApplyView(); }
        private void ApplyView()
        {
            if (imageDisplay == null || viewport == null || !IsLoaded) return;
            var scale = fitScale * zoom;
            imageDisplay.rectTransform.sizeDelta = new Vector2(sourceWidth * scale, sourceHeight * scale);
            var maxX = Mathf.Max(0, (imageDisplay.rectTransform.rect.width - viewport.rect.width) * 0.5f);
            var maxY = Mathf.Max(0, (imageDisplay.rectTransform.rect.height - viewport.rect.height) * 0.5f);
            pan = new ViewerPoint(Mathf.Clamp(pan.x, -maxX, maxX), Mathf.Clamp(pan.y, -maxY, maxY));
            imageDisplay.rectTransform.anchoredPosition = new Vector2(pan.x, pan.y);
        }
        private void OnDestroy() { Unload(); }
    }
}
