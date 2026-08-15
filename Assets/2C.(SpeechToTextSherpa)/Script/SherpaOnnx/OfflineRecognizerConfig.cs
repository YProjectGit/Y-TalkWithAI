/// Copyright (c)  2024.5 by 东风破

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineRecognizerConfig
    {
public FeatureConfig FeatConfig;
        public OfflineModelConfig ModelConfig;
        public OfflineLMConfig LmConfig;

        [MarshalAs(UnmanagedType.LPStr)]
        public string DecodingMethod;

        public int MaxActivePaths;

        [MarshalAs(UnmanagedType.LPStr)]
        public string HotwordsFile;

        public float HotwordsScore;

        [MarshalAs(UnmanagedType.LPStr)]
        public string RuleFsts;

        [MarshalAs(UnmanagedType.LPStr)]
        public string RuleFars;

        public float BlankPenalty;

        public HomophoneReplacerConfig Hr;
    }
}
