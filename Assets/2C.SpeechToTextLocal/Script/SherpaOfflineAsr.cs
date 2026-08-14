// SherpaOfflineAsr.cs
// sherpa-onnx の offline transducer を、このデモから呼びやすい形に薄く包む。
// モデル読み込みと「float サンプル → 認識テキスト」だけを持つ。UI や Gemini 通信は持たない。

using System;
using System.Diagnostics;
using System.IO;
using SherpaOnnx;
using UnityEngine;

/// <summary>
/// ReazonSpeech（Zipformer）を sherpa-onnx で offline 認識する。
/// </summary>
public class SherpaOfflineAsr
{
    public string modelDirectoryRelativePath = "2C.SpeechToTextLocal/Resource/models"; // Assets/ からの相対
    public string encoderFileName = "encoder-epoch-99-avg-1.int8.onnx";
    public string decoderFileName = "decoder-epoch-99-avg-1.onnx";
    public string joinerFileName = "joiner-epoch-99-avg-1.int8.onnx";
    public string tokensFileName = "tokens.txt";
    public int numThreads = 2; // CPU スレッド数

    OfflineRecognizer recognizer; // Play 中は使い回す（毎回ロードしない）
    string lastError; // 初期化失敗時の案内

    public bool IsReady
    {
        get { return recognizer != null; }
    }

    public string LastError
    {
        get { return lastError; }
    }

    public string ModelDirectory
    {
        get { return Path.Combine(Application.dataPath, modelDirectoryRelativePath); }
    }

    public string EncoderPath
    {
        get { return Path.Combine(ModelDirectory, encoderFileName); }
    }

    public string DecoderPath
    {
        get { return Path.Combine(ModelDirectory, decoderFileName); }
    }

    public string JoinerPath
    {
        get { return Path.Combine(ModelDirectory, joinerFileName); }
    }

    public string TokensPath
    {
        get { return Path.Combine(ModelDirectory, tokensFileName); }
    }

    // 4 ファイルの有無を見て、足りなければ Docs への案内を lastError に残す
    public bool TryInitialize()
    {
        lastError = null;
        DisposeRecognizer();

        string missing = FindMissingModelFile();
        if (missing != null)
        {
            lastError =
                "sherpa モデルがありません: " + missing
                + "\nDocs/sherpa-onnx-setup.md の手順で配置してください。\n"
                + "配置先: " + ModelDirectory;
            return false;
        }

        try
        {
            OfflineRecognizerConfig config = CreateConfig();
            recognizer = new OfflineRecognizer(config);
            if (recognizer == null)
            {
                lastError = "OfflineRecognizer の作成に失敗しました。ネイティブ lib の配置を確認してください。";
                return false;
            }
        }
        catch (Exception e)
        {
            lastError =
                "sherpa-onnx の初期化に失敗しました: " + e.Message
                + "\nDocs/sherpa-onnx-setup.md を確認してください。";
            DisposeRecognizer();
            return false;
        }

        return true;
    }

    // 録音した float サンプルを認識する（呼び出し側でバックグラウンドに出す）
    public SherpaAsrResult Recognize(float[] samples, int sampleRate)
    {
        SherpaAsrResult result = new SherpaAsrResult();
        result.sampleRate = sampleRate;
        result.sampleCount = samples != null ? samples.Length : 0;
        result.audioSeconds = sampleRate > 0 ? (float)result.sampleCount / sampleRate : 0f;

        if (recognizer == null)
        {
            result.error = lastError != null ? lastError : "recognizer が初期化されていません。";
            return result;
        }

        if (samples == null || samples.Length == 0)
        {
            result.error = "音声サンプルが空です。";
            return result;
        }

        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            using (OfflineStream stream = recognizer.CreateStream())
            {
                stream.AcceptWaveform(sampleRate, samples);
                recognizer.Decode(stream);
                OfflineRecognizerResult native = stream.Result;
                result.text = native != null && native.Text != null ? native.Text.Trim() : string.Empty;
            }
        }
        catch (Exception e)
        {
            result.error = "認識中にエラー: " + e.Message;
        }

        watch.Stop();
        result.elapsedMilliseconds = watch.ElapsedMilliseconds;
        if (result.audioSeconds > 0.0001f)
        {
            result.realtimeFactor = (result.elapsedMilliseconds / 1000f) / result.audioSeconds;
        }

        return result;
    }

    public void Dispose()
    {
        DisposeRecognizer();
    }

    string FindMissingModelFile()
    {
        if (!File.Exists(EncoderPath))
        {
            return EncoderPath;
        }

        if (!File.Exists(DecoderPath))
        {
            return DecoderPath;
        }

        if (!File.Exists(JoinerPath))
        {
            return JoinerPath;
        }

        if (!File.Exists(TokensPath))
        {
            return TokensPath;
        }

        return null;
    }

    OfflineRecognizerConfig CreateConfig()
    {
        OfflineRecognizerConfig config = default(OfflineRecognizerConfig);
        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 80;
        config.DecodingMethod = "greedy_search";
        config.MaxActivePaths = 4;
        config.HotwordsFile = string.Empty;
        config.HotwordsScore = 1.5f;
        config.RuleFsts = string.Empty;
        config.RuleFars = string.Empty;
        config.BlankPenalty = 0f;

        config.ModelConfig.Transducer.Encoder = EncoderPath;
        config.ModelConfig.Transducer.Decoder = DecoderPath;
        config.ModelConfig.Transducer.Joiner = JoinerPath;
        config.ModelConfig.Tokens = TokensPath;
        config.ModelConfig.NumThreads = numThreads < 1 ? 1 : numThreads;
        config.ModelConfig.Debug = 0;
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.ModelType = "transducer";
        config.ModelConfig.ModelingUnit = "cjkchar";
        config.ModelConfig.BpeVocab = string.Empty;
        config.ModelConfig.TeleSpeechCtc = string.Empty;
        return config;
    }

    void DisposeRecognizer()
    {
        if (recognizer != null)
        {
            recognizer.Dispose();
            recognizer = null;
        }
    }
}

/// <summary>
/// 1回の認識結果（テキストと速さ）。
/// </summary>
public class SherpaAsrResult
{
    public string text; // 認識本文。失敗時は空
    public string error; // 失敗理由。成功時は null
    public long elapsedMilliseconds; // 認識にかかった時間
    public float audioSeconds; // 入力音声の秒数
    public float realtimeFactor; // 経過秒 / 音声秒（小さいほど速い）
    public int sampleRate;
    public int sampleCount;
}
