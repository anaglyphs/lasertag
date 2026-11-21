#!/bin/bash
# Builds the committed HEAD for each platform in platforms/<name>.sh and uploads it to that platform's
# test channel. All platforms share one build number, tagged release/<number>.
# Builds run in batch Unity inside ../lasertag_release/<Unity build target> (see Tools/lib/unity-batch.sh).
#
# Usage: Tools/release/release.sh [--dry-run] [platform...]     (RELEASE_DEFAULT_PLATFORMS when none given)
#   --dry-run   check credentials and print the build number and notes, without building or uploading
# Runs in the foreground; Ctrl+C cancels, including a Unity build in progress.
#
# One-time setup:
#   metavr auth login
#   security add-generic-password -a "$USER" -s lasertag-android-keystore -w      (keystore password)
#   security add-generic-password -a "$USER" -s lasertag-android-keyalias -w      (only if the alias password differs)
#   Xcode > Settings > Accounts: signed in to APPLE_TEAM_ID's team
#   gh auth login                                                                 (GitHub releases)
set -Eeuo pipefail
trap 'echo "release: stopped unexpectedly at line $LINENO: $BASH_COMMAND" >&2' ERR

release_dir="$(cd "$(dirname "$0")" && pwd)"
project_dir="$(cd "$release_dir/../.." && pwd)"
source "$release_dir/release.config"

fail() { echo "release: $*" >&2; exit 1; }

# --- helpers platform files share ---

require_android_keystore_passwords() {
	RELEASE_KEYSTORE_PASS="$(security find-generic-password -s lasertag-android-keystore -w 2>/dev/null)" \
		|| fail "no keystore password in the Keychain (see setup in Tools/release/release.sh)"
	RELEASE_KEYALIAS_PASS="$(security find-generic-password -s lasertag-android-keyalias -w 2>/dev/null || true)"
	export RELEASE_KEYSTORE_PASS RELEASE_KEYALIAS_PASS

	local keystore
	keystore="$(sed -n "s/.*AndroidKeystoreName: \(.*\)'$/\1/p" "$project_dir/$PLATFORM_PROFILE")"
	[ -f "$keystore" ] || fail "keystore not found at '$keystore' (set in $PLATFORM_PROFILE)"
	"$unity_app/../PlaybackEngines/AndroidPlayer/OpenJDK/bin/keytool" -list -keystore "$keystore" -storepass:env RELEASE_KEYSTORE_PASS >/dev/null 2>&1 \
		|| fail "the Keychain's lasertag-android-keystore password doesn't open $keystore"
}

android_version_code() {
	local aapt
	aapt="$(ls "$unity_app"/../PlaybackEngines/AndroidPlayer/SDK/build-tools/*/aapt2 | tail -1)"
	"$aapt" dump badging "$1" | sed -n "s/.*versionCode='\([0-9]*\)'.*/\1/p"
}

highest_version_code_in_json() {
	python3 -I -c '
import json, sys
def codes(node):
	if isinstance(node, dict):
		for key, value in node.items():
			if key.replace("_", "").lower() == "versioncode" and str(value).isdigit(): yield int(value)
			else: yield from codes(value)
	elif isinstance(node, list):
		for item in node: yield from codes(item)
print(max(codes(json.load(sys.stdin)), default=0))'
}

# --- driver ---

dry_run=false
if [ "${1:-}" = --dry-run ]; then dry_run=true; shift; fi
read -r -a platforms <<< "${*:-$RELEASE_DEFAULT_PLATFORMS}"
for platform in "${platforms[@]}"; do
	[ -f "$release_dir/platforms/$platform.sh" ] || fail "no platform '$platform' (have: $(ls "$release_dir/platforms" | sed 's/\.sh$//' | xargs))"
done

commit="$(git -C "$project_dir" rev-parse HEAD)"
[ -z "$(git -C "$project_dir" status --porcelain)" ] || echo "release: uncommitted changes are NOT in this release"
git -C "$project_dir" cat-file -e "$commit:Assets/Anaglyph/Editor/ProfileBuild.cs" 2>/dev/null \
	|| fail "the build code isn't committed yet (Assets/Anaglyph/Editor/ProfileBuild.cs)"

source "$project_dir/Tools/lib/unity-batch.sh"
log_dir="$side_build_root/logs/$(date +%Y%m%d-%H%M%S)"

# each platform runs in its own subshell, so platform files can't see each other's settings
in_platform() { local platform="$1"; shift; ( source "$release_dir/platforms/$platform.sh"; "$@" ); }

echo "release: checking credentials for ${platforms[*]}"
# every credential is checked before the first long build
for platform in "${platforms[@]}"; do in_platform "$platform" platform_preflight; done

highest="$(git -C "$project_dir" tag -l 'release/*' | sed -n 's#^release/\([0-9][0-9]*\)$#\1#p' | sort -n | tail -1)"
highest="${highest:-0}"
for platform in "${platforms[@]}"; do
	store_highest="$(in_platform "$platform" platform_highest_store_build_number)" || fail "couldn't read $platform's store build numbers"
	if [ "${store_highest:-0}" -gt "$highest" ]; then highest="$store_highest"; fi
done
build_number=$(( highest + 1 ))

previous_release="$(git -C "$project_dir" tag -l 'release/*' --sort=-v:refname | head -1)"
notes="$(git -C "$project_dir" log --format='• %s' --no-merges -20 "${previous_release:+$previous_release..}$commit")"

echo "release: $(git -C "$project_dir" log -1 --format='%h %s' "$commit") as build $build_number to ${platforms[*]}"
if $dry_run; then
	printf 'release: notes:\n%s\nrelease: dry run, nothing built\n' "$notes"
	exit 0
fi
mkdir -p "$log_dir"

artifact_path() { echo "$side_build_root/$PLATFORM_BUILD_TARGET/$PLATFORM_OUTPUT"; }

build_platform() {
	local platform="$1" dir artifact log built build_stamp
	dir="$(prepare_worktree "$side_build_root/$PLATFORM_BUILD_TARGET" "$commit")"
	artifact="$(artifact_path)"
	log="$log_dir/$platform-unity.log"
	build_stamp="$dir/Builds/Release/$platform.built-from"

	if [ -e "$artifact" ] && [ "$(cat "$build_stamp" 2>/dev/null || true)" = "$commit $build_number" ]; then
		echo "release: reusing the $platform $build_number build of this commit"
	else
		rm -rf "$artifact" "$build_stamp"
		echo "release: building $platform $build_number (log: $log)"
		run_profile_build "$dir" "$PLATFORM_BUILD_TARGET" "$PLATFORM_PROFILE" "$PLATFORM_OUTPUT" "$log" -buildNumber "$build_number" \
			|| fail "$platform build failed; see $log"
		if declare -F platform_package >/dev/null; then
			echo "release: packaging $platform $build_number (log: $log_dir/$platform-package.log)"
			platform_package "$artifact" > "$log_dir/$platform-package.log" 2>&1 \
				|| fail "$platform packaging failed; see $log_dir/$platform-package.log"
		fi
		echo "$commit $build_number" > "$build_stamp"
	fi

	built="$(platform_built_number "$artifact")"
	[ "$built" = "$build_number" ] || fail "$platform artifact has build number $built, expected $build_number"
}

upload_platform() {
	local platform="$1"
	echo "release: uploading $platform $build_number"
	platform_upload "$(artifact_path)" "$notes" 2>&1 | tee "$log_dir/$platform-upload.log" \
		|| fail "$platform upload failed; see $log_dir/$platform-upload.log"

	git -C "$project_dir" rev-parse -q --verify "refs/tags/release/$build_number" >/dev/null \
		|| git -C "$project_dir" tag "release/$build_number" "$commit"
	echo "release: $platform $build_number uploaded"
}

# preflight again: it exports the platform's credentials into this platform's subshell
preflight_then_build() { platform_preflight; build_platform "$1"; }

# nothing uploads until every platform has built
for platform in "${platforms[@]}"; do in_platform "$platform" preflight_then_build "$platform"; done
for platform in "${platforms[@]}"; do in_platform "$platform" upload_platform "$platform"; done
osascript -e "display notification \"Build $build_number: ${platforms[*]}\" with title \"Lasertag release done\"" 2>/dev/null || true
