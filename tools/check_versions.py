#!/usr/bin/env python3
"""Fails when the version is not the same everywhere it is written."""
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
props = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
version = re.search(r"<Version>([^<]+)</Version>", props).group(1)
found = {
    "Directory.Build.props AssemblyVersion": re.search(r"<AssemblyVersion>([^<]+)<", props).group(1),
    "Directory.Build.props FileVersion": re.search(r"<FileVersion>([^<]+)<", props).group(1),
    "configPage.html badge": re.search(r'release-badge">v([^<]+)<', (ROOT / "Jellyfin.Plugin.KometaThemes/Configuration/configPage.html").read_text()).group(1),
    "configPage.html stylesheet": re.search(r"KometaThemesCss&amp;v=([0-9.]+)", (ROOT / "Jellyfin.Plugin.KometaThemes/Configuration/configPage.html").read_text()).group(1),
    "kometa.js": re.search(r"const VERSION = '([^']+)'", (ROOT / "Jellyfin.Plugin.KometaThemes/Web/kometa.js").read_text()).group(1),
    "package.json": json.loads((ROOT / "package.json").read_text())["version"],
    "build.yaml": re.search(r'^version: "([^"]+)"', (ROOT / "build.yaml").read_text(), re.M).group(1),
}
wrong = {where: value for where, value in found.items() if value != version}
if wrong:
    sys.exit(f"version {version} expected, found {wrong}")
print(f"version {version} everywhere")
