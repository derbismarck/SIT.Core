using BepInEx.Logging;
using SIT.Coop.Core.LocalGame;
using SIT.Coop.Core.Matchmaker;
using SIT.Coop.Core.Player;
using SIT.Core.Coop.LocalGame;
using SIT.Core.Coop.Player.GrenadeControllerPatches;
using SIT.Core.Coop.Sounds;
using SIT.Core.Coop.World;
using SIT.Tarkov.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SIT.Core.Coop
{
    internal class CoopPatches
    {
        public static ManualLogSource Logger { get; private set; }

        private static BepInEx.Configuration.ConfigFile m_Config;

        public static void Run(BepInEx.Configuration.ConfigFile config)
        {
            m_Config = config;

            if (Logger == null)
                Logger = BepInEx.Logging.Logger.CreateLogSource("Coop");

            var enabled = config.Bind<bool>("Coop", "Enable", true);
            if (!enabled.Value) // if it is disabled. stop all Coop stuff.
            {
                Logger.LogInfo("Coop has been disabled! Ignoring Patches.");
                return;
            }

            SceneManager.sceneLoaded += SceneManager_sceneLoaded;

            new TarkovApplication_LocalGameCreator_Patch().Enable();
            new LocalGameStartingPatch(m_Config).Enable();
            //new LocalGameEndingPatch(m_Config).Enable();
            //new LocalGameSpawnAICoroutinePatch().Enable(); // No longer needed. Handled by CoopGame
            new NonWaveSpawnScenarioPatch(m_Config).Enable();
            new WaveSpawnScenarioPatch(m_Config).Enable();
            new LocalGame_Weather_Patch().Enable();


            // ------ MATCHMAKER -------------------------
            MatchmakerAcceptPatches.Run();

        }

        private static void SceneManager_sceneLoaded(Scene arg0, LoadSceneMode arg1)
        {
            //if (Singleton<GameWorld>.Instantiated)
            //    EnableDisablePatches();
        }

        public static List<ModulePatch> NoMRPPatches { get; } = new List<ModulePatch>();

        public static GameObject CoopGameComponentParent { get; internal set; }

        public static void EnableDisablePatches()
        {
            var enablePatches = true;

            var coopGC = CoopGameComponent.GetCoopGameComponent();
            if (coopGC == null)
            {
                Logger.LogDebug($"CoopPatches:CoopGameComponent is null, Patches wont be Applied");
                enablePatches = false;
            }

            if (coopGC != null && !coopGC.enabled)
            {
                Logger.LogDebug($"CoopPatches:CoopGameComponent is not enabled, Patches wont be Applied");
                enablePatches = false;
            }

            if (string.IsNullOrEmpty(CoopGameComponent.GetServerId()))
            {
                Logger.LogDebug($"CoopPatches:CoopGameComponent ServerId is not set, Patches wont be Applied");
                enablePatches = false;
            }

            // ------ PLAYER -------------------------
            if (!NoMRPPatches.Any())
            {
                NoMRPPatches.Add(new Player_Init_Coop_Patch(m_Config));
                NoMRPPatches.Add(new WeaponSoundPlayer_FireSonicSound_Patch());
                NoMRPPatches.Add(new Player_LeavingGame_Patch());
                NoMRPPatches.Add(new Door_Interact_Patch());
                NoMRPPatches.Add(new Switch_Interact_Patch());
                NoMRPPatches.Add(new WorldInteractiveObject_Interact_Patch());
            }

            //Logger.LogInfo($"{NoMRPPatches.Count()} Non-MR Patches found");
            foreach (var patch in NoMRPPatches)
            {
                if (enablePatches)
                    patch.Enable();
                else
                    patch.Disable();
            }

            var moduleReplicationPatches = Assembly.GetAssembly(typeof(ModuleReplicationPatch)).GetTypes().Where(x => x.GetInterface("IModuleReplicationPatch") != null);
            ////Logger.LogInfo($"{moduleReplicationPatches.Count()} Module Replication Patches found");
            foreach (var module in moduleReplicationPatches)
            {
                if (module.IsAbstract
                    || module == typeof(ModuleReplicationPatch)
                    || module.Name.Contains(typeof(ModuleReplicationPatch).Name)
                    )
                    continue;

                ModuleReplicationPatch mrp = null;
                if (!ModuleReplicationPatch.Patches.Any(x => x.GetType() == module))
                {
                    mrp = (ModuleReplicationPatch)Activator.CreateInstance(module);
                    if (mrp.DisablePatch)
                    {
                        mrp = null;
                    }
                }
                else
                    mrp = ModuleReplicationPatch.Patches.SingleOrDefault(x => x.GetType() == module);

                if (mrp == null)
                    continue;

                if (!mrp.DisablePatch && enablePatches)
                {
                    //Logger.LogInfo($"Enabled {mrp.GetType()}");
                    mrp.Enable();
                }
                else
                    mrp.Disable();
            }
        }
    }
}
