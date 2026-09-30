using System.Collections;
using NUnit.Framework;
using SmallTown.Commands;
using SmallTown.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SmallTown.Tests.PlayMode
{
    public sealed class SmokeTests
    {
        [UnityTest]
        public IEnumerator SceneRunsAt4x_AndCommandsWork()
        {
            SceneManager.LoadScene("SmallTown", LoadSceneMode.Single);
            for (int i = 0; i < 5; i++) yield return null;
            var gc = GameController.Instance;
            Assert.NotNull(gc, "GameController must exist after loading the scene");
            long startTick = gc.Sim.Tick;
            gc.SetSpeed(4);
            float t = 0f;
            while (t < 10f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.Greater(gc.Sim.Tick - startTick, 100, "simulation should advance at 4x");

            string[] commands =
            {
                "закрой северный мост", "пусть пойдёт дождь", "подними реку на 1 метр", "начни фестиваль в парке",
                "сделай ночь", "подожги школу", "ограбь банк", "наступает зима", "преврати парковку в парк",
                "закрой мост", "потуши пожар", "raise the river by 50 cm", "что-то непонятное"
            };
            foreach (var c in commands)
            {
                var r = gc.Execute(c);
                Assert.AreNotEqual(ParseStatus.Empty, r.Status, c);
                for (int i = 0; i < 20; i++) yield return null;
            }
            int depth = gc.Session.UndoDepth;
            Assert.Greater(depth, 5);
            gc.Undo();
            yield return null;
            Assert.AreEqual(depth - 1, gc.Session.UndoDepth);

            gc.Showcase.Start();
            t = 0f;
            while (t < 4f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            gc.Showcase.Stop();
            Assert.IsFalse(gc.Showcase.Active);

            gc.SetCinema(true);
            for (int i = 0; i < 30; i++) yield return null;
            gc.SetCinema(false);
            gc.ResetWorld();
            for (int i = 0; i < 30; i++) yield return null;
            Assert.AreEqual(0, gc.Session.UndoDepth);
        }
    }
}
