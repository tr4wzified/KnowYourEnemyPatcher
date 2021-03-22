// Autogenerated by https://github.com/Mutagen-Modding/Mutagen.Bethesda.FormKeys

using Mutagen.Bethesda.Skyrim;

namespace Mutagen.Bethesda.FormKeys.SkyrimSE
{
    public static partial class KnowYourElements
    {
        public static class MagicEffect
        {
            private static FormLink<IMagicEffectGetter> Construct(uint id) => new FormLink<IMagicEffectGetter>(ModKey.MakeFormKey(id));
            public static FormLink<IMagicEffectGetter> AbWeaknessEarthConstant => Construct(0x5900);
            public static FormLink<IMagicEffectGetter> AbWeaknessWaterConstant => Construct(0x5901);
            public static FormLink<IMagicEffectGetter> AbWeaknessWindConstant => Construct(0x5902);
            public static FormLink<IMagicEffectGetter> AbResistWind => Construct(0x5903);
        }
    }
}
