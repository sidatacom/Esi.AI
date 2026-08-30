#!/usr/bin/env bash
set -u

port="${2:-7010}"
state_file="${TMPDIR:-/tmp}/esi-ai-studio-watchdog-${port}.pid"

if [[ "${1:-}" == "--stop" ]]; then
    if [[ -f "$state_file" ]]; then
        watchdog_pid="$(<"$state_file")"
        if [[ "$watchdog_pid" =~ ^[0-9]+$ ]]; then
            kill "$watchdog_pid" 2>/dev/null || true
        fi
        rm -f "$state_file"
    fi
    exit 0
fi

port="${1:-7010}"
state_file="${TMPDIR:-/tmp}/esi-ai-studio-watchdog-${port}.pid"
health_interval="${STUDIO_WATCHDOG_INTERVAL_SECONDS:-3}"
failure_limit="${STUDIO_WATCHDOG_FAILURE_LIMIT:-60}"
memory_limit_mb="${STUDIO_WATCHDOG_MEMORY_LIMIT_MB:-0}"

if [[ -f "$state_file" ]]; then
    previous_watchdog_pid="$(<"$state_file")"
    if [[ "$previous_watchdog_pid" =~ ^[0-9]+$ ]] && kill -0 "$previous_watchdog_pid" 2>/dev/null; then
        echo "Studio watchdog is already running for port $port."
        exit 1
    fi
fi

printf '%s\n' "$$" > "$state_file"
cleanup() {
    rm -f "$state_file"
}
trap cleanup EXIT INT TERM

echo "Studio watchdog starting for port $port."
echo "Studio watchdog monitoring port $port."

failure_count=0
seen_studio=false

while true; do
    studio_pid="$(ss -ltnpH "sport = :$port" 2>/dev/null | sed -n 's/.*pid=\([0-9][0-9]*\).*/\1/p' | head -n 1)"

    if [[ -z "$studio_pid" ]]; then
        if [[ "$seen_studio" == true ]]; then
            echo "Studio process is no longer listening; watchdog stopping."
            exit 0
        fi
        sleep "$health_interval"
        continue
    fi

    seen_studio=true
    command_line="$(ps -p "$studio_pid" -o args= 2>/dev/null || true)"
    if [[ "$command_line" != *"Esi.AI.Studio"* ]]; then
        echo "Refusing to monitor unexpected process $studio_pid on port $port."
        exit 1
    fi

    if kill -0 "$studio_pid" 2>/dev/null; then
        failure_count=0
    else
        failure_count=$((failure_count + 1))
        echo "Studio process check failed ($failure_count/$failure_limit)."
    fi

    if [[ "$memory_limit_mb" =~ ^[0-9]+$ ]] && (( memory_limit_mb > 0 )); then
        memory_kb="$(ps -p "$studio_pid" -o rss= 2>/dev/null | tr -d ' ' || true)"
        if [[ "$memory_kb" =~ ^[0-9]+$ ]] && (( memory_kb > memory_limit_mb * 1024 )); then
            echo "Studio RSS exceeded ${memory_limit_mb} MiB; terminating process $studio_pid."
            kill -TERM "$studio_pid" 2>/dev/null || true
            sleep 5
            kill -KILL "$studio_pid" 2>/dev/null || true
            exit 1
        fi
    fi

    if (( failure_count >= failure_limit )); then
        echo "Studio did not respond for $((failure_limit * health_interval)) seconds; terminating process $studio_pid."
        kill -TERM "$studio_pid" 2>/dev/null || true
        sleep 5
        kill -KILL "$studio_pid" 2>/dev/null || true
        exit 1
    fi

    sleep "$health_interval"
done
