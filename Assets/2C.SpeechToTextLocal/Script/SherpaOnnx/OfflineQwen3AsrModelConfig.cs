/// Copyright (c)  2026  Xiaomi Corporation

using System.Runtime.InteropServices;

namespace SherpaOnnx
{

    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineQwen3AsrModelConfig
    {
[MarshalAs(UnmanagedType.LPStr)]
        public string ConvFrontend;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Encoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Decoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Tokenizer;

        public int MaxTotalLen;
        public int MaxNewTokens;
        public float Temperature;
        public float TopP;
        public int Seed;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Hotwords;
    }
}
