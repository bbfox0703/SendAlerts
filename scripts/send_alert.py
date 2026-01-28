#!/usr/bin/env python3
"""
TC2-3: Python script to send alert messages to SendAlerts Alert Center via Named Pipe.

Usage:
    python send_alert.py --group Critical --message "GPU Temperature: 95°C"
    python send_alert.py -g Warning -m "CPU usage high"
    python send_alert.py --group Info

Examples:
    # Basic alert
    python send_alert.py --group Critical

    # Alert with custom message
    python send_alert.py --group Warning --message "Memory usage: 90%"

    # Test connection
    python send_alert.py --group Default --message "Test alert from Python"

Requirements:
    - Python 3.6+
    - pywin32 (Windows only, for Named Pipe support)

Install:
    pip install pywin32
"""

import argparse
import json
import sys
import time

# Constants
PIPE_NAME = r"\\.\pipe\sendalerts-pipe"
DEFAULT_TIMEOUT = 3000  # milliseconds


def send_alert_windows(group_name: str, message: str = None, timeout_ms: int = DEFAULT_TIMEOUT) -> bool:
    """Send alert via Named Pipe on Windows."""
    try:
        import win32file
        import win32pipe
        import pywintypes
    except ImportError:
        print("[send_alert] ERROR: pywin32 not installed. Run: pip install pywin32")
        return False

    # Build JSON message
    payload = {"GroupName": group_name}
    if message:
        payload["CustomMessage"] = message

    json_message = json.dumps(payload, ensure_ascii=False)

    print(f"[send_alert] Sending to pipe: {PIPE_NAME}")
    print(f"[send_alert] Message: {json_message}")

    try:
        # Wait for pipe to be available
        timeout_sec = timeout_ms / 1000.0
        start_time = time.time()

        while True:
            try:
                # Try to open the pipe
                handle = win32file.CreateFile(
                    PIPE_NAME,
                    win32file.GENERIC_WRITE,
                    0,  # No sharing
                    None,  # Default security
                    win32file.OPEN_EXISTING,
                    0,  # Default attributes
                    None  # No template
                )
                break
            except pywintypes.error as e:
                if e.winerror == 2:  # ERROR_FILE_NOT_FOUND - pipe doesn't exist
                    if time.time() - start_time > timeout_sec:
                        print("[send_alert] ERROR: Connection timeout. Is SendAlerts running?")
                        return False
                    time.sleep(0.1)
                    continue
                raise

        # Write message
        win32file.WriteFile(handle, json_message.encode('utf-8'))
        win32file.CloseHandle(handle)

        print("[send_alert] Message sent successfully!")
        return True

    except pywintypes.error as e:
        print(f"[send_alert] ERROR: {e.strerror}")
        return False
    except Exception as e:
        print(f"[send_alert] ERROR: {e}")
        return False


def send_alert_posix(group_name: str, message: str = None, timeout_ms: int = DEFAULT_TIMEOUT) -> bool:
    """Send alert via Named Pipe on Linux/macOS (FIFO)."""
    import os

    # On POSIX, Named Pipes are FIFO files
    # Note: This requires the pipe to be created as a FIFO
    pipe_path = "/tmp/sendalerts-pipe"

    if not os.path.exists(pipe_path):
        print(f"[send_alert] ERROR: Pipe not found at {pipe_path}")
        print("[send_alert] Note: Linux/macOS support requires FIFO pipe setup")
        return False

    # Build JSON message
    payload = {"GroupName": group_name}
    if message:
        payload["CustomMessage"] = message

    json_message = json.dumps(payload, ensure_ascii=False)

    print(f"[send_alert] Sending to pipe: {pipe_path}")
    print(f"[send_alert] Message: {json_message}")

    try:
        with open(pipe_path, 'w') as pipe:
            pipe.write(json_message)

        print("[send_alert] Message sent successfully!")
        return True
    except Exception as e:
        print(f"[send_alert] ERROR: {e}")
        return False


def main():
    parser = argparse.ArgumentParser(
        description="Send alert to SendAlerts Alert Center",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
    %(prog)s --group Critical --message "GPU overheating!"
    %(prog)s -g Warning -m "High memory usage"
    %(prog)s --group Info
        """
    )
    parser.add_argument(
        "-g", "--group",
        required=True,
        help="Alert Group name (e.g., Critical, Warning, Info, Default)"
    )
    parser.add_argument(
        "-m", "--message",
        default=None,
        help="Custom alert message (optional)"
    )
    parser.add_argument(
        "-t", "--timeout",
        type=int,
        default=DEFAULT_TIMEOUT,
        help=f"Connection timeout in milliseconds (default: {DEFAULT_TIMEOUT})"
    )

    args = parser.parse_args()

    # Detect platform and send alert
    if sys.platform == "win32":
        success = send_alert_windows(args.group, args.message, args.timeout)
    else:
        success = send_alert_posix(args.group, args.message, args.timeout)

    sys.exit(0 if success else 1)


if __name__ == "__main__":
    main()
