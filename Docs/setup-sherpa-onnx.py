#!/usr/bin/env python3
# 2C.(SpeechToTextSherpa) 用。ReazonSpeech モデルと sherpa-onnx ネイティブ lib を配置する。
# リポジトリルートで: python3 Docs/setup-sherpa-onnx.py

from __future__ import annotations

import shutil
import sys
import tarfile
import tempfile
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEMO = ROOT / "Assets/2C.(SpeechToTextSherpa)"
MODELS = DEMO / "Resource/models"
PLUGINS = DEMO / "Resource/Plugins"

SHERPA_VERSION = "1.13.5"
MODEL_URL = (
    "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/"
    "sherpa-onnx-zipformer-ja-reazonspeech-2024-08-01.tar.bz2"
)
RELEASE_BASE = f"https://github.com/k2-fsa/sherpa-onnx/releases/download/v{SHERPA_VERSION}"

MODEL_FILES = (
    "encoder-epoch-99-avg-1.int8.onnx",
    "decoder-epoch-99-avg-1.onnx",
    "joiner-epoch-99-avg-1.int8.onnx",
    "tokens.txt",
)

# GitHub shared-no-tts アーカイブ → Unity Plugins
NATIVE_ARCHIVES = (
    (
        f"sherpa-onnx-v{SHERPA_VERSION}-win-x64-shared-MD-Release-no-tts.tar.bz2",
        PLUGINS / "Windows/x86_64",
        (".dll",),
    ),
    (
        f"sherpa-onnx-v{SHERPA_VERSION}-osx-arm64-shared-no-tts.tar.bz2",
        PLUGINS / "macOS/ARM64",
        (".dylib",),
    ),
    (
        f"sherpa-onnx-v{SHERPA_VERSION}-linux-x64-shared-no-tts.tar.bz2",
        PLUGINS / "Linux/x86_64",
        (".so", ".so.1"),
    ),
)


def download(url: str, dest: Path) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    print("download:", url)
    print("  ->", dest)
    urllib.request.urlretrieve(url, dest)


def extract_models(archive: Path) -> None:
    MODELS.mkdir(parents=True, exist_ok=True)
    print("extract models from", archive.name)
    with tarfile.open(archive, "r:bz2") as tar:
        members = {Path(m.name).name: m for m in tar.getmembers() if m.isfile()}
        for name in MODEL_FILES:
            if name not in members:
                raise SystemExit("archive に " + name + " がありません")
            member = members[name]
            src = tar.extractfile(member)
            if src is None:
                raise SystemExit("extract 失敗: " + name)
            out = MODELS / name
            out.write_bytes(src.read())
            print("  model:", out, "bytes=", out.stat().st_size)


def extract_natives(archive: Path, dest_dir: Path, suffixes: tuple) -> None:
    print("extract natives from", archive.name)
    dest_dir.mkdir(parents=True, exist_ok=True)
    copied = 0
    with tarfile.open(archive, "r:bz2") as tar:
        for member in tar.getmembers():
            if not member.isfile():
                continue
            name = Path(member.name).name
            # bin/ の実行ファイルは置かない。lib/ の共有ライブラリだけ
            if Path(member.name).parent.name != "lib":
                continue
            if not name.endswith(suffixes):
                continue
            src = tar.extractfile(member)
            if src is None:
                continue
            out = dest_dir / name
            out.write_bytes(src.read())
            print("  native:", out, "bytes=", out.stat().st_size)
            copied += 1
    if copied == 0:
        raise SystemExit("native lib を抽出できませんでした: " + archive.name)


def main() -> int:
    MODELS.mkdir(parents=True, exist_ok=True)
    work = Path(tempfile.mkdtemp(prefix="sherpa-setup-"))
    try:
        need_models = any(not (MODELS / name).is_file() for name in MODEL_FILES)
        if need_models:
            model_archive = work / "reazonspeech.tar.bz2"
            download(MODEL_URL, model_archive)
            extract_models(model_archive)
        else:
            print("models already present, skip")

        for archive_name, dest_dir, suffixes in NATIVE_ARCHIVES:
            archive = work / archive_name
            download(RELEASE_BASE + "/" + archive_name, archive)
            extract_natives(archive, dest_dir, suffixes)
    finally:
        shutil.rmtree(work, ignore_errors=True)

    print("done. models ->", MODELS)
    print("done. plugins ->", PLUGINS)
    return 0


if __name__ == "__main__":
    sys.exit(main())
