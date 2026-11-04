"""Shared parent-lifetime handling for managed Python backend bridges."""

from __future__ import annotations

import asyncio
import contextlib
import os
import signal
import sys
import threading


def install_parent_lifetime_handlers(stopped: asyncio.Event) -> None:
    loop = asyncio.get_running_loop()

    for signum in (signal.SIGINT, signal.SIGTERM):
        with contextlib.suppress(NotImplementedError):
            loop.add_signal_handler(signum, stopped.set)

    if os.environ.get("ESI_PARENT_LIFETIME_PIPE") == "1":
        if sys.platform != "win32":
            input_descriptor = sys.stdin.fileno()

            def handle_parent_pipe() -> None:
                if os.read(input_descriptor, 1) == b"":
                    loop.remove_reader(input_descriptor)
                    stopped.set()

            loop.add_reader(input_descriptor, handle_parent_pipe)
        else:
            def watch_parent_pipe() -> None:
                try:
                    sys.stdin.buffer.read()
                finally:
                    loop.call_soon_threadsafe(stopped.set)

            threading.Thread(target=watch_parent_pipe, daemon=True).start()
