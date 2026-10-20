using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace GreenScreen
{
    internal static class Utils
    {
        internal static readonly List<string> Messages=new List<string>();
        internal static void LogInfo(string value) { Messages.Add(value);Debug.Log(value); }
    }
    internal static class NativePassthrough
    {
        internal static bool OwnsSrpSource(Camera camera) => true;
        internal static bool ValidTarget(RenderTextureDescriptor descriptor,bool array) => true;
    }
    internal static class SrpOutput { internal static XRDisplaySubsystem ActiveDisplay => null; }
    internal static class SrpOpaqueCoverage
    {
        internal static Func<object,object> Reader(Type type,string member,out Type valueType)
        { valueType=null;throw new NotSupportedException("The native readback fixture does not install Harmony hooks."); }
    }
}
namespace HarmonyLib
{
    public sealed class HarmonyMethod { public HarmonyMethod(MethodInfo method) { } }
    public sealed class Harmony
    {
        public Harmony(string name) { }
        public void Patch(MethodInfo target,HarmonyMethod postfix) { }
        public void Unpatch(MethodInfo target,MethodInfo postfix) { }
    }
}
