using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class FadeGpuChecks
{
    private static int checks;
    private static void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
    public static void Run()
    {
        BuildFade.VerifyBundle(Path.GetFullPath(Path.Combine(Application.dataPath, "../../assets/nativefade.bundle")));
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/NativeFade.shader");
        Shader black = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/FadeBlackControl.shader");
        Check(shader != null && shader.isSupported && black != null && black.isSupported, "Fade/control shaders supported.");
        var fade = new Material(shader) { enableInstancing = true };
        var control = new Material(black) { enableInstancing = true };
        var mesh = new Mesh { vertices = new[] { new Vector3(-1,-1,0), new Vector3(-1,3,0), new Vector3(3,-1,0) }, triangles = new[] { 0,1,2 } };
        try
        {
            foreach (int samples in new[] { 1,4 })
            foreach (bool stereo in new[] { false,true })
            foreach (float coverage in new[] { 0f,.25f,1f })
            foreach (float opacity in new[] { 0f,.25f,.5f,1f })
            {
                Color[][] values = Draw(fade, mesh, samples, stereo, coverage, opacity);
                for (int eye=0; eye<2; eye++)
                {
                    Color initial = Initial(eye, coverage); float remaining = 1-opacity;
                    foreach (Color pixel in values[eye])
                        Check(Math.Abs(pixel.r-initial.r*remaining)<.002f && Math.Abs(pixel.g-initial.g*remaining)<.002f
                            && Math.Abs(pixel.b-initial.b*remaining)<.002f && Math.Abs(pixel.a-coverage*remaining)<.002f,
                            "Each current eye must fade RGB and coverage equally; empty background stays transparent.");
                }
            }
            Color[][] forbidden = Draw(control, mesh, 1, true, 0, 1);
            Check(forbidden[0][0].a>.999f && forbidden[1][0].a>.999f, "Original source-over black control must block the room on both eyes.");
            Debug.Log("Passthrough fade GPU checks passed: " + checks + "; two eyes, 1x/4x MSAA, separate/instanced, current scene/background alpha, material-property-block isolation; " + SystemInfo.graphicsDeviceType + "/" + SystemInfo.graphicsDeviceName);
        }
        finally { UnityEngine.Object.DestroyImmediate(fade); UnityEngine.Object.DestroyImmediate(control); UnityEngine.Object.DestroyImmediate(mesh); }
    }
    private static Color Initial(int eye,float coverage) => eye==0 ? new Color(.2f*coverage,.4f*coverage,.6f*coverage,coverage) : new Color(.7f*coverage,.3f*coverage,.1f*coverage,coverage);
    private static Color[][] Draw(Material fade,Mesh mesh,int samples,bool stereo,float coverage,float opacity)
    {
        var descriptor = new RenderTextureDescriptor(8,8,RenderTextureFormat.ARGBFloat,24)
            { dimension=TextureDimension.Tex2DArray, volumeDepth=2, msaaSamples=samples, sRGB=false };
        Check(SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor)==samples,"Actual requested MSAA support.");
        var target=new RenderTexture(descriptor);Check(target.Create(),"Allocate current eye array.");
        try
        {
            fade.SetFloat("_PassthroughFade",opacity);
            var block=new MaterialPropertyBlock();block.SetColor("_Color",Color.black);block.SetColor("_BaseColor",Color.black);
            using(var commands=new CommandBuffer())
            {
                commands.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.identity);
                commands.SetGlobalMatrixArray("unity_StereoMatrixVP",new[] {Matrix4x4.identity,Matrix4x4.identity});
                for(int eye=0;eye<2;eye++)
                {
                    commands.SetRenderTarget(target,0,CubemapFace.Unknown,eye);
                    commands.ClearRenderTarget(true,true,Initial(eye,coverage));
                }
                if(stereo)
                {
                    commands.SetRenderTarget(target,0,CubemapFace.Unknown,-1);
                    commands.EnableShaderKeyword("STEREO_INSTANCING_ON");commands.SetInstanceMultiplier(2);
                    commands.DrawMesh(mesh,Matrix4x4.identity,fade,0,0,block);
                    commands.SetInstanceMultiplier(1);commands.DisableShaderKeyword("STEREO_INSTANCING_ON");
                }
                else for(int eye=0;eye<2;eye++)
                {
                    commands.SetRenderTarget(target,0,CubemapFace.Unknown,eye);
                    commands.DrawMesh(mesh,Matrix4x4.identity,fade,0,0,block);
                }
                Graphics.ExecuteCommandBuffer(commands);
            }
            descriptor.msaaSamples=1;descriptor.depthBufferBits=0;
            var resolved=new RenderTexture(descriptor);Check(resolved.Create(),"Allocate exact resolved eyes.");
            try
            {
                if(samples>1)target.ResolveAntiAliasedSurface(resolved);else Graphics.CopyTexture(target,resolved);
                var request=AsyncGPUReadback.Request(resolved,0,TextureFormat.RGBAFloat);request.WaitForCompletion();
                Check(!request.hasError&&request.layerCount==2,"Read both actual current eye slices.");
                return new[] {request.GetData<Color>(0).ToArray(),request.GetData<Color>(1).ToArray()};
            }
            finally {resolved.Release();UnityEngine.Object.DestroyImmediate(resolved);}
        }
        finally {target.Release();UnityEngine.Object.DestroyImmediate(target);}
    }
}
