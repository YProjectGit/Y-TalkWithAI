# sherpa-onnx の配置（2C.(SpeechToTextSherpa)）

2C のローカル STT（Speech-to-Text）に使うモデルとネイティブライブラリを、自分の PC に置いてください。APIキーは不要です。

ライセンス: [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) と [ReazonSpeech](https://research.reazon.jp/blog/2024-08-01-ReazonSpeech.html) は Apache 2.0 です。

対応 OS: **Windows x64** と **macOS arm64**。Linux x64 もスクリプトで置けますが、ワークショップでは未検証です。

---

## 自動で置く（推奨）

リポジトリのルートで、次を実行してください。

```bash
python3 Docs/setup-sherpa-onnx.py
```

終わったら Unity に戻し、Project ウィンドウを一度クリックしてください（置いたファイルが再インポートされます）。そのあと `SpeechToTextLocal` シーンを Play してください。

置く場所:

| 種類 | パス |
|------|------|
| モデル 4 ファイル | `Assets/2C.(SpeechToTextSherpa)/Resource/models/` |
| Windows x64 | `Assets/2C.(SpeechToTextSherpa)/Resource/Plugins/Windows/x86_64/` |
| macOS arm64 | `Assets/2C.(SpeechToTextSherpa)/Resource/Plugins/macOS/ARM64/` |
| Linux x64 | `Assets/2C.(SpeechToTextSherpa)/Resource/Plugins/Linux/x86_64/` |

バージョンは sherpa-onnx **v1.13.5**、モデルは `sherpa-onnx-zipformer-ja-reazonspeech-2024-08-01` の int8 です。

---

## 手で置く場合

### 1. モデル

1. 次のアーカイブをダウンロードして展開してください。  
   https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-zipformer-ja-reazonspeech-2024-08-01.tar.bz2
2. 次の 4 ファイルだけを `Assets/2C.(SpeechToTextSherpa)/Resource/models/` にコピーしてください。

| ファイル |
|----------|
| `encoder-epoch-99-avg-1.int8.onnx` |
| `decoder-epoch-99-avg-1.onnx` |
| `joiner-epoch-99-avg-1.int8.onnx` |
| `tokens.txt` |

fp32 の encoder（約 565MB）は使いません。

### 2. ネイティブライブラリ

[sherpa-onnx v1.13.5 の Releases](https://github.com/k2-fsa/sherpa-onnx/releases/tag/v1.13.5) から、自分の OS の **shared / no-tts** をダウンロードして展開してください。

| OS | アーカイブ | コピーするファイル | 配置先 |
|----|------------|--------------------|--------|
| Windows x64 | `sherpa-onnx-v1.13.5-win-x64-shared-MD-Release-no-tts.tar.bz2` | `lib/` の `.dll` | `Resource/Plugins/Windows/x86_64/` |
| macOS arm64 | `sherpa-onnx-v1.13.5-osx-arm64-shared-no-tts.tar.bz2` | `lib/` の `.dylib` | `Resource/Plugins/macOS/ARM64/` |
| Linux x64 | `sherpa-onnx-v1.13.5-linux-x64-shared-no-tts.tar.bz2` | `lib/` の `.so` | `Resource/Plugins/Linux/x86_64/` |

`sherpa-onnx-c-api` と `onnxruntime` の両方が必要です。`bin/` の実行ファイルはコピーしないでください。

Unity に戻したあと、必要なら各プラグインの Inspector で自分の OS だけ有効にしてください。

---

## 置けたかの目安

Play 直後の Status が「待機中（Space で録音）」になれば読み込み成功です。  
「sherpa 未配置」や、画面の 1. 欄にパス案内が出るときは、上の 4 ファイルとネイティブライブラリを見直してください。
