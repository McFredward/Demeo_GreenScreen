#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GreenScreen
{
    // Loading canvases have their own black UI image, after the camera fade.
    // Change only its colour alpha, preserving the live component, raycasts,
    // parent CanvasGroup animation, hints and completion callbacks.
    internal static class LoadingPassthrough
    {
#if !DEMEOBATTLEMARKED
        private static HarmonyLib.Harmony _patch;
        private static readonly List<MethodInfo> Hooks = new List<MethodInfo>();
        private static readonly List<ImageLease> Images = new List<ImageLease>();
        private static readonly Dictionary<Type, Func<object, object>> Presenters = new Dictionary<Type, Func<object, object>>();
        private static MethodInfo _image;
        private static PropertyInfo _colour, _sprite;
        private static float _next;
        private static bool _active, _failed, _reported;
        private static readonly BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static MethodInfo Observer(string name) => typeof(LoadingPassthrough).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);

        internal static void Initialize()
        {
            if (_patch != null || _failed) return;
            try
            {
                Type imageType = null;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    if ((imageType = assembly.GetType("UnityEngine.UI.Image") ?? assembly.GetType("Il2CppUnityEngine.UI.Image")) != null) break;
                imageType ??= PassthroughFade.FindType("UnityEngine.UI", "UnityEngine.UI.Image");
                if (imageType == null) return; // Interop can still be loading.
                _colour = imageType.GetProperty("color");
                _sprite = imageType.GetProperty("sprite");
                if (_colour?.PropertyType != typeof(Color) || !_colour.CanRead || !_colour.CanWrite
                    || _sprite?.CanRead != true || !typeof(UnityEngine.Object).IsAssignableFrom(_sprite.PropertyType))
                    throw new MissingMemberException("Loading Image colour/sprite contract");
                foreach (MethodInfo method in typeof(Transform).GetMethods())
                    if (method.Name == "GetComponent" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
                    { _image = method.MakeGenericMethod(imageType); break; }
                if (_image == null) throw new MissingMethodException("Loading Image GetComponent");
                _patch = new HarmonyLib.Harmony("McFredward.DemeoGreenScreen.LoadingBackground");
                foreach (string name in new[] { "PVPLoadingScreen", "VanillaLoadingScreen" })
                {
                    Type view = PassthroughFade.FindType("Assembly-CSharp", name + "View");
                    if (view == null) continue;
                    Add(view, "Awake", Type.EmptyTypes, nameof(AfterView));
                    Add(view, "OnEnable", Type.EmptyTypes, nameof(AfterView));
                    Type presenter = PassthroughFade.FindType("Assembly-CSharp", name + "Presenter");
                    if (presenter == null) continue;
                    Presenters.Add(presenter, ViewReader(presenter));
                    Add(presenter, "Show", new[] { typeof(bool) }, nameof(AfterShow));
                }
                if (Hooks.Count == 0) { RemoveHooks(); return; }
                Utils.LogInfo("Loading-screen passthrough observer ready: exact PVP/Vanilla Background UI images; original loading logic retained.");
            }
            catch (Exception error)
            {
                RemoveHooks(); _failed = true;
                Utils.LogInfo("Loading-screen passthrough unavailable; retaining original UI: " + error.GetType().Name + ": " + error.Message);
            }
        }

        private static Func<object, object> ViewReader(Type type)
        {
            FieldInfo field = type.GetField("view", Members);
            if (field != null) return field.GetValue;
            PropertyInfo property = type.GetProperty("view", Members);
            if (property?.CanRead != true) throw new MissingMemberException(type.FullName, "view");
            return value => property.GetValue(value);
        }

        private static void Add(Type type, string name, Type[] parameters, string observer)
        {
            MethodInfo method = type.GetMethod(name, Members, null, parameters, null);
            if (method == null || method.ReturnType != typeof(void)) throw new MissingMethodException(type.FullName, name);
            _patch.Patch(method, postfix: new HarmonyMethod(Observer(observer)));
            Hooks.Add(method);
        }

        internal static void Tick(float now)
        {
            if (_patch != null || _failed || now < _next) return;
            _next = now + 1f; Initialize();
        }

        private static void AfterShow(object __instance)
        {
            try
            {
                foreach (var presenter in Presenters)
                    if (presenter.Key.IsInstanceOfType(__instance)) { AfterView(presenter.Value(__instance)); return; }
            }
            catch (Exception error) { Warn(error); }
        }

        private static void AfterView(object __instance)
        {
            try
            {
                var view = __instance as Component;
                Transform background = view?.transform?.Find("Background");
                if (background == null) return;
                var image = _image.Invoke(background, null) as UnityEngine.Object;
                if (image == null) return;
                for (int i = Images.Count - 1; i >= 0; i--)
                {
                    if (Images[i].Dead) Images.RemoveAt(i);
                    else if (Images[i].Image == image) { Refresh(_active); return; }
                }
                if (Images.Count >= 16) return;
                Images.Add(new ImageLease(image));
                Refresh(_active);
            }
            catch (Exception error) { Warn(error); }
        }

        internal static void Refresh(bool active)
        {
            _active = active;
            foreach (ImageLease image in Images)
                try { if (active && !_failed) image.Apply(); else image.Restore(); }
                catch (Exception error) { Warn(error); }
        }

        private static void Warn(Exception error)
        {
            if (_failed) return;
            _failed = true;
            Utils.LogInfo("Loading-screen passthrough stopped; retaining original UI: " + error.GetType().Name + ": " + error.Message);
            foreach (ImageLease image in Images) try { image.Restore(); } catch { }
        }

        private sealed class ImageLease
        {
            internal readonly UnityEngine.Object Image;
            internal bool Dead => Image == null;
            private Color _original, _written;
            private bool _owned;
            internal ImageLease(UnityEngine.Object image) { Image = image; }
            private static bool Same(Color a, Color b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
            internal void Apply()
            {
                if (Dead) return;
                Color current = (Color)_colour.GetValue(Image);
                if ((_sprite.GetValue(Image) as UnityEngine.Object) != null) { Restore(); return; }
                if (_owned && Same(current, _written)) return;
                _owned = false; // A changed game/foreign value owns the current colour.
                // The supplied Background is an untextured black Image. Never
                // hide loading artwork, a coloured replacement or other UI.
                if (current.r != 0 || current.g != 0 || current.b != 0 || !float.IsFinite(current.a) || current.a <= 0 || current.a > 1) return;
                _original = current; _written = current; _written.a = 0;
                _colour.SetValue(Image, _written); _owned = true;
                if (_reported) return;
                _reported = true;
                Utils.LogInfo("Black loading Background now reveals passthrough; UI raycasts, hints, spinner and loading callbacks are retained.");
            }
            internal void Restore()
            {
                if (!_owned) return;
                if (!Dead && Same((Color)_colour.GetValue(Image), _written)) _colour.SetValue(Image, _original);
                _owned = false;
            }
        }

        private static void RemoveHooks()
        {
            if (_patch != null)
                foreach (MethodInfo method in Hooks)
                    _patch.Unpatch(method, Observer(method.Name == "Show" ? nameof(AfterShow) : nameof(AfterView)));
            Hooks.Clear(); Presenters.Clear(); _patch = null;
        }

        internal static void Shutdown()
        {
            Refresh(false); RemoveHooks(); Images.Clear();
            _image = null; _colour = _sprite = null; _next = 0; _failed = _reported = _active = false;
        }
#else
        // Battlemarked routes its level/menu/cutscene/bounds fades through the
        // already observed FadeToBlackPresenter. No PVP/Vanilla UI assumption.
        internal static void Initialize() { }
        internal static void Tick(float now) { }
        internal static void Refresh(bool active) { }
        internal static void Shutdown() { }
#endif
    }
}
