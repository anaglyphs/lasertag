#!/bin/bash
# Builds the committed HEAD for each platform in platforms/<name>.sh and uploads it to that platform's
# test channel. All platforms share one build number, tagged release/<number>.
# Builds run in batch Unity inside ../lasertag_release/<Unity build target>: worktrees whose Library
# starts as an APFS clone of this checkout's, so the open editor is untouched.
#
# Usage: Tools/release/release.sh [platform...]       (RELEASE_DEFAULT_PLATFORMS when none given)
#
# One-time setup:
#   metavr auth login
#   security add-generic-password -a "$USER" -s lasertag-android-keystore -w      (keystore password)
#   security add-generic-password -a "$USER" -s lasertag-android-keyalias -w      (only if the alias password differs)
#   Xcode > Settings > Accounts: signed in to APPLE_TEAM_ID's team
set -euo pipefail

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

read -r -a platforms <<< "${*:-$RELEASE_DEFAULT_PLATFORMS}"
for platform in "${platforms[@]}"; do
	[ -f "$release_dir/platforms/$platform.sh" ] || fail "no platform '$platform' (have: $(ls "$release_dir/platforms" | sed 's/\.sh$//' | xargs))"
done

commit="$(git -C "$project_dir" rev-parse HEAD)"
[ -z "$(git -C "$project_dir" status --porcelain)" ] || echo "release: uncommitted changes are NOT in this release"

unity_version="$(sed -n 's/^m_EditorVersion: //p' "$project_dir/ProjectSettings/ProjectVersion.txt")"
unity_app="/Applications/Unity/Hub/Editor/$unity_version/Unity.app"
[ -x "$unity_app/Contents/MacOS/Unity" ] || fail "Unity $unity_version is not installed at $unity_app"

release_root="$(dirname "$project_dir")/lasertag_release"
log_dir="$release_root/logs/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$log_dir"

# each platform runs in its own subshell, so platform files can't see each other's settings
in_platform() { local platform="$1"; shift; ( source "$release_dir/platforms/$platform.sh"; "$@" ); }

# every credential is checked before the first long build
for platform in "${platforms[@]}"; do in_platform "$platform" platform_preflight; done

highest="$(git -C "$project_dir" tag -l 'release/*' | sed 's#release/##' | grep -E '^[0-9]+$' | sort -n | tail -1)"
highest="${highest:-0}"
for platform in "${platforms[@]}"; do
	store_highest="$(in_platform "$platform" platform_highest_store_build_number)" || fail "couldn't read $platform's store build numbers"
	[ "${store_highest:-0}" -gt "$highest" ] && highest="$store_highest"
done
build_number=$(( highest + 1 ))

previous_release="$(git -C "$project_dir" tag -l 'release/*' --sort=-v:refname | head -1)"
notes="$(git -C "$project_dir" log --format='- %s' --no-merges -20 "${previous_release:+$previous_release..}$commit")"

# a detached worktree at $commit beside this checkout, keeping its own Library between releases
prepare_worktree() {
	local dir="$release_root/$1"
	if [ ! -e "$dir/.git" ]; then
		git -C "$project_dir" worktree add --quiet --detach "$dir" "$commit"
		echo "release: cloning Library into $dir" >&2
		cp -Rc "$project_dir/Library" "$dir/Library"
		rm -f "$dir/Library/EditorInstance.json" "$dir/Library/"*-lock
	else
		git -C "$dir" checkout --quiet --detach --force "$commit"
		git -C "$dir" clean -fdq
	fi
	pgrep -f -- "-projectPath $dir( |$)" >/dev/null && fail "Unity is already open on $dir"
	rm -f "$dir/Temp/UnityLockfile"
	echo "$dir"
}

release_platform() {
	local platform="$1" dir artifact log built
	dir="$(prepare_worktree "$PLATFORM_BUILD_TARGET")"
	artifact="$dir/$PLATFORM_OUTPUT"
	log="$log_dir/$platform-unity.log"
	rm -rf "$artifact"

	echo "release: building $platform $build_number (log: $log)"
	"$unity_app/Contents/MacOS/Unity" -batchmode -projectPath "$dir" -buildTarget "$PLATFORM_BUILD_TARGET" \
		-executeMethod Anaglyph.Editor.ReleaseBuild.BuildFromCommandLine \
		-releaseProfile "$PLATFORM_PROFILE" -releaseOutput "$PLATFORM_OUTPUT" -releaseBuildNumber "$build_number" \
		-logFile "$log" || fail "$platform build failed; see $log"

	built="$(platform_built_number "$artifact")"
	[ "$built" = "$build_number" ] || fail "$platform artifact has build number $built, expected $build_number"

	echo "release: uploading $platform $build_number"
	platform_upload "$artifact" "$notes" 2>&1 | tee "$log_dir/$platform-upload.log" \
		|| fail "$platform upload failed; see $log_dir/$platform-upload.log"

	git -C "$project_dir" rev-parse -q --verify "refs/tags/release/$build_number" >/dev/null \
		|| git -C "$project_dir" tag "release/$build_number" "$commit"
	echo "release: $platform $build_number uploaded"
}

# preflight again: it exports the platform's credentials into this platform's subshell
preflight_then_release() { platform_preflight; release_platform "$1"; }

echo "release: $(git -C "$project_dir" log -1 --format='%h %s' "$commit") as build $build_number to ${platforms[*]}"
for platform in "${platforms[@]}"; do
	in_platform "$platform" preflight_then_release "$platform"
done
osascript -e "display notification \"Build $build_number: ${platforms[*]}\" with title \"Lasertag release done\"" 2>/dev/null || true
