using System;
using UnityEngine;

namespace ARNav.Core.Navigation
{
    [Serializable]
    public sealed class CameraViewState
    {
        public float angle;
        public float zoom;
        public Vector3 pivot;

        public CameraViewState(float defaultAngle, float defaultZoom) { angle = defaultAngle; zoom = defaultZoom; }

        public void SetAngle(float value) { angle = value; }
        public void SetZoom(float value) { zoom = value; }
        public void SetPivot(Vector3 value) { pivot = value; }
    }
}
