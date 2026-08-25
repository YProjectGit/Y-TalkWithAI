# sherpa-onnx の配置（2D.(SpeechToTextSherpa)）

2D のローカル STT（Speech-to-Text）に使うモデルとネイティブライブラリを、自分で選んで置いてください。APIキーは不要です。

ライセンス: [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) と [ReazonSpeech](https://research.reazon.jp/blog/2024-08-01-ReazonSpeech.html) は Apache 2.0 です。

対応 OS: **Windows x64** と **macOS（Apple Silicon）**。

---

## モデルを選ぶ

このデモが読むのは、日本語の ReazonSpeech（Zipformer）です。配布アーカイブの中に、同じモデルの **int8** と **fp32** が入っています。どちらを置くかを決めてください。

| 選ぶもの | 置くファイルの合計 | 速さ | 精度 | 向いているとき |
|----------|--------------------|------|------|----------------|
| **int8**（授業の出発点） | 約 162MB | 速い | 授業の発話なら足りることが多い | まずこれを置く |
| **fp32** | 約 586MB | 遅め | 少し安定しやすい | ディスクと CPU に余裕があるとき |

アーカイブ全体は約 680MB です（両方入っているため）。Unity にコピーするのは、選んだほうの 4 ファイルだけです。

---

## 1. アーカイブを取る

1. 次をダウンロードして展開してください。  
   https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-zipformer-ja-reazonspeech-2024-08-01.tar.bz2
2. 展開したフォルダから、選んだほうのファイルを `Assets/2D.(SpeechToTextSherpa)/Resource/models/` にコピーしてください。`tokens.txt` はリポジトリに入っているので、上書きしてもそのままでも構いません。

**int8 を選んだとき**

| ファイル | サイズ |
|----------|--------|
| `encoder-epoch-99-avg-1.int8.onnx` | 約 148MB |
| `decoder-epoch-99-avg-1.int8.onnx` | 約 3MB |
| `joiner-epoch-99-avg-1.int8.onnx` | 約 2.6MB |
| `tokens.txt` | 約 45KB |

**fp32 を選んだとき**

| ファイル | サイズ |
|----------|--------|
| `encoder-epoch-99-avg-1.onnx` | 約 565MB |
| `decoder-epoch-99-avg-1.onnx` | 約 11MB |
| `joiner-epoch-99-avg-1.onnx` | 約 10MB |
| `tokens.txt` | 約 45KB |

fp32 を置いたら、Play の前に Hierarchy でデモ本体（`SpeechToTextSherpa`）を選び、Inspector のファイル名を次に変えてください。置いたファイルと名前が違うと読み込みません。

- `sherpaEncoderFileName` → `encoder-epoch-99-avg-1.onnx`
- `sherpaDecoderFileName` → `decoder-epoch-99-avg-1.onnx`
- `sherpaJoinerFileName` → `joiner-epoch-99-avg-1.onnx`

int8 のときは、Inspector の初期値のままで動きます。

---

## 2. ネイティブライブラリを置く

モデルとは別に、sherpa-onnx 本体（共有ライブラリ）が要ります。上のモデル用アーカイブとは別物です。

1. 自分の OS のファイルを、次の URL からダウンロードして展開してください。

| OS | ダウンロード |
|----|--------------|
| Windows x64 | https://github.com/k2-fsa/sherpa-onnx/releases/download/v1.13.5/sherpa-onnx-v1.13.5-win-x64-shared-MD-Release-no-tts.tar.bz2 |
| macOS（Apple Silicon） | https://github.com/k2-fsa/sherpa-onnx/releases/download/v1.13.5/sherpa-onnx-v1.13.5-osx-arm64-shared-no-tts.tar.bz2 |

2. 展開したフォルダの `lib/` から、次のファイルだけをコピーしてください。`.lib` と `bin/` の実行ファイルはコピーしないでください。

| OS | コピーするファイル | 配置先 |
|----|--------------------|--------|
| Windows x64 | `sherpa-onnx-c-api.dll`<br>`onnxruntime.dll`<br>`onnxruntime_providers_shared.dll` | `Assets/2D.(SpeechToTextSherpa)/Resource/Plugins/Windows/x86_64/` |
| macOS（Apple Silicon） | `libsherpa-onnx-c-api.dylib`<br>`libonnxruntime.dylib` | `Assets/2D.(SpeechToTextSherpa)/Resource/Plugins/macOS/ARM64/` |

3. Unity に戻し、Project ウィンドウを一度クリックしてください（置いたファイルが再インポートされます）。必要なら各プラグインの Inspector で自分の OS だけ有効にしてください。そのあと `SpeechToTextSherpa` シーンを Play してください。

---

## 置けたかの目安

Play 直後の Status が「待機中（Space で録音）」になれば読み込み成功です。  
「sherpa 未配置」や、画面の 1. 欄にパス案内が出るときは、選んだ 4 ファイルの名前と、ネイティブライブラリを見直してください。
