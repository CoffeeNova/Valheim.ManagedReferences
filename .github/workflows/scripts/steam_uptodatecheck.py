#!/usr/bin/env python3
import argparse
import json
import os
import urllib.request


def write_output(path: str, **kv):
    with open(path, "a", encoding="utf-8") as f:
        for k, v in kv.items():
            f.write(f"{k}={v}\n")


def to_bool_str(v: bool) -> str:
    return "true" if v else "false"


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--appid", type=int, required=True)
    p.add_argument("--version", type=int, required=True)
    p.add_argument("--timeout", type=int, default=30)
    p.add_argument("--out", default=os.environ.get("GITHUB_OUTPUT", ""))
    p.add_argument("--strict", action="store_true", help="Exit 2 if success=false.")
    args = p.parse_args()

    url = f"https://api.steampowered.com/ISteamApps/UpToDateCheck/v1/?appid={args.appid}&version={args.version}"

    req = urllib.request.Request(
        url,
        headers={
            "User-Agent": "valheim-managed-references/1.0",
            "Accept": "application/json",
        },
        method="GET",
    )

    data = json.loads(urllib.request.urlopen(req, timeout=args.timeout).read().decode("utf-8", "replace"))
    resp = data.get("response", {})

    success = bool(resp.get("success", False))
    up_to_date = bool(resp.get("up_to_date", False))
    version_is_listable = bool(resp.get("version_is_listable", False))
    required_version = resp.get("required_version", "")
    message = resp.get("message", "")

    print(f"url={url}")
    print(json.dumps(resp, ensure_ascii=False, indent=2))

    if args.out:
        write_output(
            args.out,
            success=to_bool_str(success),
            up_to_date=to_bool_str(up_to_date),
            version_is_listable=to_bool_str(version_is_listable),
            required_version=str(required_version),
            message=str(message),
        )

    if args.strict and not success:
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
