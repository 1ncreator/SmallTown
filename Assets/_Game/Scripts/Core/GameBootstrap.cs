using UnityEngine;

namespace SmallTown.Core
{
    /// <summary>Starts the game from code so pressing Play works even in an empty scene.</summary>
    public static class GameBootstrap
    {
        public static bool Disabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Disabled || GameController.Instance != null) return;
            var go = new GameObject("SmallTown");
            go.AddComponent<GameController>();
        }
    }
}
