# sherpa-onnx の配置（2C.(SpeechToTextSherpa)）

2C のローカル STT（Speech-to-Text）に使うモデルとネイティブライブラリを、自分で選んで置いてください。APIキーは不要です。

ライセンス: [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) と [ReazonSpeech](https://research.reazon.jp/blog/2024-08-01-ReazonSpeech.html) は Apache 2.0 です。

対応 OS: **Windows x64** と **macOS arm64**。Linux x64 も同じ手順で置けますが、ワークショップでは未検証です。

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
2. 展開したフォルダから、選んだほうのファイルを `Assets/2C.(SpeechToTextSherpa)/Resource/models/` にコピーしてください。`tokens.txt` はリポジトリに入っているので、上書きしてもそのままでも構いません。

**int8 を選んだとき**

| ファイル | サイズ |
|----------|--------|
| `encoder-epoch-99-avg-1.int8.onnx` | 約 148MB |
| `decoder-epoch-99-avg-1.onnx` | 約 11MB |
| `joiner-epoch-99-avg-1.int8.onnx` | 約 2.6MB |
| `tokens.txt` | 約 45KB |

**fp32 を選んだとき**

| ファイル | サイズ |
|----------|--------|
| `encoder-epoch-99-avg-1.onnx` | 約 565MB |
| `decoder-epoch-99-avg-1.onnx` | 約 11MB |
| `joiner-epoch-99-avg-1.onnx` | 約 10MB |
| `tokens.txt` | 約 45KB |

fp32 を置いたら、Play の前に Hierarchy でデモ本体（`SpeechToTextSherpa`）を選び、Inspector のファイル名を次に変えてください。

- `sherpaEncoderFileName` → `encoder-epoch-99-avg-1.onnx`
- `sherpaJoinerFileName` → `joiner-epoch-99-avg-1.onnx`

int8 のときは、Inspector の初期値のままで動きます。

---

## 2. ネイティブライブラリを置く

モデルとは別に、sherpa-onnx 本体（共有ライブラリ）が要ります。

[sherpa-onnx v1.13.5 の Releases](https://github.com/k2-fsa/sherpa-onnx/releases/tag/v1.13.5) から、自分の OS の **shared / no-tts** をダウンロードして展開してください。

| OS | アーカイブ | コピーするファイル | 配置先 |
|----|------------|--------------------|--------|
| Windows x64 | `sherpa-onnx-v1.13.5-win-x64-shared-MD-Release-no-tts.tar.bz2` | `lib/` の `.dll` | `Assets/2C.(SpeechToTextSherpa)/Resource/Plugins/Windows/x86_64/` |
| macOS arm64 | `sherpa-onnx-v1.13.5-osx-arm64-shared-no-tts.tar.bz2` | `lib/` の `.dylib` | `Assets/2C.(SpeechToTextSherpa)/Resource/Plugins/macOS/ARM64/` |
| Linux x64 | `sherpa-onnx-v1.13.5-linux-x64-shared-no-tts.tar.bz2` | `lib/` の `.so` | `Assets/2C.(SpeechToTextSherpa)/Resource/Plugins/Linux/x86_64/` |

`sherpa-onnx-c-api` と `onnxruntime` の両方が必要です。`bin/` の実行ファイルはコピーしないでください。

Unity に戻し、Project ウィンドウを一度クリックしてください（置いたファイルが再インポートされます）。必要なら各プラグインの Inspector で自分の OS だけ有効にしてください。そのあと `SpeechToTextSherpa` シーンを Play してください。

---

## 置けたかの目安

Play 直後の Status が「待機中（Space で録音）」になれば読み込み成功です。  
「sherpa 未配置」や、画面の 1. 欄にパス案内が出るときは、選んだ 4 ファイルの名前と、ネイティブライブラリを見直してください。
