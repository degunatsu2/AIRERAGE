#!/usr/bin/env python3
"""Assets/ 以下の .meta ファイルを生成・検査する。

Unity Editor を通さずに追加したファイル（クラウド環境で Claude が作ったスクリプトなど）にも
固定の GUID を持つ .meta を用意し、環境ごとに別の GUID が生成されて参照が壊れるのを防ぐ。

使い方:
    python3 tools/unity_meta.py          # 足りない .meta を生成する
    python3 tools/unity_meta.py --check  # 足りない .meta と、対応するファイルがない .meta を報告する（あれば終了コード 1）

対応していない種類のファイル（画像など、インポート設定が必要なもの）は生成せず、
Unity Editor で開いて生成するよう報告する。
"""

import argparse
import sys
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "Assets"

FOLDER_TEMPLATE = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

MONO_TEMPLATE = """fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

ASMDEF_TEMPLATE = """fileFormatVersion: 2
guid: {guid}
AssemblyDefinitionImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

ASMREF_TEMPLATE = """fileFormatVersion: 2
guid: {guid}
AssemblyDefinitionReferenceImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

TEXT_TEMPLATE = """fileFormatVersion: 2
guid: {guid}
TextScriptImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

TEMPLATES_BY_SUFFIX = {
    ".cs": MONO_TEMPLATE,
    ".asmdef": ASMDEF_TEMPLATE,
    ".asmref": ASMREF_TEMPLATE,
    ".md": TEXT_TEMPLATE,
    ".txt": TEXT_TEMPLATE,
    ".json": TEXT_TEMPLATE,
    ".xml": TEXT_TEMPLATE,
    ".csv": TEXT_TEMPLATE,
    ".bytes": TEXT_TEMPLATE,
}


def is_ignored(path: Path) -> bool:
    """Unity がインポートしないパス（隠しファイル、末尾が ~ のフォルダなど）を除外する。"""
    for part in path.relative_to(ASSETS).parts:
        if part.startswith(".") or part.endswith("~") or part == "cvs" or part.endswith(".tmp"):
            return True
    return False


def asset_paths():
    for path in sorted(ASSETS.rglob("*")):
        if path.suffix == ".meta" or is_ignored(path):
            continue
        yield path


def template_for(path: Path):
    if path.is_dir():
        return FOLDER_TEMPLATE
    return TEMPLATES_BY_SUFFIX.get(path.suffix.lower())


def meta_path_of(path: Path) -> Path:
    return path.with_name(path.name + ".meta")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="生成せずに検査だけ行う")
    args = parser.parse_args()

    if not ASSETS.is_dir():
        print(f"Assets フォルダがありません: {ASSETS}", file=sys.stderr)
        return 1

    missing = []
    unsupported = []
    for path in asset_paths():
        meta = meta_path_of(path)
        if meta.exists():
            continue
        template = template_for(path)
        if template is None:
            unsupported.append(path)
            continue
        missing.append(path)
        if not args.check:
            meta.write_text(template.format(guid=uuid.uuid4().hex), encoding="utf-8", newline="\n")

    orphans = []
    for meta in sorted(ASSETS.rglob("*.meta")):
        if is_ignored(meta):
            continue
        target = meta.with_name(meta.name[: -len(".meta")])
        if not target.exists():
            orphans.append(meta)

    rel = lambda p: p.relative_to(ROOT).as_posix()
    if args.check:
        for path in missing:
            print(f"[missing meta] {rel(path)}")
    else:
        for path in missing:
            print(f"[created] {rel(meta_path_of(path))}")
    for path in unsupported:
        print(f"[unsupported] {rel(path)} (Unity Editor で開いて .meta を生成してください)")
    for meta in orphans:
        print(f"[orphan meta] {rel(meta)}")

    if args.check and (missing or unsupported or orphans):
        return 1
    if orphans:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
