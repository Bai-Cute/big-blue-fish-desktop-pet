#!/usr/bin/env python3
"""Smoke-test a real Mac app with isolated settings; never touches user data."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time

app = Path(sys.argv[1]).resolve()
with tempfile.TemporaryDirectory(prefix="bbf-mac-test-") as directory:
    data = Path(directory)
    env = dict(os.environ, BIGBLUEFISH_DATA=directory)
    with (data / "stdout.log").open("w") as log:
        process = subprocess.Popen([str(app / "Contents/MacOS/BigBlueFish.Mac"), "--test-mode"], env=env, stdout=log, stderr=subprocess.STDOUT)
        def wait_for(predicate, timeout=20):
            deadline = time.monotonic() + timeout
            last_status = None
            while time.monotonic() < deadline:
                if process.poll() is not None:
                    raise AssertionError((data / "stdout.log").read_text())
                try:
                    status = json.loads((data / "test-status.json").read_text())
                    last_status = status
                    if predicate(status):
                        return status
                except (FileNotFoundError, json.JSONDecodeError):
                    pass
                time.sleep(.1)
            raise AssertionError(f"Timed out waiting for app state; last status: {last_status}")
        def command(text):
            pending = data / "test-command.pending"
            pending.write_text(text)
            pending.replace(data / "test-command.txt")
        try:
            status = wait_for(lambda s: s["frames"] > 5)
            assert status["decoderError"] == "", status
            assert status["handle"] == "NSWindow", status
            assert status["width"] > 0 and status["height"] > 0, status
            assert status["boundsWidth"] > 0 and status["boundsHeight"] > 0, status
            assert status["bubbleWidth"] > 0 and status["bubbleHeight"] > 0, status
            expected_bubble_y = status["y"] + int(status["height"] * status["desktopScaling"] * .16) - 110
            assert abs(status["bubbleY"] - expected_bubble_y) <= 2, status
            print("PASS: real AppKit window and decoded animation", status)
            with tempfile.TemporaryDirectory(prefix="bbf-mac-duplicate-") as duplicate_directory:
                duplicate_data = Path(duplicate_directory)
                duplicate_env = dict(os.environ, BIGBLUEFISH_DATA=duplicate_directory)
                duplicate = None
                with (duplicate_data / "stdout.log").open("w") as duplicate_log:
                    duplicate = subprocess.Popen(
                        [str(app / "Contents/MacOS/BigBlueFish.Mac"), "--test-mode"],
                        env=duplicate_env,
                        stdout=duplicate_log,
                        stderr=subprocess.STDOUT,
                    )
                    try:
                        duplicate_exit = duplicate.wait(timeout=5)
                    except subprocess.TimeoutExpired as error:
                        raise AssertionError("A second launch stayed running") from error
                    finally:
                        if duplicate.poll() is None:
                            try:
                                duplicate.terminate()
                            except ProcessLookupError:
                                pass
                            try:
                                duplicate.wait(timeout=5)
                            except subprocess.TimeoutExpired:
                                try:
                                    duplicate.kill()
                                except ProcessLookupError:
                                    pass
                                duplicate.wait(timeout=5)
                assert duplicate_exit == 0, (duplicate_exit, (duplicate_data / "stdout.log").read_text())
                assert not (duplicate_data / "test-status.json").exists(), "A duplicate launch created a second app instance"
            assert process.poll() is None, "The duplicate launch terminated the primary app"
            print("PASS: duplicate launch exits without creating another instance")
            if not os.environ.get("BIGBLUEFISH_SKIP_MENU_FOCUS_TEST"):
                subprocess.run(["open", "-a", str(app)], check=True)
                wait_for(lambda s: s["applicationActive"])
                command("context-menu")
                status = wait_for(lambda s: s["contextMenuOpen"])
                assert status["applicationActive"] and status["contextMenuOpenedWhileAppActive"], status
                subprocess.run(["open", "-a", "Finder"], check=True)
                wait_for(lambda s: not s["applicationActive"] and not s["contextMenuOpen"])
                subprocess.run(["open", "-a", str(app)], check=True)
                print("PASS: right-click context menu closes when the app loses focus")
            chat_settings = data / "mac-chat-settings.json"
            assert not chat_settings.exists(), "Opening the app must not save chat settings"
            command("settings")
            status = wait_for(lambda s: s["settingsOpen"])
            assert status["settingsOpacity"] == 1, status
            background = status["settingsBackground"].lower()
            assert background == "white" or "ffffff" in background, status
            assert not chat_settings.exists(), "Opening settings must not save a draft"
            command("close-settings")
            wait_for(lambda s: not s["settingsOpen"])
            assert not chat_settings.exists(), "Closing settings without Save must discard the draft"
            command("settings")
            wait_for(lambda s: s["settingsOpen"])
            command("save-settings")
            wait_for(lambda s: not s["settingsOpen"] and chat_settings.exists())
            saved_chat = json.loads(chat_settings.read_text())
            assert saved_chat["Backend"] == "api", saved_chat
            assert "apiKey" not in chat_settings.read_text().lower(), "API Key must remain in memory"
            command("agent")
            status = wait_for(lambda s: s["agentOpen"])
            assert status["chatWidth"] >= 420 and status["chatHeight"] >= 480, status
            assert 0.55 <= status["chatOpacity"] <= 1, status
            assert not status["agentConnected"], "Agent must stay disconnected without explicit consent"
            screenshot_path = os.environ.get("BIGBLUEFISH_TEST_SCREENSHOT_PATH")
            if screenshot_path:
                subprocess.run(["screencapture", "-x", screenshot_path], check=True)
            command("hide")
            wait_for(lambda s: s["hidden"])
            command("show")
            status = wait_for(lambda s: not s["hidden"])
            assert status["boundsWidth"] > 0 and status["boundsHeight"] > 0, status
            children = subprocess.check_output(["pgrep", "-P", str(process.pid)], text=True).split()
            command("exit")
            assert process.wait(timeout=20) == 0
            for child in children:
                result = subprocess.run(["ps", "-p", child, "-o", "comm="], capture_output=True, text=True)
                assert result.returncode != 0, "Decoder still alive: " + result.stdout
            settings = json.loads((data / "preferences.json").read_text())
            assert settings["Left"] is not None and settings["Top"] is not None
            assert not (data / "companion-errors.log").exists()
            print("PASS: settings, hide/show, saved position, graceful exit, no remaining decoder")
        finally:
            if process.poll() is None:
                process.terminate()
                process.wait(timeout=5)
