#!/usr/bin/env bash
set -Eeuo pipefail

if [[ "${1:-}" == "--stop" ]]; then
    port="${2:-7010}"
else
    port="${1:-7010}"
fi

state_file="${ESI_AI_STUDIO_WATCHDOG_PID_FILE:-${TMPDIR:-/tmp}/esi-ai-studio-watchdog-${port}.pid}"
health_interval="${STUDIO_WATCHDOG_INTERVAL_SECONDS:-3}"
failure_limit="${STUDIO_WATCHDOG_FAILURE_LIMIT:-60}"
graceful_seconds="${STUDIO_WATCHDOG_GRACEFUL_SECONDS:-5}"
memory_limit_mb="${STUDIO_WATCHDOG_MEMORY_LIMIT_MB:-0}"
health_url="${STUDIO_WATCHDOG_HEALTH_URL:-http://127.0.0.1:${port}/v1/models}"

read_state_value() {
    local key="$1"
    [[ -f "$state_file" ]] || return 0
    awk -F= -v key="$key" '$1 == key { print substr($0, index($0, "=") + 1); exit }' "$state_file"
}

process_start_time() {
    local pid="$1"
    [[ -r "/proc/$pid/stat" ]] || return 1
    awk '{ print $22 }' "/proc/$pid/stat"
}

process_group_id() {
    local pid="$1"
    ps -p "$pid" -o pgid= 2>/dev/null | tr -d ' '
}

process_command_line() {
    local pid="$1"
    [[ -r "/proc/$pid/cmdline" ]] || return 0
    tr '\0' ' ' < "/proc/$pid/cmdline"
}

process_is_owned() {
    local pid="$1"
    local expected_start="$2"
    local expected_command="$3"
    local actual_start
    actual_start="$(process_start_time "$pid" 2>/dev/null || true)"
    [[ -n "$actual_start" && "$actual_start" == "$expected_start" ]] || return 1
    [[ "$(process_command_line "$pid")" == *"$expected_command"* ]] || return 1
    kill -0 "$pid" 2>/dev/null
}

process_children() {
    local parent_pid="$1"
    ps -eo pid=,ppid= | awk -v parent="$parent_pid" '$2 == parent { print $1 }'
}

process_descendants() {
    local parent_pid="$1"
    local child_pid
    while read -r child_pid; do
        [[ -n "$child_pid" ]] || continue
        printf '%s\n' "$child_pid"
        process_descendants "$child_pid"
    done < <(process_children "$parent_pid")
}

write_state() {
    local temporary_state="${state_file}.$$"
    umask 077
    {
        printf 'watchdog_pid=%s\n' "$$"
        printf 'watchdog_start=%s\n' "$watchdog_start"
        printf 'watchdog_pgid=%s\n' "$watchdog_pgid"
        printf 'studio_pid=%s\n' "${studio_pid:-}"
        printf 'studio_start=%s\n' "${studio_start:-}"
        printf 'studio_pgid=%s\n' "${studio_pgid:-}"
    } > "$temporary_state"
    mv -f "$temporary_state" "$state_file"
}

terminate_studio() {
    local target_pid="${studio_pid:-}"
    local target_start="${studio_start:-}"
    local target_pgid="${studio_pgid:-}"
    local child_pid
    local descendants
    local deadline

    [[ "$target_pid" =~ ^[0-9]+$ && "$target_start" =~ ^[0-9]+$ ]] || return 0
    process_is_owned "$target_pid" "$target_start" "Esi.AI.Studio" || return 0

    descendants="$(process_descendants "$target_pid" || true)"
    if [[ "$target_pgid" =~ ^[0-9]+$ && "$target_pgid" != "$watchdog_pgid" && "$(process_group_id "$target_pid")" == "$target_pgid" ]]; then
        kill -TERM -- "-$target_pgid" 2>/dev/null || true
    fi
    kill -TERM "$target_pid" 2>/dev/null || true
    for child_pid in $descendants; do
        kill -TERM "$child_pid" 2>/dev/null || true
    done

    deadline=$((SECONDS + graceful_seconds))
    while (( SECONDS < deadline )); do
        process_is_owned "$target_pid" "$target_start" "Esi.AI.Studio" || return 0
        sleep 0.2
    done

    if [[ "$target_pgid" =~ ^[0-9]+$ && "$target_pgid" != "$watchdog_pgid" ]]; then
        kill -KILL -- "-$target_pgid" 2>/dev/null || true
    fi
    kill -KILL "$target_pid" 2>/dev/null || true
    for child_pid in $descendants; do
        kill -KILL "$child_pid" 2>/dev/null || true
    done
}

find_studio_pid() {
    local candidate
    while read -r candidate; do
        [[ -n "$candidate" ]] || continue
        if [[ "$(process_command_line "$candidate")" == *"Esi.AI.Studio"* ]]; then
            printf '%s\n' "$candidate"
            return 0
        fi
    done < <(ss -ltnpH "sport = :$port" 2>/dev/null | sed -n 's/.*pid=\([0-9][0-9]*\).*/\1/p')
    return 0
}

health_check() {
    command -v curl >/dev/null 2>&1 || return 1
    curl --fail --silent --show-error --max-time 2 "$health_url" >/dev/null 2>&1
}

stop_from_state() {
    local previous_watchdog_pid
    local previous_watchdog_start
    local previous_watchdog_command

    [[ -f "$state_file" ]] || return 0
    previous_watchdog_pid="$(read_state_value watchdog_pid)"
    previous_watchdog_start="$(read_state_value watchdog_start)"
    previous_watchdog_command="$(process_command_line "$previous_watchdog_pid" 2>/dev/null || true)"
    studio_pid="$(read_state_value studio_pid)"
    studio_start="$(read_state_value studio_start)"
    studio_pgid="$(read_state_value studio_pgid)"
    watchdog_pgid="$(read_state_value watchdog_pgid)"

    if [[ "$previous_watchdog_pid" =~ ^[0-9]+$ && "$previous_watchdog_start" =~ ^[0-9]+$ && "$previous_watchdog_command" == *"studio-watchdog.sh"* ]] && process_is_owned "$previous_watchdog_pid" "$previous_watchdog_start" "studio-watchdog.sh"; then
        if [[ "$previous_watchdog_pid" != "$$" ]]; then
            terminate_studio
            kill -TERM "$previous_watchdog_pid" 2>/dev/null || true
        fi
    else
        terminate_studio
    fi
    rm -f "$state_file"
}

if [[ "${1:-}" == "--stop" ]]; then
    stop_from_state
    exit 0
fi

if ! command -v ss >/dev/null 2>&1 || ! command -v curl >/dev/null 2>&1; then
    echo "Studio watchdog requires both ss and curl; refusing to run without health and ownership checks." >&2
    exit 1
fi

watchdog_start="$(process_start_time "$$")"
watchdog_pgid="$(process_group_id "$$")"
studio_pid=""
studio_start=""
studio_pgid=""

if [[ -f "$state_file" ]]; then
    previous_watchdog_pid="$(read_state_value watchdog_pid)"
    previous_watchdog_start="$(read_state_value watchdog_start)"
    if [[ "$previous_watchdog_pid" =~ ^[0-9]+$ && "$previous_watchdog_start" =~ ^[0-9]+$ ]] && process_is_owned "$previous_watchdog_pid" "$previous_watchdog_start" "studio-watchdog.sh"; then
        echo "Studio watchdog is already running for port $port."
        exit 1
    fi
    stop_from_state
    watchdog_pgid="$(process_group_id "$$")"
fi

write_state
cleanup() {
    if [[ -f "$state_file" ]] && [[ "$(read_state_value watchdog_pid)" == "$$" ]] && [[ "$(read_state_value watchdog_start)" == "$watchdog_start" ]]; then
        rm -f "$state_file"
    fi
}
handle_signal() {
    terminate_studio
    exit 143
}
trap cleanup EXIT
trap handle_signal INT TERM

echo "Studio watchdog starting for port $port."
echo "Studio watchdog monitoring port $port."

failure_count=0
seen_studio=false
while true; do
    discovered_studio_pid="$(find_studio_pid)"
    if [[ -z "$discovered_studio_pid" ]]; then
        if [[ "$seen_studio" == true ]]; then
            echo "Studio process is no longer listening; terminating its process group."
            terminate_studio
            exit 0
        fi
        sleep "$health_interval"
        continue
    fi

    if [[ "$seen_studio" == false || "$discovered_studio_pid" != "$studio_pid" ]]; then
        studio_pid="$discovered_studio_pid"
        studio_start="$(process_start_time "$studio_pid")"
        studio_pgid="$(process_group_id "$studio_pid")"
        write_state
        seen_studio=true
    fi

    if health_check; then
        failure_count=0
    else
        failure_count=$((failure_count + 1))
        echo "Studio health check failed ($failure_count/$failure_limit)."
    fi

    if [[ "$memory_limit_mb" =~ ^[0-9]+$ ]] && (( memory_limit_mb > 0 )); then
        memory_kb="$(ps -p "$studio_pid" -o rss= 2>/dev/null | tr -d ' ' || true)"
        if [[ "$memory_kb" =~ ^[0-9]+$ ]] && (( memory_kb > memory_limit_mb * 1024 )); then
            echo "Studio RSS exceeded ${memory_limit_mb} MiB; terminating its process group."
            terminate_studio
            exit 1
        fi
    fi

    if (( failure_count >= failure_limit )); then
        echo "Studio failed health checks for $((failure_limit * health_interval)) seconds; terminating its process group."
        terminate_studio
        exit 1
    fi

    sleep "$health_interval"
done
