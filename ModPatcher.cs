namespace GreenScreen
{
    using Boardgame;
    using Boardgame.NonVR;
    using Boardgame.NonVR.Ui;
    using Boardgame.Ui.LobbyMenu;
    using HarmonyLib;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.SceneManagement;

    internal static class ModPatcher
    {

        internal static void Patch(Harmony harmony)
        {
            harmony.Patch(
            original: typeof(Lobby).GetMethod("Init"),
            prefix: new HarmonyMethod(typeof(ModPatcher), nameof(Init_Prefix)));

            
            GreenScreenBase.LogDebug("Patched");
        }

        private static void ChangeCamBackground()
        {
            GreenScreenBase.LogDebug("Start FindCamerInScene");
            Camera[] camerasInScene = GameObject.FindObjectsOfType<Camera>();
            GreenScreenBase.LogDebug("After FindObjectsOfType");
            //Color vd_green = new Color(0f, 152f/255f, 51f/255f); //Default green value in Virtual Desktop
            Color green = new Color(0f, 1f, 0f); //Works best with Similarity 37% & Smootheness 5%
            foreach (Camera camera in camerasInScene)
            {
                camera.backgroundColor = green;
            }
        }
        //Harmony Patches

        private static void Init_Prefix(
            GameContext gameContext,
            GameObject lobbyGUI,
            BehindBackLobbyGUI behindBackLobbyGUI,
            LobbyMenuController lobbyMenuController)
        {
            GreenScreenBase.LogDebug("Change Background to green");
            ChangeCamBackground();
        }
    }
}
