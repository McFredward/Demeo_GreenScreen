using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

public static class BuildCoverage
{
    // Build with a licensed Unity 2021.3 editor and Windows build support:
    // Unity -batchmode -nographics -projectPath unity -executeMethod BuildCoverage.Build -quit
    public static void Build()
    {
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../assets"));
        Directory.CreateDirectory(output);
#pragma warning disable 618
        PlayerSettings.virtualRealitySupported = true;
        PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;
#pragma warning restore 618
        var build = new AssetBundleBuild
        {
            assetBundleName = "nativecoverage.bundle",
            assetNames = new[] { "Assets/NativeCoverage.shader" }
        };
        if (BuildPipeline.BuildAssetBundles(output, new[] { build },
            BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64) == null)
            throw new System.Exception("Coverage bundle build failed.");
    }
}

// A stereo-capable source shader is insufficient if the bundle build strips
// its array-output variant. Refuse such a bundle at the actual compiler input.
public sealed class CoverageVariants : IPreprocessShaders
{
    public int callbackOrder => int.MaxValue;
    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
    {
        if (shader.name != "Hidden/Demeo/NativeCoverage") return;
        bool mono = false, stereo = false;
        var keyword = new ShaderKeyword("STEREO_INSTANCING_ON");
        foreach (var variant in data)
            if (variant.shaderKeywordSet.IsEnabled(keyword)) stereo = true; else mono = true;
        if (!mono || !stereo) throw new BuildFailedException("Coverage needs both ordinary and stereo-instanced variants: " + snippet.passName);
    }
}
