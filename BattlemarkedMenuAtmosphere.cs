#nullable disable
#if DEMEOBATTLEMARKED
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GreenScreen
{
    // The shipped menu has two EnvFX/FogSprite renderers and separate smoke
    // children under Static/LightProps. Also hide only the longest authored
    // EnvFX godray. Parent effects, shorter rays, flames and lights stay live.
    internal static class BattlemarkedMenuAtmosphere
    {
        private static readonly List<Renderer> Hidden = new List<Renderer>();
        private static GameObject _menu;
        private static float _next;
        private static int _reports;
        private static bool _failed;

        internal static void Invalidate() => _next = 0;

        internal static void Tick()
        {
            if (_failed || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1;
            try
            {
                var menu = GameObject.Find("MainMenuSceneView");
                if (_menu != menu) { Restore(); _menu = menu; }
                if (menu == null) { Restore(); _menu = null; return; }
                for (int i = Hidden.Count - 1; i >= 0; i--)
                    if (Hidden[i] == null) Hidden.RemoveAt(i);
                    else Hidden[i].enabled = false;

                int fog = 0, smoke = 0, longBeam = 0;
                var menuScene = menu.scene;
                foreach (var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
                {
                    if (renderer == null || !renderer.enabled) continue;
                    Transform item = renderer.transform;
                    bool isFog = item.name == "FogSprite" && item.parent != null
                        && item.parent.name == "EnvFX" && item.parent.parent == null;
                    bool isSmoke = TorchSmoke(item);
                    bool isLongBeam = item.name == "Godray" && item.parent != null
                        && item.parent.name == "Godray_Base (3)" && item.parent.parent != null
                        && item.parent.parent.name == "EnvFX" && item.parent.parent.parent == null;
                    if ((!isFog && !isSmoke && !isLongBeam) || Hidden.Count >= 64 || renderer.gameObject.scene != menuScene) continue;
                    renderer.enabled = false;
                    Hidden.Add(renderer);
                    if (isFog) fog++; else if (isSmoke) smoke++; else longBeam++;
                }
                if (fog + smoke + longBeam > 0 && _reports++ < 8)
                    Utils.LogDebug("Battlemarked menu atmosphere hidden: fog=" + fog + "; smoke=" + smoke
                        + "; long-beam=" + longBeam
                        + ". Only renderers in the MainMenuSceneView scene; shorter beams, flames, lights and scenario effects retained.");
            }
            catch (Exception error)
            {
                Restore(); _failed = true;
                Utils.LogWarning("Battlemarked menu atmosphere could not be hidden: " + error.GetType().Name + ": " + error.Message);
            }
        }

        private static bool TorchSmoke(Transform item)
        {
            if (item == null || item.name != "Smoke" && !item.name.StartsWith("Smoke (", StringComparison.Ordinal)) return false;
            Transform effect = item.parent;
            if (effect == null || effect.name != "PStatusFire01" && effect.name != "PFireTorch"
                && !effect.name.StartsWith("PFireTorch (", StringComparison.Ordinal)) return false;
            Transform props = effect.parent?.parent;
            return props != null && props.name == "LightProps" && props.parent != null
                && props.parent.name == "Static" && props.parent.parent == null;
        }

        private static void Restore()
        {
            // Only renderers actually changed from enabled to disabled belong
            // to this lease. Originally hidden particles stay hidden.
            foreach (Renderer renderer in Hidden)
                if (renderer != null && !renderer.enabled) renderer.enabled = true;
            Hidden.Clear();
        }

        internal static void Shutdown()
        {
            Restore(); _menu = null; _next = 0; _reports = 0; _failed = false;
        }
    }
}
#endif
