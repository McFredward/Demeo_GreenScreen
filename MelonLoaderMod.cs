﻿#if MELONLOADER
using MelonLoader;
using GreenScreen;

[assembly: MelonInfo(
    typeof(MelonLoaderMod),
    GreenScreenBase.ModName,
    GreenScreenBase.ModVersion,
    GreenScreenBase.ModAuthor,
    "https://github.com/orendain/DemeoMods")]
[assembly: MelonGame("Resolution Games", "Demeo")]
[assembly: MelonGame("Resolution Games", "Demeo PC Edition")]
[assembly: MelonID("566783")]
[assembly: VerifyLoaderVersion("0.5.7", true)]

namespace GreenScreen
{
    using MelonLoader;

    internal class MelonLoaderMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            GreenScreenBase.Init(this);
            ModPatcher.Patch(HarmonyInstance);
        }
    }
}
#endif
