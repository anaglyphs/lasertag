#!/bin/bash
# Builds this working tree (uncommitted and new files included) as an iOS development build and runs it
# on an iPhone: batch Unity in ../lasertag_build, then Xcode, then install and launch.
# Your open editor is untouched.
#
# Usage: Tools/ios-device.sh [device name or UDID]      (the first connected iPhone when none given)
# Needs: the phone paired with this Mac (USB or same Wi-Fi) with Developer Mode on, and Xcode signed in
# to APPLE_TEAM_ID's team (Tools/release/release.config).
set -Eeuo pipefail
trap 'echo "ios-device: stopped unexpectedly at line $LINENO: $BASH_COMMAND" >&2' ERR

project_dir="$(cd "$(dirname "$0")/.." && pwd)"
fail() { echo "ios-device: $*" >&2; exit 1; }
source "$project_dir/Tools/release/release.config"
source "$project_dir/Tools/lib/unity-batch.sh"

profile="Assets/Settings/Build Profiles/iOS.asset"
xcode_output="Builds/Dev/iOS"
log_dir="$build_logs_dir/ios-dev-$(date +%Y%m%d-%H%M%S)"

[ -n "$APPLE_TEAM_ID" ] || fail "set APPLE_TEAM_ID in Tools/release/release.config"

devices_json="$(mktemp)"
xcrun devicectl list devices --json-output "$devices_json" >/dev/null
read -r udid device_name < <(python3 -I - "$devices_json" "${1:-}" <<'PY'
import json, sys
wanted = sys.argv[2]
for device in json.load(open(sys.argv[1]))["result"]["devices"]:
	hardware, properties = device["hardwareProperties"], device["deviceProperties"]
	if hardware.get("platform") != "iOS" or hardware.get("reality") != "physical": continue
	if wanted and wanted not in (hardware.get("udid"), properties.get("name")): continue
	if not wanted and device["connectionProperties"].get("tunnelState") == "unavailable": continue
	print(hardware["udid"], properties.get("name", ""))
	break
PY
) || true
rm -f "$devices_json"
[ -n "${udid:-}" ] || fail "no ${1:+matching }iPhone connected (see 'xcrun devicectl list devices')"
echo "ios-device: building for $device_name ($udid)"

snapshot="$(working_tree_snapshot)"
tree="$(git -C "$project_dir" rev-parse "$snapshot^{tree}")"
dir="$(prepare_build_worktree "$snapshot")"
mkdir -p "$log_dir"

xcode_dir="$dir/$xcode_output"
built_tree_stamp="$dir/Builds/Dev/ios.built-from"
if [ -d "$xcode_dir" ] && [ "$(cat "$built_tree_stamp" 2>/dev/null || true)" = "$tree" ]; then
	echo "ios-device: no file changes since the last Unity build, reusing its Xcode project"
else
	rm -f "$built_tree_stamp"
	echo "ios-device: Unity build (log: $log_dir/unity.log)"
	run_profile_build "$dir" iOS "$profile" "$xcode_output" "$log_dir/unity.log" -developmentBuild \
		|| fail "Unity build failed; see $log_dir/unity.log"
	echo "$tree" > "$built_tree_stamp"
fi

derived_data="$dir/Builds/Dev/DerivedData"
echo "ios-device: Xcode build (log: $log_dir/xcode.log)"
xcodebuild -project "$xcode_dir/Unity-iPhone.xcodeproj" -scheme Unity-iPhone -configuration Release \
	-destination "id=$udid" -derivedDataPath "$derived_data" \
	-allowProvisioningUpdates -allowProvisioningDeviceRegistration \
	DEVELOPMENT_TEAM="$APPLE_TEAM_ID" CODE_SIGN_STYLE=Automatic build > "$log_dir/xcode.log" 2>&1 \
	|| fail "Xcode build failed; see $log_dir/xcode.log ($(grep -m1 'error:' "$log_dir/xcode.log" || true))"

app="$(ls -d "$derived_data"/Build/Products/Release-iphoneos/*.app | head -1)"
bundle_id="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$app/Info.plist")"

echo "ios-device: installing $bundle_id on $device_name"
xcrun devicectl device install app --device "$udid" "$app" > "$log_dir/install.log" 2>&1 \
	|| fail "install failed; see $log_dir/install.log"
xcrun devicectl device process launch --device "$udid" --terminate-existing "$bundle_id" > "$log_dir/launch.log" 2>&1 \
	&& echo "ios-device: running on $device_name" \
	|| echo "ios-device: installed, but couldn't launch it (phone locked?); open it on the phone"
