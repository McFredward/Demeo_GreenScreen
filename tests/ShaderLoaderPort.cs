// Lifecycle tests control the engine's asset boundary here. The production
// Unity6 span/handle loader is separately exercised against the native fixture.
using UnityEngine;
namespace GreenScreen
{
    internal static class UnityShaderLoader
    {
        internal static AssetBundle LoadBundle(byte[] data) => AssetBundle.LoadFromMemory(data);
        internal static Shader LoadShader(AssetBundle bundle, string path) => bundle.LoadAsset<Shader>(path);
    }
}
