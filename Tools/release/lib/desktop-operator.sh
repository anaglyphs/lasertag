# Desktop operator server shared by platforms/windows.sh and platforms/linux.sh: a Mono player zipped as
# Lasertag-Operator-<OPERATOR_OS>-<number>.zip and attached to the GitHub release on the release/<number> tag.
# The operator app's updater (GitHubReleaseUpdater) finds builds by that tag and zip name.
# Mono, because Unity on macOS can't build Windows or Linux IL2CPP players (Burst still compiles).
# Needs: OPERATOR_OS, OPERATOR_MODULE (Unity PlaybackEngines folder), PLATFORM_* set by the platform file,
# and GitHub release access (lib/github-release.sh).
source "$release_dir/lib/github-release.sh"

operator_zip() { echo "$(dirname "$(dirname "$1")")/Lasertag-Operator-$OPERATOR_OS-$build_number.zip"; }

platform_preflight() {
	[ -d "$unity_app/../PlaybackEngines/$OPERATOR_MODULE" ] \
		|| fail "install Unity Hub's '$OPERATOR_OS Build Support (Mono)' module for Unity $unity_version"
	git -C "$project_dir" cat-file -e "$commit:$PLATFORM_PROFILE" 2>/dev/null || fail "$PLATFORM_PROFILE isn't committed"
	require_github_release_access
}

platform_highest_store_build_number() { highest_github_release_number; }

# the next release resets the worktree, so packaging keeps the number BuildProcess stamped into the game
stamped_number_file() { echo "$(dirname "$(dirname "$1")")/$OPERATOR_OS.build-number"; }
platform_built_number() { cat "$(stamped_number_file "$1")"; }

platform_package() {
	local player_dir zip
	player_dir="$(dirname "$1")"
	zip="$(operator_zip "$1")"
	rm -f "$zip"
	sed -n 's/^  defaultVal: //p' "$build_dir/Assets/Anaglyph/Resources/BuildNumber.asset" > "$(stamped_number_file "$1")"
	( cd "$player_dir" && zip -qr "$zip" . -x '*_DoNotShip/*' '*_ButDontShipItWithYourGame/*' )
}

platform_upload() { attach_to_github_release "$(operator_zip "$1")" "$2"; }
