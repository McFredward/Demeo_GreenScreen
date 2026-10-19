#nullable disable
using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace GreenScreen
{
    // The renderer's current colour/depth/MSAA attachments stay bound.
    // Only alpha is written. Transparent game shaders then contribute their
    // native alpha in draw order; they must use correct separate alpha blending.
    internal sealed class OpaqueCoverage : IDisposable
    {
        private static AssetBundle _bundle;
        private static Shader _shader;
        private static bool _attempted;
        private readonly Camera _camera;
        private readonly Material _material;
        private readonly CommandBuffer _commands;

        private OpaqueCoverage(Camera camera, Shader shader, bool srp)
        {
            _camera = camera;
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            if (_material.passCount != 2 || _material.FindPass("RESET") != 0 || _material.FindPass("STAMP") != 1)
            {
                UnityEngine.Object.Destroy(_material);
                throw new InvalidDataException("Unexpected coverage shader passes.");
            }
            if (srp) return; // URP records inside its opaque raster pass, including Render Graph.
            _commands = new CommandBuffer { name = "Demeo.NativePassthrough.OpaqueCoverage" };
            try
            {
                _commands.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3);
                _commands.DrawProcedural(Matrix4x4.identity, _material, 1, MeshTopology.Triangles, 3);
                camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _commands);
            }
            catch
            {
                camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, _commands);
                _commands.Release();
                UnityEngine.Object.Destroy(_material);
                throw;
            }
        }

        internal static OpaqueCoverage TryCreate(Camera camera, bool srp = false)
        {
            try
            {
                if (_shader == null)
                {
                    if (_attempted) return null;
                    _attempted = true;
                    using (Stream resource = typeof(OpaqueCoverage).Assembly.GetManifestResourceStream("GreenScreen.NativeCoverage"))
                    {
                        if (resource == null) return null;
                        using (var data = new MemoryStream())
                        {
                            resource.CopyTo(data);
                            _bundle = AssetBundle.LoadFromMemory(data.ToArray());
                        }
                    }
                    if (_bundle == null) return null;
#if DEMEO
                    _shader = _bundle.LoadAsset<Shader>("Assets/NativeCoverage.shader");
#else
                    _shader = _bundle.LoadAsset("Assets/NativeCoverage.shader", Il2CppInterop.Runtime.Il2CppType.Of<Shader>())?.Cast<Shader>();
#endif
                }
                if (_shader == null || !_shader.isSupported || _shader.name != "Hidden/Demeo/NativeCoverage") return null;
                return new OpaqueCoverage(camera, _shader, srp);
            }
            catch (Exception error)
            {
                Utils.LogInfo("Could not prepare opaque coverage: " + error.Message);
                return null;
            }
        }

        public void Dispose()
        {
            if (_commands != null)
            {
                if (_camera != null) _camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, _commands);
                _commands.Release();
            }
            UnityEngine.Object.Destroy(_material);
        }

        internal Material Material => _material;

        internal static void Unload()
        {
            _shader = null;
            _attempted = false;
            if (_bundle != null) _bundle.Unload(true);
            _bundle = null;
        }
    }
}
