using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildCoverage
{
    // Build with a licensed Unity 2021.3 editor and Windows build support:
    // Unity -batchmode -nographics -projectPath unity -executeMethod BuildCoverage.Build -quit
    public static void Build()
    {
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../assets"));
        Directory.CreateDirectory(output);
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
