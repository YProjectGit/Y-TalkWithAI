# sherpa-onnx の配置（2C.(SpeechToTextLocal)）

<br/>

2C のローカル STT（Speech-to-Text）に使うモデルとネイティブライブラリを、自分で選んで置いてください。APIキーは不要です。

対応 OS: **Windows x64** と **macOS（Apple Silicon）**。

<br/>

---

## 1. ReazonSpeechのモデルをUnity内に配置する

<br/>

以下のアーカイブデータをダウンロードして展開してください。  

https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-zipformer-ja-reazonspeech-2024-08-01.tar.bz2

<br/>

#### バージョンの選択

日本語の ReazonSpeech（Zipformer）です。配布アーカイブの中に、同じモデルの **int8**版 と **fp32**版 が入っています。それぞれの違いは以下の通りです。

| バージョン               | ファイルの合計 | 認識の速さ                 | 認識の精度                     |
| ------------------------ | -------------- | -------------------------- | ------------------------------ |
| **int8**（授業の出発点） | 約 162MB       | 速い                       | 授業の発話なら足りることが多い |
| **fp32**                 | 約 586MB       | すこし遅い（十分に速いが） | 認識がより安定している         |

どちらかを選び、下記のファイル群を `Assets/2C.(SpeechToTextLocal)/Resource/models/` にコピーしてください。

<br/>

#### int8 を選んだとき

| ファイル | サイズ |
|----------|--------|
| `encoder-epoch-99-avg-1.int8.onnx` | 約 148MB |
| `decoder-epoch-99-avg-1.int8.onnx` | 約 3MB |
| `joiner-epoch-99-avg-1.int8.onnx` | 約 2.6MB |
| `tokens.txt` | 約 45KB |

#### fp32 を選んだとき

| ファイル | サイズ |
|----------|--------|
| `encoder-epoch-99-avg-1.onnx` | 約 565MB |
| `decoder-epoch-99-avg-1.onnx` | 約 11MB |
| `joiner-epoch-99-avg-1.onnx` | 約 10MB |
| `tokens.txt` | 約 45KB |

<br/>

デモ（`SpeechToTextLocal`）は、**int8版を初期値にしています**。**fp32版 を使う場合は、SpeechToTextLocalのインスペクタで、以下の項目を書き換えてください**。

- `sherpaEncoderFileName` → `encoder-epoch-99-avg-1.onnx`
- `sherpaDecoderFileName` → `decoder-epoch-99-avg-1.onnx`
- `sherpaJoinerFileName` → `joiner-epoch-99-avg-1.onnx`

int8 のときは、Inspector の初期値のままで動きます。

<br/>

---

## 2. sherpa-onnxをUnity内に配置する

<br/>

ReazonSpeechとは別に、sherpa-onnx 本体（共有ライブラリ）が必要です。

1. 自分のOSに合った以下のファイルをダウンロードして展開してください。

| OS | ダウンロード |
|----|--------------|
| Windows (x64) | https://github.com/k2-fsa/sherpa-onnx/releases/download/v1.13.5/sherpa-onnx-v1.13.5-win-x64-shared-MD-Release-no-tts.tar.bz2 |
| macOS（Apple Silicon） | https://github.com/k2-fsa/sherpa-onnx/releases/download/v1.13.5/sherpa-onnx-v1.13.5-osx-arm64-shared-no-tts.tar.bz2 |

2. 展開したフォルダの `lib/` から、次のファイルだけをコピーしてください。`.lib` と `bin/` の実行ファイルはコピーしないでください。

| OS | コピーするファイル | 配置先 |
|----|--------------------|--------|
| Windows (x64) | `sherpa-onnx-c-api.dll`<br>`onnxruntime.dll`<br>`onnxruntime_providers_shared.dll` | `Assets/2C.(SpeechToTextLocal)/Resource/Plugins/Windows/x86_64/` |
| macOS（Apple Silicon） | `libsherpa-onnx-c-api.dylib`<br>`libonnxruntime.dylib` | `Assets/2C.(SpeechToTextLocal)/Resource/Plugins/macOS/ARM64/` |

3. Unity に戻り、Project ウィンドウを一度クリックしてください（置いたファイルが再インポートされます）。必要なら各プラグインのインスペクタで自分の OS だけ有効にしてください。そのあと `SpeechToTextLocal` シーンを Play してください。

<br/>

---

## 確認

<br/>

Play 直後のコンソール画面に、

```text
[SpeechToTextLocal] sherpa-onnx の初期化に成功しました。
```

と出れば成功です。

失敗した場合は何かしらのエラーメッセージが出るはずです。配置場所、ファイル名、インスペクタの参照項目をチェックしてください。
