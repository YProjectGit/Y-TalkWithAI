# whisper.unity の配置（2D.(SpeechToTextWhisper)）

2D のローカル STT（Speech-to-Text）に使う ggml モデルを、自分で選んで置いてください。APIキーは不要です。ネイティブライブラリは UPM（Unity Package Manager）パッケージ `com.whisper.unity` に同梱されます。

ライセンス: [whisper.unity](https://github.com/Macoron/whisper.unity) と [whisper.cpp](https://github.com/ggerganov/whisper.cpp)、[OpenAI Whisper](https://github.com/openai/whisper) は MIT です。

対応 OS: パッケージの公式対応どおり **Windows x64**、**macOS（Intel / arm64）**、Linux x64。ワークショップの主対象は Win / Mac です。

---

## パッケージ

`Packages/manifest.json` に次が入っています。Unity を開くと自動で入ります。

```text
https://github.com/Macoron/whisper.unity.git?path=/Packages/com.whisper.unity#1.4.0
```

バージョンは whisper.unity **1.4.0**（whisper.cpp 1.7.5）です。

---

## モデルを選ぶ

Whisper は、同じ系統のモデルを大きさで選べます。大きいほど精度は上がり、ファイルも処理も重くなります。日本語を使うので、**多言語モデル**を選んでください。名前に `.en` が付く英語専用は使えません。

| モデル | ファイル | サイズ | 速さ | 精度 | 向いているとき |
|--------|----------|--------|------|------|----------------|
| **tiny** | `ggml-tiny.bin` | 約 75MB | 速い | 低い。短い文は足りることがある | 軽い PC、まず動かしたいとき |
| **base**（授業の出発点） | `ggml-base.bin` | 約 142MB | 普通 | 授業の発話なら安定しやすい | まずこれを置く |
| **small** | `ggml-small.bin` | 約 466MB | 遅め | より安定 | 精度を上げたいとき |
| **medium** | `ggml-medium.bin` | 約 1.5GB | 遅い | 高い | ディスクとメモリに余裕があるとき |

`large-v3`（約 2.9GB）は精度は高いですが、授業用の PC では重すぎることが多いです。

一覧と配布元 → [ggerganov/whisper.cpp（Hugging Face）](https://huggingface.co/ggerganov/whisper.cpp)

---

## モデルを置く

1. 選んだファイルをダウンロードしてください。

| モデル | URL |
|--------|-----|
| tiny | https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin |
| base | https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin |
| small | https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin |
| medium | https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin |

2. `Assets/2D.(SpeechToTextWhisper)/Resource/models/` に、ダウンロードした名前のまま置いてください。
3. Unity に戻し、Project ウィンドウを一度クリックしてください（置いたファイルが再インポートされます）。
4. base 以外を置いたときは、Play の前に Hierarchy でデモ本体（`SpeechToTextWhisper`）を選び、Inspector の `whisperModelRelativePath` を置いたファイルに合わせてください。

例:

- tiny → `2D.(SpeechToTextWhisper)/Resource/models/ggml-tiny.bin`
- small → `2D.(SpeechToTextWhisper)/Resource/models/ggml-small.bin`

base のときは、Inspector の初期値のままで動きます。そのあと `SpeechToTextWhisper` シーンを Play してください。

---

## 置けたかの目安

Play 直後の Status が「待機中（Space で録音）」になれば読み込み成功です。  
「whisper 未配置」や、画面の 1. 欄にパス案内が出るときは、置いたファイル名と `whisperModelRelativePath`、パッケージの解決を見直してください。
