using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

public static class BuildFade
{
    public static void Build()
    {
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../assets"));
        Directory.CreateDirectory(output);
#pragma warning disable 618
        PlayerSettings.virtualRealitySupported = true;
        PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;
#pragma warning restore 618
        var build = new AssetBundleBuild { assetBundleName = "nativefade.bundle",
            assetNames = new[] { "Assets/NativeFade.shader" } };
        if (BuildPipeline.BuildAssetBundles(output, new[] { build },
            BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64) == null) throw new BuildFailedException("Fade bundle build failed.");
        VerifyBundle(Path.Combine(output, "nativefade.bundle"));
    }

    public static void VerifyBundle(string filename)
    {
        var bundle = AssetBundle.LoadFromMemory(File.ReadAllBytes(filename));
        if (bundle == null) throw new BuildFailedException("Fade bundle is not loadable.");
        try
        {
            string[] names = bundle.GetAllAssetNames();
            if (names.Length != 1 || names[0] != "assets/nativefade.shader") throw new BuildFailedException("Unexpected fade bundle assets.");
            var shader = bundle.LoadAsset<Shader>(names[0]);
            if (shader == null || shader.name != "Hidden/Demeo/NativeFade") throw new BuildFailedException("Fade Shader asset is missing.");
            Debug.Log("Passthrough fade bundle native load verified: " + filename);
        }
        finally { bundle.Unload(true); }
    }
}

public sealed class FadeVariants : IPreprocessShaders
{
    public int callbackOrder => int.MaxValue;
    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
    {
        if (shader.name != "Hidden/Demeo/NativeFade") return;
        bool mono = false, stereo = false;
        var keyword = new ShaderKeyword("STEREO_INSTANCING_ON");
        foreach (var variant in data)
            if (variant.shaderKeywordSet.IsEnabled(keyword)) stereo = true; else mono = true;
        if (!mono || !stereo) throw new BuildFailedException("Fade needs ordinary and stereo-instanced variants.");
    }
}
