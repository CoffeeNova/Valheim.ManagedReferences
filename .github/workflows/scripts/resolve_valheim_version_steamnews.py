#!/usr/bin/env python3
import argparse
import json
import re
import sys
import urllib.request


def write_github_output(path: str, kv: dict):
    with open(path, "a", encoding="utf-8") as f:
        for k, v in kv.items():
            f.write(f"{k}={v}\n")


def fetch_json(url: str, timeout: int = 30) -> dict:
    req = urllib.request.Request(
        url,
        headers={
            "User-Agent": (
                "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 "
                "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            ),
            "Accept": "application/json,text/plain,*/*",
        },
        method="GET",
    )
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8", "replace"))


def to_minor(ver: str) -> str:
    # 0.221.4 -> "2214"
    parts = ver.split(".")
    return f"{parts[1]}{parts[2]}" if len(parts) == 3 else ""


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--appid", type=int, required=True)
    p.add_argument("--buildid", type=int, default=0, help="Kept for compatibility; not used.")
    p.add_argument("--count", type=int, default=20)
    p.add_argument("--timeout", type=int, default=30)
    p.add_argument("--out", help="Write GitHub step outputs to this file (e.g. $GITHUB_OUTPUT).")
    p.add_argument("--strict", action="store_true", help="Exit 2 if unresolved.")
    args = p.parse_args()

    url = (
        "https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/"
        f"?appid={args.appid}&count={args.count}&maxlength=0&format=json"
    )

    def finish(resolved: bool, ver="", minor="", source="", error=""):
        if args.out:
            write_github_output(
                args.out,
                {
                    "resolved": "true" if resolved else "false",
                    "valheim_version": ver,
                    "valheim_minor": minor,
                    "source": source,
                    "error": error,
                },
            )
        if not resolved and args.strict:
            return 2
        return 0

    try:
        data = fetch_json(url, timeout=args.timeout)
    except Exception as e:
        msg = f"Failed to fetch Steam news API: {e}"
        print(f"WARNING: {msg}", file=sys.stderr)
        return finish(False, error=msg)

    items = (((data or {}).get("appnews") or {}).get("newsitems")) or []
    if not items:
        msg = "No news items returned by Steam API."
        print(f"WARNING: {msg}", file=sys.stderr)
        return finish(False, error=msg)

    # Prefer patchnotes-ish posts (feedlabel/title varies)
    def is_patchnotes(it: dict) -> bool:
        fl = (it.get("feedlabel") or "").lower()
        title = (it.get("title") or "").lower()
        return ("patch" in fl) or ("patch" in title) or ("hotfix" in title)

    candidates = [it for it in items if is_patchnotes(it)] or items

    # Find first semver-like version in title (0.xxx.y)
    for it in candidates:
        title = (it.get("title") or "").strip()
        m = re.search(r"\b\d+\.\d+\.\d+\b", title)
        if not m:
            continue
        ver = m.group(0)
        minor = to_minor(ver)
        print(f"Resolved from Steam News: {title} -> {ver} (minor={minor})")
        return finish(True, ver=ver, minor=minor, source="steamnews")

    msg = "Could not parse version from Steam news titles."
    print(f"WARNING: {msg}", file=sys.stderr)
    return finish(False, error=msg)


if __name__ == "__main__":
    raise SystemExit(main())
