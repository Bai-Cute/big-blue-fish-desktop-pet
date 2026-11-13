"""Validate the bundled Apple Silicon application on a macOS build host."""
import argparse
import hashlib
import json
import os
import pathlib
import plistlib
import subprocess
import tempfile
import time
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument("app", type=pathlib.Path)
parser.add_argument("--edition", choices=["Standard", "Safe"], required=True)
parser.add_argument("--output", type=pathlib.Path, required=True)
args = parser.parse_args()
app = args.app.resolve()
payload = app / "Contents/MacOS"
args.output.mkdir(parents=True, exist_ok=True)
checks = []


def check(condition, label):
    if not condition:
        raise RuntimeError(label)
    checks.append(label)
    print("PASS", label, flush=True)


def run(*command, **kwargs):
    return subprocess.check_output(command, stderr=subprocess.STDOUT, **kwargs)


info = plistlib.loads((app / "Contents/Info.plist").read_bytes())
expected_version = ET.parse(pathlib.Path(__file__).resolve().parents[1] / "Version.props").findtext(".//Version")
check(info["CFBundleShortVersionString"] == expected_version, "bundle product version")
for name in ["BigBlueFish.Mac", "libcoreclr.dylib", "libhostfxr.dylib",
             "runtime/llama-server", "runtime/BigBlueFish.RunnerGuard",
             "runtime/BigBlueFish.MacOcr", "media/gstreamer/lib/libgstreamer-1.0.0.dylib"]:
    check((payload / name).is_file(), "bundled dependency: " + name)
for name in ["BigBlueFish.Mcp", "BigBlueFish.Control.dll", "BigBlueFish.Agent.dll"]:
    check((payload / name).is_file() == (args.edition == "Standard"), "edition isolation: " + name)
check(len(list((payload / "assets/fish").glob("*.webm"))) == 106, "106 enhanced clips")
check(len(list((payload / "assets/fish/compat").glob("*.webm"))) == 106, "106 compatibility clips")
native = list(payload.rglob("*.dylib")) + [payload / n for n in [
    "BigBlueFish.Mac", "runtime/llama-server", "runtime/BigBlueFish.RunnerGuard", "runtime/BigBlueFish.MacOcr"]]
if args.edition == "Standard":
    native.append(payload / "BigBlueFish.Mcp")
names = {p.name for p in payload.rglob("*") if p.is_file()}
for file in native:
    arch = run("lipo", "-archs", str(file), text=True).strip()
    check("arm64" in arch.split(), "arm64: " + str(file.relative_to(payload)))
    for line in run("otool", "-arch", "arm64", "-L", str(file), text=True).splitlines()[1:]:
        dependency = line.strip().split(" (", 1)[0]
        if dependency.startswith(("/System/Library/", "/usr/lib/")):
            continue
        check(dependency.startswith("@") and pathlib.Path(dependency).name in names,
              "self-contained native dependency: " + dependency)
run("codesign", "--verify", "--deep", "--strict", str(app))
check(True, "application bundle signature verifies")
run(str(payload / "runtime/llama-server"), "--version", timeout=30)
check(True, "bundled llama-server starts")
run(str(payload / "runtime/BigBlueFish.RunnerGuard"), "/usr/bin/true", timeout=10)
check(True, "native runner guard executes a child")

with tempfile.TemporaryDirectory(prefix="bbf-macos-verify-") as temporary:
    data = pathlib.Path(temporary)
    (data / "preferences.json").write_text(json.dumps({"ModelEnabled": False, "PublicInfo": False,
        "CheckUpdatesOnStartup": False, "HideOnFullscreen": False, "Left": 300, "Top": 250}))
    (data / "model-selection.json").write_text('{"Source":"none"}')
    env = {**os.environ, "BIGBLUEFISH_DATA": str(data), "DOTNET_ROOT": "/nonexistent",
           "DOTNET_MULTILEVEL_LOOKUP": "0", "DYLD_LIBRARY_PATH": "", "DYLD_FALLBACK_LIBRARY_PATH": ""}
    with (args.output / "app.log").open("w") as log:
        process = subprocess.Popen([str(payload / "BigBlueFish.Mac"), "--test-mode"], env=env,
                                   stdout=log, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 60
            status = None
            while time.monotonic() < deadline:
                if process.poll() is not None:
                    raise RuntimeError("application exited: " + (args.output / "app.log").read_text())
                try:
                    status = json.loads((data / "test-status.json").read_text())
                except (FileNotFoundError, json.JSONDecodeError):
                    time.sleep(.25)
                    continue
                if status["phase"] != "Startup" and status["frames"] > 3:
                    break
                time.sleep(.25)
            check(status is not None and status["phase"] != "Startup" and status["frames"] > 3,
                  "real application completes startup clip and presents subsequent decoded frames")
            check(status["McpAvailable"] == (args.edition == "Standard"), "running edition matches package")
            (data / "test-command.txt").write_text("render")
            deadline = time.monotonic() + 10
            while not (data / "pet.png").exists() and time.monotonic() < deadline:
                time.sleep(.25)
            check((data / "pet.png").stat().st_size > 1000, "real pet window renders")
            (args.output / "pet.png").write_bytes((data / "pet.png").read_bytes())
            (args.output / "status.json").write_text(json.dumps(status, indent=2))
            (data / "test-command.txt").write_text("exit")
            process.wait(timeout=15)
            check(process.returncode == 0, "application exits normally")
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
(args.output / "checks.json").write_text(json.dumps(checks, indent=2))
