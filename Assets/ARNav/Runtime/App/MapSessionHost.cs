using UnityEngine;

namespace ARNav.Runtime.App
{
    /// <summary>Scene-persistent owner of the explicitly selected map session.</summary>
    public sealed class MapSessionHost : MonoBehaviour
    {
        public static MapSessionHost Instance { get; private set; }
        public MapSession Session { get; private set; }
        public string ActiveProjectId { get { return Session == null ? null : Session.CurrentProjectId; } }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this; Session = new MapSession(); DontDestroyOnLoad(gameObject);
        }
        public void SetSession(MapSession session) { Session = session ?? new MapSession(); }
        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
