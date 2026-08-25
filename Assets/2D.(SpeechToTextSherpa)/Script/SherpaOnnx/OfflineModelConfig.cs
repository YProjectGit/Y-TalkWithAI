/// Copyright (c)  2024.5 by 东风破

using System.Runtime.InteropServices;

namespace SherpaOnnx
{

    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineModelConfig
    {
public OfflineTransducerModelConfig Transducer;
        public OfflineParaformerModelConfig Paraformer;
        public OfflineNemoEncDecCtcModelConfig NeMoCtc;
        public OfflineWhisperModelConfig Whisper;
        public OfflineTdnnModelConfig Tdnn;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Tokens;

        public int NumThreads;

        public int Debug;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Provider;

        [MarshalAs(UnmanagedType.LPStr)]
        public string ModelType;

        [MarshalAs(UnmanagedType.LPStr)]
        public string ModelingUnit;

        [MarshalAs(UnmanagedType.LPStr)]
        public string BpeVocab;

        [MarshalAs(UnmanagedType.LPStr)]
        public string TeleSpeechCtc;

        public OfflineSenseVoiceModelConfig SenseVoice;
        public OfflineMoonshineModelConfig Moonshine;
        public OfflineFireRedAsrModelConfig FireRedAsr;
        public OfflineDolphinModelConfig Dolphin;
        public OfflineZipformerCtcModelConfig ZipformerCtc;
        public OfflineCanaryModelConfig Canary;
        public OfflineWenetCtcModelConfig WenetCtc;
        public OfflineOmnilingualAsrCtcModelConfig Omnilingual;
        public OfflineMedAsrCtcModelConfig MedAsr;
        public OfflineFunAsrNanoModelConfig FunAsrNano;
        public OfflineFireRedAsrCtcModelConfig FireRedAsrCtc;
        public OfflineQwen3AsrModelConfig Qwen3Asr;
        public OfflineCohereTranscribeModelConfig CohereTranscribe;
    }
}
