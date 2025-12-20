#!/usr/bin/env python3
import argparse
import os
import re


def write_output(path: str, **kv):
    with open(path, "a", encoding="utf-8") as f:
        for k, v in kv.items():
            f.write(f"{k}={v}\n")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--valheim-dir", required=True)
    p.add_argument("--appid", required=True)
    p.add_argument("--out", default=os.environ.get("GITHUB_OUTPUT", ""))
    args = p.parse_args()

    manifest = os.path.join(args.valheim_dir, "steamapps", f"appmanifest_{args.appid}.acf")

    build = "0"
    if os.path.isfile(manifest):
        text = open(manifest, "r", encoding="utf-8", errors="ignore").read()
        m = re.search(r'"buildid"\s+"([^"]+)"', text)
        if m:
            build = m.group(1)

    print(f"manifest={manifest}")
    print(f"build={build}")

    if args.out:
        write_output(args.out, build=build)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
