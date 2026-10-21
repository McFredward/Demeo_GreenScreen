#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GreenScreen
{
    // Observe the actual game fade, retaining its timing, state and callbacks.
    // Only the identified fullscreen renderer receives an owned RGB/alpha fade.
    internal static class PassthroughFade
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly List<Fader> Faders = new List<Fader>();
        private static readonly List<MethodInfo> Targets = new List<MethodInfo>();
#if !DEMEOBATTLEMARKED
        private static readonly List<TimelineFader> TimelineFaders = new List<TimelineFader>();
        private static MethodInfo _introAwake;
        private static MethodInfo IntroObserver => typeof(PassthroughFade).GetMethod(nameof(AfterIntroAwake), BindingFlags.NonPublic | BindingFlags.Static);
#endif
        private static HarmonyLib.Harmony _patch;
        private static Func<object, object> _view, _material, _renderer;
#if DEMEOBATTLEMARKED
        private static Func<object, object> _alternate;
#endif
        private static AssetBundle _bundle;
        private static Shader _shader;
        private static bool _active, _attempted, _reported, _failed;
        private static float _nextBind;

        internal static void Initialize()
        {
            if (_patch != null) return;
            try
            {
#if DEMEOBATTLEMARKED
                Type controller = FindType("GameView", "Jade.GameView.FadeToBlackPresenter");
                if (controller == null) return;
                _view = Reader(controller, "view", out Type view);
                _renderer = Reader(view, "FadeCubeRenderer", out Type renderer);
                _material = Reader(view, "BlackFadeMaterial", out Type material);
                _alternate = Reader(view, "TransparentFadeMaterial", out Type alternate);
                if (renderer != typeof(MeshRenderer) || material != typeof(Material) || alternate != typeof(Material))
                    throw new MissingMemberException("Battlemarked fade renderer/material contract");
                AddTarget(controller, "SetFadePlaneColor", new[] { typeof(float), typeof(float), typeof(float), typeof(float) });
#else
                Type controller = FindType("Assembly-CSharp", "Boardgame.Rendering.CameraFadeController");
                if (controller == null) return;
                _view = Reader(controller, "cameraFadeView", out Type view);
                _renderer = Reader(view, "faderMeshRenderer", out Type renderer);
                _material = Reader(controller, "fadeMaterial", out Type material);
                if (renderer != typeof(MeshRenderer) || material != typeof(Material))
                    throw new MissingMemberException("Demeo/Battles fade renderer/material contract");
                AddTarget(controller, "OnUpdate", new[] { typeof(float) });
                AddTarget(controller, "SetFadedOut", Type.EmptyTypes);
#endif
                _patch = new HarmonyLib.Harmony("McFredward.DemeoGreenScreen.PassthroughFade");
                foreach (MethodInfo target in Targets)
                    _patch.Patch(target, postfix: new HarmonyMethod(Observer));
                Utils.LogInfo("Passthrough fade observer ready: " + controller.FullName + ".");
#if !DEMEOBATTLEMARKED
                // The two proven intro Timeline spheres have their own animated
                // URP material; CameraFadeController never owns these renderers.
                try
                {
                    _introAwake = FindType("Assembly-CSharp", "Boardgame.Intro.IntroController")?.GetMethod("Awake", Members, null, Type.EmptyTypes, null);
                    if (_introAwake != null && _introAwake.ReturnType == typeof(void))
                        _patch.Patch(_introAwake, postfix: new HarmonyMethod(IntroObserver));
                }
                catch (Exception error) { Utils.LogInfo("Intro timeline fade binding unavailable; retaining original timeline material: " + error.GetType().Name + "."); }
#endif
            }
            catch (Exception error)
            {
                RemoveHooks();
                Warn(error);
            }
        }

        internal static void Tick(float now)
        {
            if (_patch != null || _failed || now < _nextBind) return;
            _nextBind = now + 1f;
            Initialize();
        }

        private static Type FindType(string assemblyName, string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == assemblyName || assembly.GetName().Name == "Il2Cpp" + assemblyName)
                {
                    Type type = assembly.GetType(name) ?? assembly.GetType("Il2Cpp" + name);
                    if (type != null) return type;
                }
            // Interop assemblies may be resolved lazily by either loader.
            foreach (string candidate in new[] { assemblyName, "Il2Cpp" + assemblyName })
            {
                try
                {
                    Assembly assembly = Assembly.Load(new AssemblyName(candidate));
                    Type type = assembly.GetType(name) ?? assembly.GetType("Il2Cpp" + name);
                    if (type != null) return type;
                }
                catch (FileNotFoundException) { }
                catch (FileLoadException) { }
            }
            return null;
        }

        // Mono fields and generated IL2CPP properties represent the same member.
        private static Func<object, object> Reader(Type type, string name, out Type valueType)
        {
            FieldInfo field = type.GetField(name, Members);
            if (field != null) { valueType = field.FieldType; return field.GetValue; }
            PropertyInfo property = type.GetProperty(name, Members);
            if (property == null || !property.CanRead) throw new MissingMemberException(type.FullName, name);
            valueType = property.PropertyType;
            return owner => property.GetValue(owner);
        }

        private static void AddTarget(Type type, string name, Type[] parameters)
        {
            MethodInfo method = type.GetMethod(name, Members, null, parameters, null);
            if (method == null || method.ReturnType != typeof(void)) throw new MissingMethodException(type.FullName, name);
            Targets.Add(method);
        }

        private static MethodInfo Observer => typeof(PassthroughFade).GetMethod(nameof(AfterGameFade), BindingFlags.NonPublic | BindingFlags.Static);
#if DEMEOBATTLEMARKED
        private static void AfterGameFade(object __instance, float __3) => Observe(__instance, __3);
#else
        private static void AfterGameFade(object __instance) => Observe(__instance, 0);
#endif
        private static void Observe(object controller, float alpha)
        {
            try
            {
                object view = _view(controller);
                var renderer = _renderer(view) as MeshRenderer;
#if DEMEOBATTLEMARKED
                var material = _material(view) as Material;
                var alternate = _alternate(view) as Material;
#else
                var material = _material(controller) as Material;
                Material alternate = null;
                if (material == null || !material.HasProperty("_Color")) return;
#endif
                if (renderer == null || material == null) return;
                for (int i = Faders.Count - 1; i >= 0; i--)
                {
                    if (Faders[i].Dead) { Faders[i].Restore(); Faders.RemoveAt(i); }
                    else if (Faders[i].Renderer == renderer)
                    {
                        Faders[i].Alpha = alpha;
                        Refresh();
                        return;
                    }
                }
                var slots = renderer.sharedMaterials;
                if (slots.Length != 1 || (slots[0] != material && slots[0] != alternate) || Faders.Count >= 16) return;
                Faders.Add(new Fader(renderer, material, alternate) { Alpha = alpha });
                Refresh();
            }
            catch (Exception error) { Warn(error); }
        }

        internal static void SetActive(bool active) { _active = active; Refresh(); }

        // Refresh before the admitted camera renders, after game tween updates.
        internal static void Refresh()
        {
#if !DEMEOBATTLEMARKED
            for (int i = TimelineFaders.Count - 1; i >= 0; i--)
            {
                TimelineFader fader = TimelineFaders[i];
                try
                {
                    if (fader.Dead) { fader.Restore(); TimelineFaders.RemoveAt(i); }
                    else if (!_active || _failed) fader.Restore();
                    else fader.Apply();
                }
                catch (Exception error) { fader.Restore(); Warn(error); }
            }
#endif
            for (int i = Faders.Count - 1; i >= 0; i--)
            {
                Fader fader = Faders[i];
                try
                {
                    if (fader.Dead) { fader.Restore(); Faders.RemoveAt(i); }
                    else if (!_active || _failed) fader.Restore();
                    else if (LoadShader()) fader.Apply(_shader);
                }
                catch (Exception error) { fader.Restore(); Warn(error); }
            }
        }

        private static bool LoadShader()
        {
            if (_shader != null) return true;
            if (_attempted) return false;
            _attempted = true;
            using (Stream resource = typeof(PassthroughFade).Assembly.GetManifestResourceStream("GreenScreen.NativeFade"))
            {
                if (resource == null) throw new InvalidDataException("The embedded passthrough fade is missing.");
                using (var data = new MemoryStream())
                {
                    resource.CopyTo(data);
                    _bundle = UnityShaderLoader.LoadBundle(data.ToArray());
                }
            }
            if (_bundle == null) throw new InvalidDataException("Unity rejected the passthrough fade bundle.");
            _shader = UnityShaderLoader.LoadShader(_bundle, "assets/nativefade.shader");
            if (_shader == null || _shader.name != "Hidden/Demeo/NativeFade" || !_shader.isSupported)
                throw new NotSupportedException("The passthrough fade shader is unavailable.");
            return true;
        }

        private static void Warn(Exception error)
        {
            if (_failed) return;
            _failed = true;
            Utils.LogInfo("Passthrough fade unavailable; retaining the game's original fades: " + error.GetType().Name + ": " + error.Message);
        }

        private static void RemoveHooks()
        {
            if (_patch != null) foreach (MethodInfo target in Targets) _patch.Unpatch(target, Observer);
#if !DEMEOBATTLEMARKED
            if (_patch != null && _introAwake != null) _patch.Unpatch(_introAwake, IntroObserver);
            _introAwake = null;
#endif
            _patch = null;
            Targets.Clear();
        }

        internal static void Shutdown()
        {
            SetActive(false);
            Faders.Clear();
#if !DEMEOBATTLEMARKED
            TimelineFaders.Clear();
#endif
            RemoveHooks();
            _view = _material = _renderer = null;
#if DEMEOBATTLEMARKED
            _alternate = null;
#endif
            _shader = null;
            if (_bundle != null) _bundle.Unload(true);
            _bundle = null;
            _attempted = _reported = _failed = false;
            _nextBind = 0;
        }

#if !DEMEOBATTLEMARKED
        private static void AfterIntroAwake(object __instance)
        {
            try
            {
                if (!(__instance is Component controller)) return;
                foreach (string path in new[] { "Intro/invert_sphere", "Intro/invert_sphere_end" })
                {
                    MeshRenderer renderer = controller.transform.Find(path)?.GetComponent<MeshRenderer>();
                    if (renderer == null || TimelineFaders.Count >= 8) continue;
                    bool known = false;
                    foreach (TimelineFader fader in TimelineFaders) known |= fader.Renderer == renderer;
                    if (known) continue;
                    Material[] slots = renderer.sharedMaterials;
                    if (slots.Length != 1 || !TimelineFader.Compatible(slots[0])) continue;
                    TimelineFaders.Add(new TimelineFader(renderer, slots[0]));
                    Utils.LogInfo("Intro timeline passthrough fade bound: " + path + "; animated URP BaseColor and original geometry retained.");
                }
                Refresh();
            }
            catch (Exception error) { Utils.LogInfo("Intro timeline fade discovery unavailable: " + error.GetType().Name + "."); }
        }

        private sealed class TimelineFader
        {
            internal readonly MeshRenderer Renderer;
            private readonly Material _original;
            private Material _owned;
            internal bool Dead => Renderer == null || _original == null;
            internal TimelineFader(MeshRenderer renderer, Material original) { Renderer = renderer; _original = original; }
            internal static bool Compatible(Material material)
            {
                if (material == null || material.shader == null || material.shader.name != "Universal Render Pipeline/Unlit"
                    || material.renderQueue < 3000 || material.renderQueue >= 4000) return false;
                foreach (string property in new[] { "_BaseColor", "_SrcBlend", "_DstBlend", "_SrcBlendAlpha", "_DstBlendAlpha", "_Surface", "_ZWrite" })
                    if (!material.HasProperty(property)) return false;
                return material.GetFloat("_Surface") == 1 && material.GetFloat("_ZWrite") == 0;
            }
            internal void Apply()
            {
                Material current = Renderer.sharedMaterial;
                if (current != _original && current != _owned) return;
                if (_owned == null)
                {
                    // Keep URP's animation properties, keywords, alpha calculation,
                    // culling, stencil and draw order. Only RGB/alpha blend factors
                    // change to Zero / OneMinusSrcAlpha. Timeline property blocks
                    // continue to supply exact opacity directly on the GPU.
                    _owned = new Material(_original) { hideFlags = HideFlags.HideAndDontSave };
                    _owned.SetFloat("_SrcBlend", 0);
                    _owned.SetFloat("_SrcBlendAlpha", 0);
                    _owned.SetFloat("_DstBlend", 10);
                    _owned.SetFloat("_DstBlendAlpha", 10);
                }
                Renderer.sharedMaterial = _owned;
            }
            internal void Restore()
            {
                if (_owned == null) return;
                if (Renderer != null && Renderer.sharedMaterial == _owned) Renderer.sharedMaterial = _original;
                UnityEngine.Object.Destroy(_owned); _owned = null;
            }
        }
#endif

        private sealed class Fader
        {
            internal readonly MeshRenderer Renderer;
            private readonly Material _primary, _alternate;
            private Material _original, _owned;
            internal float Alpha;
            internal bool Dead => Renderer == null || _primary == null;
            internal Fader(MeshRenderer renderer, Material primary, Material alternate)
            { Renderer = renderer; _primary = primary; _alternate = alternate; }

            internal void Apply(Shader shader)
            {
                Material current = Renderer.sharedMaterial;
                // Follow only the game's proven material choices. An unrelated
                // replacement by another owner is never overwritten/restored.
                if (current != _owned && current != _primary && (current == null || current != _alternate)) return;
                if (current != _owned) _original = current;
                if (_owned == null)
                {
                    var owned = new Material(shader) { hideFlags = HideFlags.HideAndDontSave,
                        enableInstancing = true, renderQueue = _original.renderQueue };
                    if (owned.passCount != 1 || owned.FindPass("ROOM_FADE") != 0)
                    {
                        UnityEngine.Object.Destroy(owned);
                        throw new InvalidDataException("Unexpected passthrough fade shader pass.");
                    }
                    _owned = owned;
                }
#if DEMEOBATTLEMARKED
                // The presenter writes a MaterialPropertyBlock, not material.color.
                // Its exact SetFadePlaneColor alpha is the semantic fade value.
                float alpha = Alpha;
#else
                float alpha = _primary.color.a;
#endif
                if (float.IsNaN(alpha) || float.IsInfinity(alpha)) { Restore(); return; }
                // Use a distinct property so existing _Color/_BaseColor blocks
                // cannot override the game's independently observed opacity.
                _owned.renderQueue = _original.renderQueue;
                _owned.SetFloat("_PassthroughFade", alpha);
                Renderer.sharedMaterial = _owned;
                if (_reported) return;
                _reported = true;
                Utils.LogInfo("Game black fades now fade into passthrough. Original fade timing and loading callbacks are retained.");
            }

            internal void Restore()
            {
                if (_owned == null) return;
                if (Renderer != null && Renderer.sharedMaterial == _owned) Renderer.sharedMaterial = _original;
                UnityEngine.Object.Destroy(_owned);
                _owned = null;
                _original = null;
            }
        }
    }
}
