#!/bin/bash
# Shows one kind of toast per run on a connected Quest and reports whether the
# shell created an oversized toast swapchain and how far free memory fell.
#
#   toast-probe.sh all                  every kind, control first
#   toast-probe.sh text custom-view     chosen kinds
#
# Kinds:
#   none              control: probe panel open, no toast
#   text              Toast.makeText(app context), LENGTH_SHORT
#   text-long         same, LENGTH_LONG
#   activity-context  Toast.makeText(activity), LENGTH_SHORT
#   custom-view       new Toast + setView (drawn by the app, not SystemUI)
#   background        text toast from a broadcast receiver, no probe window
#                     open; start an immersive app first to test that case
#
# Env: ANDROID_SERIAL picks the headset; SAMPLE_SECONDS (25), RECOVERED_MB (2500).
# A hit can lock the headset for about 2 minutes; runs wait for memory to recover.
set -uo pipefail
cd "$(dirname "$0")"

PKG=com.anaglyph.toastprobe
ALL_KINDS=(none text text-long activity-context custom-view background)
SAMPLE_SECONDS=${SAMPLE_SECONDS:-25}
TOAST_AT_SECOND=3
RECOVERED_MB=${RECOVERED_MB:-2500}
OVERSIZED_SWAPCHAIN_PIXELS=50000000
RUNS_DIR="$(pwd)/out/runs/$(date +%Y%m%d-%H%M%S)"

mem_available_mb() {
    adb shell grep MemAvailable /proc/meminfo 2>/dev/null | awk '{print int($2/1024)}'
}

wait_for_free_memory() {
    local mb
    for second in $(seq 0 300); do
        mb=$(mem_available_mb)
        if [ -n "$mb" ] && [ "$mb" -ge "$RECOVERED_MB" ]; then return 0; fi
        if [ $((second % 15)) -eq 0 ]; then echo "  waiting for free memory: ${mb:-?} MB, need $RECOVERED_MB"; fi
        sleep 1
    done
    echo "  free memory did not recover within 5 minutes"
    return 1
}

prepare() {
    if [ "$1" = background ]; then
        adb shell am force-stop $PKG
        sleep 2
    else
        adb shell am start -W -n $PKG/.ProbeActivity >/dev/null
        sleep 5
    fi
}

show_toast() {
    if [ "$1" = background ]; then
        adb shell am broadcast -f 0x20 -n $PKG/.ToastReceiver --es kind text >/dev/null
    else
        adb shell am start -n $PKG/.ProbeActivity --es kind "$1" >/dev/null
    fi
}

sample_memory_on_device() {
    adb shell "i=0; while [ \$i -lt $SAMPLE_SECONDS ]; do
        read up idle < /proc/uptime
        echo \"\$up \$(grep MemAvailable /proc/meminfo | tr -s ' ' | cut -d' ' -f2)\"
        sleep 1; i=\$((i+1)); done"
}

report() {
    local kind=$1 dir=$2
    local toast_time largest swapchain_line toast_windows lmkd_kills baseline_mb lowest_mb verdict

    toast_time=$(grep -m1 "ToastProbe: shown" "$dir/logcat.txt" | awk '{print $1}')
    if [ -z "$toast_time" ]; then toast_time=0; echo "  probe never logged the toast"; fi

    largest=$(awk -v t="$toast_time" '$1 >= t' "$dir/logcat.txt" \
        | grep -oE "createSwapchain.*size=[0-9]+x[0-9]+|width: [0-9]+, height: [0-9]+" \
        | sed -E 's/.*size=([0-9]+)x([0-9]+)/\1 \2/; s/width: ([0-9]+), height: ([0-9]+)/\1 \2/' \
        | awk '{print $1*$2, $1"x"$2}' | sort -n | tail -1)
    toast_windows=$(awk -v t="$toast_time" '$1 >= t' "$dir/logcat.txt" | grep -c -E "VolumetricToast|TYPE_TOAST")
    lmkd_kills=$(awk -v t="$toast_time" '$1 >= t' "$dir/logcat.txt" | grep -c -E "lowmemorykiller: Kill|lmkd watchdog")
    baseline_mb=$(awk -v t="$toast_time" '$1 < t {mb=int($2/1024)} END {print mb}' "$dir/memory.txt")
    lowest_mb=$(awk -v t="$toast_time" '$1 >= t {mb=int($2/1024); if (min=="" || mb<min) min=mb} END {print min}' "$dir/memory.txt")

    if [ -n "$largest" ] && [ "${largest%% *}" -ge $OVERSIZED_SWAPCHAIN_PIXELS ]; then verdict=HIT
    elif [ "$kind" = none ]; then verdict=control
    elif [ "$toast_windows" -gt 0 ]; then verdict=normal
    else verdict=no-toast-window-logged; fi

    printf "%-17s %-24s swapchain=%-12s toastWindowLines=%-3s freeMB %s -> %s  lmkdLines=%s\n" \
        "$kind" "$verdict" "${largest#* }" "$toast_windows" "${baseline_mb:-?}" "${lowest_mb:-?}" "$lmkd_kills" \
        | tee -a "$RUNS_DIR/summary.txt"
}

run_kind() {
    local kind=$1 dir="$RUNS_DIR/$1"
    mkdir -p "$dir"
    echo "== $kind"
    wait_for_free_memory || return 1
    prepare "$kind"
    wait_for_free_memory || return 1

    adb logcat -c
    adb logcat -v monotonic > "$dir/logcat.txt" &
    local logcat_pid=$!
    sample_memory_on_device > "$dir/memory.txt" &
    local sampler_pid=$!
    sleep $TOAST_AT_SECOND
    show_toast "$kind"
    wait $sampler_pid
    kill $logcat_pid 2>/dev/null; wait $logcat_pid 2>/dev/null

    report "$kind" "$dir"
}

[ $# -eq 0 ] && { sed -n '2,20p' "$0"; exit 1; }
kinds=("$@"); [ "$1" = all ] && kinds=("${ALL_KINDS[@]}")
for kind in "${kinds[@]}"; do
    [[ " ${ALL_KINDS[*]} " == *" $kind "* ]] || { echo "unknown kind $kind"; exit 1; }
done

adb get-state >/dev/null || exit 1
[ -f out/toastprobe.apk ] || ./build.sh || exit 1
adb install -r out/toastprobe.apk >/dev/null || exit 1

# Unworn headsets sleep and stop drawing toasts.
adb shell am broadcast -a com.oculus.vrpowermanager.prox_close >/dev/null
trap 'adb shell am broadcast -a com.oculus.vrpowermanager.automation_disable >/dev/null' EXIT

mkdir -p "$RUNS_DIR"
echo "headset: $(adb shell getprop ro.product.model) Horizon OS $(adb shell getprop ro.vros.build.version), $(adb shell getprop ro.build.version.incremental)"
for kind in "${kinds[@]}"; do run_kind "$kind"; done
adb shell am force-stop $PKG
echo "logs: $RUNS_DIR"
