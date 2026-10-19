#nullable disable
using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GreenScreen
{
    // ExecutePass is shared by URP's Render Graph and compatibility paths.
    // Its opaque postfix still has the game's colour/depth/MSAA attachments
    // and XR instance multiplier bound. Never blit an eye or sample old depth.
    internal static class SrpOpaqueCoverage
    {
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static HarmonyLib.Harmony _patches;
        private static MethodInfo _target, _draw;
        private static Func<object, object> _opaque, _cameraData, _camera;
        private static bool _reported;

        internal static bool Prepare(out string reason)
        {
            if (_patches != null) { reason = null; return true; }
            try
            {
                Type pass = null;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    pass = assembly.GetType("UnityEngine.Rendering.Universal.Internal.DrawObjectsPass")
                        ?? assembly.GetType("Il2CppUnityEngine.Rendering.Universal.Internal.DrawObjectsPass");
                    if (pass != null) break;
                }
                _target = pass?.GetMethod("ExecutePass", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                ParameterInfo[] parameters = _target?.GetParameters();
                if (parameters == null || parameters.Length != 5 || _target.ReturnType != typeof(void))
                    throw new MissingMethodException("URP DrawObjectsPass.ExecutePass");
                Type commands = parameters[0].ParameterType, data = parameters[1].ParameterType;
                if (!commands.FullName.EndsWith("UnityEngine.Rendering.RasterCommandBuffer", StringComparison.Ordinal))
                    throw new MissingMethodException("URP raster command buffer");
                _opaque = Reader(data, "isOpaque", out Type opaqueType);
                _cameraData = Reader(data, "cameraData", out Type cameraDataType);
                _camera = Reader(cameraDataType, "camera", out Type cameraType);
                if (opaqueType != typeof(bool) || cameraType != typeof(Camera))
                    throw new MissingMemberException("URP opaque camera data");
                _draw = commands.GetMethod("DrawProcedural", new[] {
                    typeof(Matrix4x4), typeof(Material), typeof(int), typeof(MeshTopology), typeof(int) });
                if (_draw == null) throw new MissingMethodException("URP raster DrawProcedural");
                var patches = new HarmonyLib.Harmony("McFredward.DemeoGreenScreen.OpaqueCoverage");
                patches.Patch(_target, postfix: new HarmonyMethod(typeof(SrpOpaqueCoverage).GetMethod(
                    nameof(AfterOpaque), BindingFlags.NonPublic | BindingFlags.Static)));
                _patches = patches;
                reason = null;
                return true;
            }
            catch (Exception error)
            {
                reason = "URP opaque depth coverage is unavailable: " + error.Message;
                return false;
            }
        }

        // Mono exposes fields; the generated IL2CPP assemblies expose properties.
        private static Func<object, object> Reader(Type type, string name, out Type valueType)
        {
            FieldInfo field = type.GetField(name, Members);
            if (field != null) { valueType = field.FieldType; return field.GetValue; }
            PropertyInfo property = type.GetProperty(name, Members);
            if (property == null || !property.CanRead) throw new MissingMemberException(type.FullName, name);
            valueType = property.PropertyType;
            return value => property.GetValue(value);
        }

        private static void AfterOpaque(object __0, object __1)
        {
            try
            {
                if (!(_opaque(__1) is bool opaque) || !opaque) return;
                Camera camera = _camera(_cameraData(__1)) as Camera;
                NativePassthrough.RecordSrpCoverage(camera, __0);
            }
            catch (Exception error) { NativePassthrough.CoverageFailed(error); }
        }

        internal static void Record(object commands, Material material)
        {
            // Unity's XR instance multiplier supplies both array slices. An
            // explicit second instance here would multiply the eye count again.
            object[] arguments = { Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3 };
            _draw.Invoke(commands, arguments);
            arguments[2] = 1;
            _draw.Invoke(commands, arguments);
            if (_reported) return;
            _reported = true;
            Utils.LogInfo("URP opaque depth coverage is active. Alpha is repaired on the current eye attachments before transparents; the game's XR layout and MSAA are retained.");
        }

        internal static void Shutdown()
        {
            _patches?.Unpatch(_target, typeof(SrpOpaqueCoverage).GetMethod(nameof(AfterOpaque), BindingFlags.NonPublic | BindingFlags.Static));
            _patches = null; _target = _draw = null;
            _opaque = _cameraData = _camera = null;
            _reported = false;
        }
    }
}
