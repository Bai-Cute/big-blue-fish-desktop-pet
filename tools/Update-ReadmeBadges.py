#!/usr/bin/env python3
"""Generate README Release metrics from GitHub's public counters, excluding checksum files.

Download totals cover all published releases, including prereleases. Latest means
GitHub's latest official release. Windows standard includes historical OCR/Vision
packages, excluding Safe editions. Nothing is downloaded from release assets.
"""

import argparse
from datetime import datetime, timezone
from html import escape
import json
import os
from pathlib import Path
import re
import time
from urllib.error import HTTPError, URLError
from urllib.parse import quote
from urllib.request import Request, urlopen


REPOSITORY = "Bai-Cute/big-blue-fish-desktop-pet"
PACKAGE_SUFFIXES = (".exe", ".msi", ".deb", ".dmg", ".zip", ".appimage", ".tar.gz", ".tar.xz")


def api(path):
    headers = {"Accept": "application/vnd.github+json", "User-Agent": "big-blue-fish-readme-badges"}
    token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")
    if token:
        headers["Authorization"] = "Bearer " + token
    for attempt in range(3):
        try:
            with urlopen(Request("https://api.github.com/" + path, headers=headers), timeout=30) as response:
                return json.load(response)
        except HTTPError as error:
            if attempt == 2 or error.code not in (429, 500, 502, 503, 504):
                raise RuntimeError(f"GitHub API returned HTTP {error.code} for {path}") from None
        except (URLError, TimeoutError):
            if attempt == 2:
                raise RuntimeError(f"GitHub API connection failed for {path}") from None
        time.sleep(2 ** attempt)


def read_releases(repository):
    releases = []
    page = 1
    while True:
        batch = api(f"repos/{repository}/releases?per_page=100&page={page}")
        releases.extend(r for r in batch if not r["draft"])
        if len(batch) < 100:
            return releases
        page += 1


def package(asset):
    name = asset["name"].lower()
    return asset.get("state") == "uploaded" and name.startswith("bigbluefish") and name.endswith(PACKAGE_SUFFIXES)


def windows_standard(asset):
    name = asset["name"].lower()
    windows = name.endswith((".exe", ".msi")) or name.endswith(".zip") and bool(re.search(r"win(?:dows|[-_])", name))
    return package(asset) and windows and "safe" not in name


def summarize(releases, latest):
    # A release published between the two API reads must still be included once.
    releases = list({r["id"]: r for r in [*releases, latest] if not r["draft"]}.values())
    rows = []
    for release in releases:
        assets = [a for a in release["assets"] if package(a)]
        rows.append({"id": release["id"], "tag": release["tag_name"],
                     "downloads": sum(a["download_count"] for a in assets),
                     "windowsStandardDownloads": sum(a["download_count"] for a in assets if windows_standard(a)),
                     "url": release["html_url"]})
    highest = max(rows, key=lambda row: (row["downloads"], row["tag"]))
    latest_row = next(row for row in rows if row["id"] == latest["id"])
    stable = sorted((r for r in releases if not r["prerelease"] and r["id"] != latest["id"]),
                    key=lambda r: r["published_at"], reverse=True)
    windows_release = next((r for r in [latest, *stable] if any(windows_standard(a) for a in r["assets"])), None)
    if windows_release is None:
        raise ValueError("No published Windows standard package found")
    windows = sorted((a for a in windows_release["assets"] if windows_standard(a)),
                     key=lambda a: (not a["name"].lower().endswith("-ocr-setup.exe"), a["name"]))[0]
    return {"repository": REPOSITORY, "totalDownloads": sum(r["downloads"] for r in rows),
            "windowsStandardDownloads": sum(r["windowsStandardDownloads"] for r in rows),
            "highestRelease": highest, "latestRelease": latest_row,
            "latestWindowsStandard": {"tag": windows_release["tag_name"], "name": windows["name"],
                                      "bytes": windows["size"], "url": windows["browser_download_url"]},
            "releases": sorted(rows, key=lambda row: row["tag"])}


def repository_metrics():
    metadata = api(f"repos/{REPOSITORY}")
    counts = {}
    for name, state in (("openIssues", " is:open"), ("allIssues", "")):
        result = api("search/issues?q=" + quote(f"repo:{REPOSITORY} is:issue{state}", safe="") + "&per_page=1")
        if result.get("incomplete_results"):
            raise ValueError("GitHub returned incomplete issue statistics")
        counts[name] = result["total_count"]
    license_info = metadata.get("license") or {}
    return {"stars": metadata["stargazers_count"], "license": license_info.get("spdx_id") or "unknown",
            "repositoryBytes": metadata["size"] * 1024, **counts}


def size_text(size):
    return f"{size / 1024 ** 3:.1f} GiB" if size >= 1024 ** 3 else f"{size / 1024 ** 2:.1f} MiB"


def badge(label, message, color, description):
    # Approximate glyph widths with room for Chinese fallback fonts and SVG antialiasing.
    def width(text):
        return round(sum(12 if ord(c) > 255 else 7 for c in text)) + 20
    left, right = width(label), width(message)
    total = left + right
    label, message, description = escape(label), escape(message), escape(description, quote=True)
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{total}" height="20" role="img" aria-label="{description}">\n'
            f'  <title>{description}</title>\n'
            '  <linearGradient id="s" x2="0" y2="100%"><stop offset="0" stop-color="#fff" stop-opacity=".1"/><stop offset="1" stop-opacity=".1"/></linearGradient>\n'
            f'  <clipPath id="r"><rect width="{total}" height="20" rx="3" fill="#fff"/></clipPath>\n'
            f'  <g clip-path="url(#r)"><rect width="{left}" height="20" fill="#555"/><rect x="{left}" width="{right}" height="20" fill="{color}"/><rect width="{total}" height="20" fill="url(#s)"/></g>\n'
            '  <g fill="#fff" text-anchor="middle" font-family="Verdana,Arial,DejaVu Sans,Microsoft YaHei,sans-serif" font-size="11">\n'
            f'    <text x="{left / 2}" y="15" fill="#010101" fill-opacity=".3">{label}</text><text x="{left / 2}" y="14">{label}</text>\n'
            f'    <text x="{left + right / 2}" y="15" fill="#010101" fill-opacity=".3">{message}</text><text x="{left + right / 2}" y="14">{message}</text>\n'
            '  </g>\n</svg>\n')


def write_badges(summary, output):
    highest, latest = summary["highestRelease"], summary["latestRelease"]
    windows = summary["latestWindowsStandard"]
    size = size_text(windows["bytes"])
    definitions = [
        ("downloads", "总下载", f'{summary["totalDownloads"]:,}', "#4c1", "全部公开Release发行包累计下载次数"),
        ("windows-standard-downloads", "Windows标准版下载", f'{summary["windowsStandardDownloads"]:,}', "#007ec6", "全部公开Release的Windows标准版发行包累计下载次数"),
        ("highest-release-downloads", "最高单版本下载", f'{highest["tag"]} · {highest["downloads"]:,}', "#8a2be2", "历史单个Release发行包累计下载次数的最大值"),
        ("latest-release-downloads", "最新版本下载", f'{latest["tag"]} · {latest["downloads"]:,}', "#5c6bc0", "GitHub最新正式Release的发行包累计下载次数"),
        ("release-size", "release size", size, "#007ec6", f'最新Windows标准版：{windows["tag"]}，{windows["name"]}，{windows["bytes"]:,} bytes'),
        ("latest-version", "最新版", latest["tag"], "#007ec6", "GitHub最新正式Release版本号"),
        ("stars", "Stars", f'{summary["stars"]:,}', "#dfb317", "GitHub Stars"),
        ("license", "License", summary["license"], "#fe7d37", "代码许可证"),
        ("repo-size", "repo size", size_text(summary["repositoryBytes"]), "#007ec6", "GitHub报告的仓库大小，包含Git历史"),
        ("issues-open", "issues open", f'{summary["openIssues"]:,}', "#dfb317", "未关闭的Issue数量，不含Pull Request"),
        ("issues-all", "issues all", f'{summary["allIssues"]:,}', "#007ec6", "全部Issue数量，包括已关闭的Issue，不含Pull Request"),
    ]
    files = {name + ".svg": badge(label, message, color, description + "：" + message)
             for name, label, message, color, description in definitions}
    stats = output / "stats.json"
    if stats.exists():
        previous = json.loads(stats.read_text(encoding="utf-8"))
        if {k: v for k, v in previous.items() if k != "updatedAt"} == summary:
            summary = previous
    if "updatedAt" not in summary:
        summary = {**summary, "updatedAt": datetime.now(timezone.utc).isoformat(timespec="seconds")}
    files["stats.json"] = json.dumps(summary, ensure_ascii=False, indent=2) + "\n"
    output.mkdir(parents=True, exist_ok=True)
    for name, text in files.items():
        path = output / name
        if not path.exists() or path.read_text(encoding="utf-8") != text:
            temporary = output / (name + ".tmp")
            temporary.write_text(text, encoding="utf-8", newline="\n")
            temporary.replace(path)
    for _, label, message, _, _ in definitions:
        print(f"{label}: {message}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / ".work/readme-badges/generated")
    args = parser.parse_args()
    releases = read_releases(REPOSITORY)
    latest = api(f"repos/{REPOSITORY}/releases/latest")
    write_badges({**summarize(releases, latest), **repository_metrics()}, args.output)


if __name__ == "__main__":
    main()
