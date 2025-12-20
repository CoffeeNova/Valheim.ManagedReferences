#!/usr/bin/env python3
import argparse
import os
import re
import shutil
import subprocess


def write_output(path: str, **kv):
    with open(path, "a", encoding="utf-8") as f:
        for k, v in kv.items():
            f.write(f"{k}={v}\n")


def run_list_sdks(dotnet_path: str) -> str:
    p = subprocess.run([dotnet_path, "--list-sdks"], capture_output=True, text=True)
    return (p.stdout or "") + ("\n" + p.stderr if p.stderr else "")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--out", default=os.environ.get("GITHUB_OUTPUT", ""))
    p.add_argument("--dotnet-install-dir", default=os.environ.get("DOTNET_INSTALL_DIR", ""))
    args = p.parse_args()

    candidates = []
    if shutil.which("dotnet"):
        candidates.append(shutil.which("dotnet"))
    if args.dotnet_install_dir:
        candidates.append(os.path.join(args.dotnet_install_dir, "dotnet"))

    candidates = [c for c in candidates if c and os.path.exists(c)]

    found_dotnet = ""
    found_sdk10 = False
    found_sdk10_version = ""

    for dotnet in candidates:
        try:
            text = run_list_sdks(dotnet)
        except Exception:
            continue

        m = re.search(r"^(10\.[0-9]+\.[0-9]+[^\s]*)\s+\[", text, flags=re.MULTILINE)
        if m:
            found_dotnet = dotnet
            found_sdk10 = True
            found_sdk10_version = m.group(1)
            break

        if not found_dotnet:
            found_dotnet = dotnet

    print(f"dotnet_path={found_dotnet}")
    print(f"dotnet10_installed={found_sdk10}")
    print(f"dotnet10_version={found_sdk10_version}")

    if args.out:
        write_output(
            args.out,
            dotnet_path=found_dotnet,
            dotnet10_installed=("true" if found_sdk10 else "false"),
            dotnet10_version=found_sdk10_version,
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
