# Desktop operator server shared by platforms/windows.sh and platforms/linux.sh: a Mono player zipped as
# Lasertag-Operator-<OPERATOR_OS>-<number>.zip and attached to the GitHub release on the release/<number> tag.
# The operator app's updater (GitHubReleaseUpdater) finds builds by that tag and zip name.
# Mono, because Unity on macOS can't build Windows or Linux IL2CPP players (Burst still compiles).
# Needs: OPERATOR_OS, OPERATOR_MODULE (Unity PlaybackEngines folder), PLATFORM_* set by the platform file,
# and `gh auth login` with push access to GITHUB_REPO.

operator_zip() { echo "$(dirname "$(dirname "$1")")/Lasertag-Operator-$OPERATOR_OS-$build_number.zip"; }

platform_preflight() {
	[ -n "$GITHUB_REPO" ] || fail "set GITHUB_REPO in Tools/release/release.config"
	[ -d "$unity_app/../PlaybackEngines/$OPERATOR_MODULE" ] \
		|| fail "install Unity Hub's '$OPERATOR_OS Build Support (Mono)' module for Unity $unity_version"
	git -C "$project_dir" cat-file -e "$commit:$PLATFORM_PROFILE" 2>/dev/null || fail "$PLATFORM_PROFILE isn't committed"
	gh auth status --hostname github.com >/dev/null 2>&1 || fail "run 'gh auth login' first"
	gh repo view "$GITHUB_REPO" --json viewerPermission -q .viewerPermission | grep -qE 'ADMIN|MAINTAIN|WRITE' \
		|| fail "your GitHub account can't publish releases to $GITHUB_REPO"
}

platform_highest_store_build_number() {
	gh release list --repo "$GITHUB_REPO" --limit 100 --json tagName -q '.[].tagName' \
		| sed -n 's#^release/\([0-9][0-9]*\)$#\1#p' | sort -n | tail -1
}

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

platform_upload() {
	local tag="release/$build_number" zip
	zip="$(operator_zip "$1")"
	if gh release view "$tag" --repo "$GITHUB_REPO" >/dev/null 2>&1; then
		gh release upload "$tag" "$zip" --repo "$GITHUB_REPO" --clobber
	else
		gh release create "$tag" "$zip" --repo "$GITHUB_REPO" --target "$commit" \
			--title "Build $build_number" --notes "$2" --prerelease --latest=false
	fi
}
