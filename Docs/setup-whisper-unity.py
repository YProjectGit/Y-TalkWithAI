#!/usr/bin/env python3
# 2D.SpeechToTextWhisper 用。whisper.cpp の ggml-base.bin を配置する。
# リポジトリルートで: python3 Docs/setup-whisper-unity.py

from __future__ import annotations

import sys
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEST_DIR = ROOT / "Assets/2D.SpeechToTextWhisper/Resource/models"
DEST = DEST_DIR / "ggml-base.bin"
MODEL_URL = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin"


def download(url: str, dest: Path) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    print("download:", url)
    print("  ->", dest)
    urllib.request.urlretrieve(url, dest)


def main() -> int:
    DEST_DIR.mkdir(parents=True, exist_ok=True)
    if DEST.is_file() and DEST.stat().st_size > 0:
        print("model already present, skip:", DEST, "bytes=", DEST.stat().st_size)
        return 0

    download(MODEL_URL, DEST)
    print("done. model ->", DEST, "bytes=", DEST.stat().st_size)
    return 0


if __name__ == "__main__":
    sys.exit(main())
