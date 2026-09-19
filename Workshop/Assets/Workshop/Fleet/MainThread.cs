using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// The one rule for the SDK in Unity: its callbacks arrive on background
    /// threads. Enqueue here; a single instance drains the queue in Update.
    /// </summary>
    public sealed class MainThread : MonoBehaviour
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        private static MainThread _instance;

        public static void Run(Action a) => Queue.Enqueue(a);

        public static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("MainThread");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MainThread>();
        }

        private void Update()
        {
            while (Queue.TryDequeue(out var a))
            {
                try { a(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
