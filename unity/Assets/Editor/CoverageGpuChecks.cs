using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

// Run with an actual graphics device (no -nographics):
// Unity -batchmode -projectPath unity -executeMethod CoverageGpuChecks.Run -quit
public static class CoverageGpuChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    public static void Run()
    {
        // Source compilation alone cannot detect a malformed packaged bundle.
        BuildCoverage.VerifyBundle(Path.GetFullPath(Path.Combine(Application.dataPath, "../../assets/nativecoverage.bundle")));
        string rejectedBundle = Environment.GetEnvironmentVariable("DEMEO_COVERAGE_REJECT");
        if (!string.IsNullOrEmpty(rejectedBundle))
        {
            bool refused = false;
            try { BuildCoverage.VerifyBundle(rejectedBundle); }
            catch (BuildFailedException) { refused = true; }
            Check(refused, "Native Unity must reject the broken 1.4.1 bundle.");
            Debug.Log("Coverage bundle negative control passed: broken 1.4.1 bundle rejected by native Unity.");
        }
        Check(SystemInfo.supports2DArrayTextures && SystemInfo.supportsAsyncGPUReadback, "Array/readback support is required.");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/NativeCoverage.shader");
        var fixtureShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/CoverageFixture.shader");
        Check(shader != null && shader.isSupported && fixtureShader.isSupported, "Both shaders must compile on this GPU.");
        var coverage = new Material(shader) { enableInstancing = true };
        var fixture = new Material(fixtureShader);
        try
        {
            foreach (int samples in new[] { 1, 4 })
            {
                var descriptor = new RenderTextureDescriptor(32, 32, RenderTextureFormat.ARGBFloat, 24)
                { dimension = TextureDimension.Tex2DArray, volumeDepth = 2, msaaSamples = samples, sRGB = false };
                Check(SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor) == samples, "Requested MSAA must really be supported.");
                var target = new RenderTexture(descriptor);
                Check(target.Create(), "Create eye array.");
                try
                {
                    Scene(target, fixture);
                    Color[][] before = Read(target);
                    Repair(target, coverage, true);
                    Color[][] stereo = Read(target);
                    Scene(target, fixture);
                    Repair(target, coverage, false);
                    Color[][] separate = Read(target);
                    int opaque = 0, clear = 0, edges = 0, differences = 0;
                    for (int eye = 0; eye < 2; eye++)
                        for (int p = 0; p < stereo[eye].Length; p++)
                        {
                            Color result = stereo[eye][p], original = before[eye][p];
                            Check(Math.Abs(result.r-original.r) < .001f && Math.Abs(result.g-original.g) < .001f && Math.Abs(result.b-original.b) < .001f, "Coverage must preserve RGB.");
                            Check(Math.Abs(result.a-separate[eye][p].a) < .001f, "Instanced coverage must match that eye's independent depth.");
                            if (result.a > .999f) opaque++;
                            else if (result.a < .001f) clear++;
                            else edges++;
                            if (Math.Abs(stereo[0][p].a-stereo[1][p].a) > .1f) differences++;
                        }
                    Check(opaque > 300 && clear > 300 && differences > 300, "Both eyes need their own opaque geometry and transparent background.");
                    Check(samples == 1 || edges > 0, "MSAA must retain fractional edge coverage.");
                    Debug.Log($"Coverage GPU: samples={samples}; opaque/clear/edges={opaque}/{clear}/{edges}; distinct-eye samples={differences}.");
                }
                finally { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            }
            Debug.Log($"Coverage GPU checks passed: {checks}; device={SystemInfo.graphicsDeviceType}/{SystemInfo.graphicsDeviceName}.");
        }
        finally { UnityEngine.Object.DestroyImmediate(coverage); UnityEngine.Object.DestroyImmediate(fixture); }
    }
    private static void Scene(RenderTexture target, Material fixture)
    {
        using (var commands = new CommandBuffer())
        {
            for (int eye = 0; eye < 2; eye++)
            {
                commands.SetRenderTarget(target, 0, CubemapFace.Unknown, eye);
                commands.ClearRenderTarget(true, true, new Color(.1f, .2f, .3f, eye == 0 ? .3f : .8f));
                var properties = new MaterialPropertyBlock(); properties.SetFloat("_Side", eye == 0 ? 1 : -1);
                commands.DrawProcedural(Matrix4x4.identity, fixture, 0, MeshTopology.Triangles, 3, 1, properties);
            }
            Graphics.ExecuteCommandBuffer(commands);
        }
    }
    private static void Repair(RenderTexture target, Material coverage, bool stereo)
    {
        using (var commands = new CommandBuffer())
        {
            if (stereo)
            {
                commands.SetRenderTarget(target, 0, CubemapFace.Unknown, -1);
                commands.EnableShaderKeyword("STEREO_INSTANCING_ON");
                commands.SetInstanceMultiplier(2);
                commands.DrawProcedural(Matrix4x4.identity, coverage, 0, MeshTopology.Triangles, 3);
                commands.DrawProcedural(Matrix4x4.identity, coverage, 1, MeshTopology.Triangles, 3);
                commands.SetInstanceMultiplier(1);
                commands.DisableShaderKeyword("STEREO_INSTANCING_ON");
            }
            else for (int eye = 0; eye < 2; eye++)
            {
                commands.SetRenderTarget(target, 0, CubemapFace.Unknown, eye);
                commands.DrawProcedural(Matrix4x4.identity, coverage, 0, MeshTopology.Triangles, 3);
                commands.DrawProcedural(Matrix4x4.identity, coverage, 1, MeshTopology.Triangles, 3);
            }
            Graphics.ExecuteCommandBuffer(commands);
        }
    }
    private static Color[][] Read(RenderTexture target)
    {
        var descriptor = target.descriptor; descriptor.msaaSamples = 1; descriptor.depthBufferBits = 0;
        var resolved = new RenderTexture(descriptor); resolved.Create();
        try
        {
            if (target.antiAliasing > 1) target.ResolveAntiAliasedSurface(resolved);
            else Graphics.CopyTexture(target, resolved);
            var request = AsyncGPUReadback.Request(resolved, 0, TextureFormat.RGBAFloat);
            request.WaitForCompletion(); Check(!request.hasError && request.layerCount == 2, "Read both actual array slices.");
            return new[] { request.GetData<Color>(0).ToArray(), request.GetData<Color>(1).ToArray() };
        }
        finally { resolved.Release(); UnityEngine.Object.DestroyImmediate(resolved); }
    }
}
