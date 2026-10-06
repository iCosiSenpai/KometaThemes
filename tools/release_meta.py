#!/usr/bin/env python3
"""Writes the meta.json that goes inside the release zip.

Jellyfin groups installed plugin versions by the name in each folder's meta.json and keeps the
name a package already carries. Shipping the plugin's own name, GUID and version means every
catalog install is recognised as the same plugin, so an update retires the old copy.
"""
import json
import pathlib
import re
import sys
from datetime import datetime, timezone

ROOT = pathlib.Path(__file__).resolve().parent.parent


def read(pattern, text, what):
    match = re.search(pattern, text)
    if not match:
        sys.exit(f"cannot find {what}")
    return match.group(1)


def main(out):
    props = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
    plugin = (ROOT / "Jellyfin.Plugin.KometaThemes" / "Plugin.cs").read_text(encoding="utf-8")
    meta = {
        "category": "Metadata",
        "changelog": "",
        "description": read(r'Description => "([^"]+)"', plugin, "description"),
        "guid": read(r'Guid\.Parse\("([0-9a-f-]{36})"\)', plugin, "GUID"),
        "name": read(r'override string Name => "([^"]+)"', plugin, "name"),
        "overview": "Anime openings and endings on Jellyfin.",
        "owner": "iCosiSenpai",
        "targetAbi": read(r"<JellyfinTargetAbi>([0-9.]+)</JellyfinTargetAbi>", props, "target ABI"),
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "version": read(r"<Version>([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)</Version>", props, "version"),
    }
    pathlib.Path(out).write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(meta, indent=2))


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "meta.json")
