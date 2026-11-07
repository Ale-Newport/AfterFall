using System.Collections;
using System.IO;
using System.Linq;
using Afterfall.Domain.World;
using Afterfall.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Afterfall.Tests.PlayMode
{
    /// <summary>
    /// End-to-end smoke tests that load the real main scene, start a game through the
    /// bootstrap and drive it with simulated keyboard input.
    /// </summary>
    public class GameSmokeTests : InputTestFixture
    {
        private const string MainScene = "Assets/_Afterfall/Scenes/Main.unity";
        private Keyboard _keyboard;
        private Mouse _mouse;

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
        }

        private static IEnumerator StartGame(int seed)
        {
            SceneManager.LoadScene(MainScene);
            yield return null;
            GameBootstrap boot = Object.FindAnyObjectByType<GameBootstrap>();
            Assert.IsNotNull(boot, "Main scene must contain the bootstrap");
            Assert.IsNull(boot.ContentError, "content must load without errors");
            boot.StartNewGame(DifficultyPreset.Exploration, seed);
            float timeout = Time.realtimeSinceStartup + 90f;
            while ((boot.Game == null || !boot.Game.Running) && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.IsNotNull(boot.Game);
            Assert.IsTrue(boot.Game.Running, "world finished building");
            boot.Game.Session.Threats.SpawningEnabled = false;
        }

        private static GameController Game => Object.FindAnyObjectByType<GameBootstrap>().Game;

        [UnityTest]
        public IEnumerator NewGame_BuildsCoherentWorld()
        {
            yield return StartGame(4321);
            GameController gc = Game;
            Assert.IsNotNull(gc.Terrain);
            Assert.IsNotNull(Camera.main, "a main camera exists");
            Assert.Greater(Object.FindObjectsByType<ResourceNodeView>(FindObjectsSortMode.None).Length, 300);
            Vector3 p = gc.Player.transform.position;
            Assert.Less(Mathf.Abs(p.y - gc.GroundHeight(p)), 1.5f, "player stands on the terrain");
            yield return new WaitForSeconds(1f);
            Assert.Less(Mathf.Abs(gc.Player.transform.position.y - gc.GroundHeight(gc.Player.transform.position)), 1.0f, "player did not fall through the world");
        }

        [UnityTest]
        public IEnumerator Keyboard_MovesPlayer()
        {
            yield return StartGame(4322);
            GameController gc = Game;
            Vector3 start = gc.Player.transform.position;
            Press(_keyboard.wKey);
            yield return new WaitForSeconds(1.5f);
            Release(_keyboard.wKey);
            float moved = Vector3.Distance(new Vector3(start.x, 0, start.z), new Vector3(gc.Player.transform.position.x, 0, gc.Player.transform.position.z));
            Assert.Greater(moved, 2f, "W walks the character forward");
            Assert.AreEqual(gc.Player.transform.position.x, gc.Session.Player.Position.X, 0.05f, "simulation receives the physical position");
        }

        [UnityTest]
        public IEnumerator Tab_SwitchesToStrategicCameraAndBack()
        {
            yield return StartGame(4323);
            GameController gc = Game;
            PressAndRelease(_keyboard.tabKey);
            yield return new WaitForSeconds(1.5f);
            Assert.IsTrue(gc.CameraRig.TacticalActive);
            Assert.Greater(Camera.main.transform.position.y, gc.Player.transform.position.y + 15f, "camera is elevated");
            PressAndRelease(_keyboard.tabKey);
            yield return new WaitForSeconds(1.5f);
            Assert.IsFalse(gc.CameraRig.TacticalActive);
            Assert.Less(Vector3.Distance(Camera.main.transform.position, gc.Player.transform.position), 12f, "back behind the survivor");
        }

        [UnityTest]
        public IEnumerator Harvesting_AddsItemsToInventory()
        {
            yield return StartGame(4324);
            GameController gc = Game;
            ResourceNodeView branches = Object.FindObjectsByType<ResourceNodeView>(FindObjectsSortMode.None)
                .Where(v => v.Def.Id == "branch_pile")
                .OrderBy(v => Vector3.Distance(v.transform.position, gc.Player.transform.position))
                .First();
            gc.Player.Teleport(branches.transform.position + new Vector3(1.2f, 0.2f, 0f));
            yield return null;
            gc.HarvestTarget(branches);
            Assert.Greater(gc.Session.Player.Inventory.Count("wood"), 0);
        }

        [UnityTest]
        public IEnumerator SaveAndLoad_RestoresPlayerAndInventory()
        {
            yield return StartGame(4325);
            GameController gc = Game;
            gc.Session.Player.Inventory.TryAdd("stone", 7);
            Vector3 target = gc.Player.transform.position + new Vector3(5f, 0f, 3f);
            target.y = gc.GroundHeight(target) + 0.1f;
            gc.Player.Teleport(target);
            yield return null;
            yield return null;
            gc.Save("playmode_test");
            GameBootstrap boot = Object.FindAnyObjectByType<GameBootstrap>();
            boot.LoadGame("playmode_test");
            float timeout = Time.realtimeSinceStartup + 90f;
            while ((boot.Game == null || boot.Game == gc || !boot.Game.Running) && Time.realtimeSinceStartup < timeout) yield return null;
            GameController loaded = boot.Game;
            Assert.AreNotSame(gc, loaded);
            Assert.AreEqual(7, loaded.Session.Player.Inventory.Count("stone"));
            Assert.Less(Vector3.Distance(loaded.Player.transform.position, target), 1.0f);
            boot.Saves.Store.Delete("playmode_test");
        }
    }
}
