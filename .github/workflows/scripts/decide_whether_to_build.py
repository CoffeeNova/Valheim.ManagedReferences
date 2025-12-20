#!/usr/bin/env python3
import argparse
import os


def write_output(path: str, **kv):
    with open(path, "a", encoding="utf-8") as f:
        for k, v in kv.items():
            f.write(f"{k}={v}\n")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--event", required=True)
    p.add_argument("--up-to-date", default="false")
    p.add_argument("--installed-build", default="0")
    p.add_argument("--updated-build", default="")
    p.add_argument("--out", default=os.environ.get("GITHUB_OUTPUT", ""))
    args = p.parse_args()

    event = args.event.strip()
    up_to_date = (args.up_to_date.strip().lower() == "true")
    installed_build = (args.installed_build or "").strip() or "0"
    updated_build = (args.updated_build or "").strip()

    current_build = updated_build or installed_build or "0"

    manual = (event == "workflow_dispatch")
    build = manual or (not up_to_date)

    print(
        f"event={event} manual={manual} up_to_date={up_to_date} "
        f"installed={installed_build} updated={updated_build} => build={build} current_build={current_build}"
    )

    if args.out:
        write_output(
            args.out,
            build=("true" if build else "false"),
            valheim_build=current_build,
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
