using System.Collections.Generic;
using ARNav.Core.Data;
using UnityEngine;

namespace ARNav.Core.Annotation
{
    public static class AnnotationDataUtility
    {
        public static FloorPlanAnnotationData Clone(FloorPlanAnnotationData data)
        { return data == null ? new FloorPlanAnnotationData() : JsonUtility.FromJson<FloorPlanAnnotationData>(JsonUtility.ToJson(data)); }
    }

    public sealed class AnnotationHistory
    {
        private readonly Stack<FloorPlanAnnotationData> undo = new Stack<FloorPlanAnnotationData>();
        private readonly Stack<FloorPlanAnnotationData> redo = new Stack<FloorPlanAnnotationData>();
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public void Clear() { undo.Clear(); redo.Clear(); }
        public void Record(FloorPlanAnnotationData before, FloorPlanAnnotationData after) { undo.Push(AnnotationDataUtility.Clone(before)); redo.Clear(); }
        public FloorPlanAnnotationData Undo(FloorPlanAnnotationData current) { if (!CanUndo) return current; redo.Push(AnnotationDataUtility.Clone(current)); return undo.Pop(); }
        public FloorPlanAnnotationData Redo(FloorPlanAnnotationData current) { if (!CanRedo) return current; undo.Push(AnnotationDataUtility.Clone(current)); return redo.Pop(); }
    }
}
