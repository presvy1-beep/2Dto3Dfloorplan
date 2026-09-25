using UnityEngine;
using System;

namespace ARNav.Runtime.App
{
    /// <summary>Single owner of the portrait display settling state.</summary>
    public static class DisplayOrientationState
    {
        public static bool IsStable { get; private set; }
        public static int Width { get; private set; }
        public static int Height { get; private set; }
        public static Rect SafeArea { get; private set; }
        public static int Generation { get; private set; }
        public static event Action Changed;

        public static void Reset()
        { IsStable = false; Width = Height = 0; SafeArea = new Rect(); Generation++; Changed?.Invoke(); }

        public static bool TryCommitStablePortrait()
        {
            if (Screen.width <= 0 || Screen.height <= 0 || Screen.height < Screen.width) return false;
            Width = Screen.width; Height = Screen.height; SafeArea = Screen.safeArea; IsStable = true; Generation++; Changed?.Invoke();
            return true;
        }

        public static bool RefreshIfChanged()
        {
            if (!IsStable || Screen.width <= 0 || Screen.height <= 0 || Screen.height < Screen.width) return false;
            if (Width == Screen.width && Height == Screen.height && SafeArea == Screen.safeArea) return false;
            Width = Screen.width; Height = Screen.height; SafeArea = Screen.safeArea; Generation++; Changed?.Invoke(); return true;
        }
    }
}
