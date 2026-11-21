#!/bin/bash
# Builds this working tree (uncommitted and new files included) as an Android phone APK to send around:
# batch Unity in ../lasertag_release/Android-phone, then a copy in Builds/Phone named by version and commit.
# Your open editor is untouched.
#
# Usage: Tools/android-phone-apk.sh
# Signing follows the Android Phone profile. With its debug key, friends' updates install over each other
# only while this Mac's ~/.android/debug.keystore stays the same.
set -Eeuo pipefail
trap 'echo "android-phone-apk: stopped unexpectedly at line $LINENO: $BASH_COMMAND" >&2' ERR

project_dir="$(cd "$(dirname "$0")/.." && pwd)"
fail() { echo "android-phone-apk: $*" >&2; exit 1; }
source "$project_dir/Tools/lib/unity-batch.sh"

profile="Assets/Settings/Build Profiles/Android Phone.asset"
apk_output="Builds/Phone/Lasertag.apk"
dir="$side_build_root/Android-phone"
log_dir="$side_build_root/logs/android-phone-$(date +%Y%m%d-%H%M%S)"
shared_apk_dir="$project_dir/Builds/Phone"

snapshot="$(working_tree_snapshot)"
tree="$(git -C "$project_dir" rev-parse "$snapshot^{tree}")"
# the Quest release folder's Library is already imported for Android, so the first build here starts warm
library_source="$side_build_root/Android/Library"
[ -d "$library_source" ] || library_source="$project_dir/Library"
prepare_worktree "$dir" "$snapshot" "$library_source" >/dev/null
mkdir -p "$log_dir"

apk="$dir/$apk_output"
built_tree_stamp="$dir/Builds/Phone/apk.built-from"
if [ -f "$apk" ] && [ "$(cat "$built_tree_stamp" 2>/dev/null || true)" = "$tree" ]; then
	echo "android-phone-apk: no file changes since the last build, reusing its APK"
else
	rm -f "$apk" "$built_tree_stamp"
	echo "android-phone-apk: Unity build (log: $log_dir/unity.log)"
	# the phone profile builds only through PhoneBuild, which swaps Quest's XR loader and manifest for ARCore's
	build_method=Anaglyph.LaserTag.Editor.PhoneBuild.BuildFromCommandLine \
		run_profile_build "$dir" Android "$profile" "$apk_output" "$log_dir/unity.log" \
		|| fail "Unity build failed; see $log_dir/unity.log"
	[ -f "$apk" ] || fail "Unity reported success but wrote no APK at $apk; see $log_dir/unity.log"
	echo "$tree" > "$built_tree_stamp"
fi

aapt="$(ls "$unity_app"/../PlaybackEngines/AndroidPlayer/SDK/build-tools/*/aapt2 | tail -1)"
version_name="$("$aapt" dump badging "$apk" | sed -n "s/.*versionName='\([^']*\)'.*/\1/p")"
commit_label="$(git -C "$project_dir" rev-parse --short HEAD)"
[ -z "$(git -C "$project_dir" status --porcelain)" ] || commit_label="$commit_label-modified"

mkdir -p "$shared_apk_dir"
shared_apk="$shared_apk_dir/Lasertag-$version_name-$commit_label.apk"
cp "$apk" "$shared_apk"
echo "android-phone-apk: $shared_apk ($(du -h "$shared_apk" | cut -f1 | xargs))"
open -R "$shared_apk" 2>/dev/null || true
