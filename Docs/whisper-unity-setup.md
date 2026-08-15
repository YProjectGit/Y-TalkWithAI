# whisper.unity の配置（2D.SpeechToTextWhisper）

2D のローカル STT（Speech-to-Text）に使う ggml モデルを、自分の PC に置いてください。APIキーは不要です。ネイティブライブラリは UPM（Unity Package Manager）パッケージ `com.whisper.unity` に同梱されます。

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

## 自動で置く（推奨）

リポジトリのルートで、次を実行してください。

```bash
python3 Docs/setup-whisper-unity.py
```

終わったら Unity に戻し、Project ウィンドウを一度クリックしてください（置いたファイルが再インポートされます）。そのあと `SpeechToTextWhisper` シーンを Play してください。

置く場所:

| 種類 | パス |
|------|------|
| ggml モデル | `Assets/2D.SpeechToTextWhisper/Resource/models/ggml-base.bin` |

既定は多言語の **base**（日本語向け。tiny より重いが精度が安定しやすい）です。

---

## 手で置く場合

1. 次のファイルをダウンロードしてください。  
   https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin
2. `Assets/2D.SpeechToTextWhisper/Resource/models/ggml-base.bin` として保存してください。

より精度を上げたいときは `ggml-small.bin` を同じフォルダに置き、インスペクタの `whisperModelRelativePath` を差し替えてください。英語専用（`.en`）のモデルは日本語には使えません。

---

## 置けたかの目安

Play 直後の Status が「待機中（Space で録音）」になれば読み込み成功です。  
「whisper 未配置」や、画面の 1. 欄にパス案内が出るときは、`ggml-base.bin` の場所とパッケージの解決を見直してください。
