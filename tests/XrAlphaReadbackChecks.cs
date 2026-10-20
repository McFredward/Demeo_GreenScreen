using System;
using System.Reflection;
using GreenScreen;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Native Unity API check. The runner copies the actual production probe into
// a temporary Editor project; only unrelated mod/loader services are stubbed.
public static class XrAlphaReadbackChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }

    public static void Run()
    {
        Check(SystemInfo.supportsAsyncGPUReadback && SystemInfo.supports2DArrayTextures, "Native GPU readback/array support is required.");
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProbeFixture.shader");
        Check(shader!=null && shader.isSupported,"Fixture shader must compile on the actual GPU.");
        var material=new Material(shader);
        try
        {
            foreach (var dimension in new[] { TextureDimension.Tex2D,TextureDimension.Tex2DArray })
            foreach (var format in new[] { GraphicsFormat.R8G8B8A8_UNorm,GraphicsFormat.R8G8B8A8_SRGB })
            foreach (int samples in new[] { 1,4 })
            {
                var descriptor=new RenderTextureDescriptor(128,128)
                { graphicsFormat=format,depthBufferBits=0,dimension=dimension,volumeDepth=dimension==TextureDimension.Tex2DArray ? 2 : 1,msaaSamples=samples };
                Check(SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor)==samples,"Requested MSAA must actually be supported.");
                var source=new RenderTexture(descriptor);
                Check(source.Create(),"Create native fixture target.");
                try
                {
                    var pixel=SrpAlphaProbe.PixelViewport(new Rect(0,0,1,1),128,128);
                    Check(pixel.x==0 && pixel.y==0 && pixel.width==128 && pixel.height==128,"A full normalized viewport must expand to the full target.");
                    pixel=SrpAlphaProbe.PixelViewport(new Rect(.25f,.25f,.5f,.5f),128,128);
                    int left=(int)Math.Ceiling(pixel.x),bottom=(int)Math.Ceiling(pixel.y);
                    int right=(int)Math.Floor(pixel.x+pixel.width),top=(int)Math.Floor(pixel.y+pixel.height);
                    int x=left+(right-left-32)/2,height=top-bottom;
                    Check(x==48 && bottom==32 && height==64,"Normalized cropped viewport uses the production float conversion.");
                    Type batchType=typeof(SrpAlphaProbe).GetNestedType("Batch",BindingFlags.NonPublic);
                    object batch=Activator.CreateInstance(batchType,true);
                    var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                    var heights=(int[])batchType.GetField("Heights",flags).GetValue(batch);
                    heights[0]=heights[1]=height;
                    batchType.GetField("Pending",flags).SetValue(batch,2);
                    batchType.GetField("Description",flags).SetValue(batch,"native GPU fixture");
                    MethodInfo complete=typeof(SrpAlphaProbe).GetMethod("Complete",BindingFlags.Static|BindingFlags.NonPublic);
                    for(int view=0;view<2;view++)
                    {
                        int slice=dimension==TextureDimension.Tex2DArray ? view : 0;
                        using(var commands=new CommandBuffer())
                        {
                            commands.SetRenderTarget(source,0,CubemapFace.Unknown,slice);
                            var properties=new MaterialPropertyBlock();properties.SetFloat("_BaseAlpha",view==0 ? .25f : .75f);
                            commands.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,3,1,properties);
                            Graphics.ExecuteCommandBuffer(commands);
                        }
                        // Same partial-rectangle, one-slice API as production,
                        // including MSAA inputs and sRGB colour attachments.
                        var request=AsyncGPUReadback.Request(source,0,x,32,bottom,height,slice,1,TextureFormat.RGBA32);
                        request.WaitForCompletion();
                        Check(!request.hasError && request.layerCount==1,"Read back exactly the requested actual slice.");
                        var data=request.GetData<Color32>(0);
                        Check(data.Length==32*height,"Native crop/readback dimensions must match the production summary.");
                        for(int i=0;i<data.Length;i++)
                        {
                            float alpha=(view==0 ? .25f : .75f)+(x+i%32>=64 ? .25f : 0);
                            Check(Math.Abs(data[i].a-alpha*255)<=1,"Every alpha byte must match its requested source pixel and view.");
                        }
                        complete.Invoke(null,new object[] { batch,view,request });
                    }
                    string report=Utils.Messages[Utils.Messages.Count-1];
                    Check(report.Contains("view 0 region 1 alpha min/mean/max=") && report.Contains("view 1 region 1 alpha min/mean/max="),
                        "The actual production completion code summarizes both native GPU replies.");
                    Debug.Log("XR alpha native readback: "+dimension+"/"+format+"/MSAA"+samples+" passed.");
                }
                finally { source.Release();UnityEngine.Object.DestroyImmediate(source); }
            }
            Debug.Log("XR alpha native checks passed: "+checks+"; device="+SystemInfo.graphicsDeviceType+"/"+SystemInfo.graphicsDeviceName);
        }
        finally { UnityEngine.Object.DestroyImmediate(material); }
    }
}
