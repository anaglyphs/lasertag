# Windows operator server: Mono player zipped and attached to a GitHub release on the release/<number> tag.
# Mono, because Unity on macOS can't build Windows IL2CPP players (Burst still compiles).
# Needs: Unity's "Windows Build Support (Mono)" module, and `gh auth login` with push access to GITHUB_REPO.
PLATFORM_PROFILE="Assets/Settings/Build Profiles/Windows Operator.asset"
PLATFORM_BUILD_TARGET=Win64
PLATFORM_OUTPUT="Builds/Release/Windows/Lasertag.exe"

windows_zip() { echo "$(dirname "$(dirname "$1")")/Lasertag-Operator-Windows-$build_number.zip"; }

platform_preflight() {
	[ -n "$GITHUB_REPO" ] || fail "set GITHUB_REPO in Tools/release/release.config"
	[ -d "$unity_app/../PlaybackEngines/WindowsStandaloneSupport" ] \
		|| fail "install Unity Hub's 'Windows Build Support (Mono)' module for Unity $unity_version"
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
stamped_number_file() { echo "$(dirname "$(dirname "$1")")/Windows.build-number"; }
platform_built_number() { cat "$(stamped_number_file "$1")"; }

platform_package() {
	local player_dir zip
	player_dir="$(dirname "$1")"
	zip="$(windows_zip "$1")"
	rm -f "$zip"
	sed -n 's/^  defaultVal: //p' "$side_build_root/$PLATFORM_BUILD_TARGET/Assets/Anaglyph/Resources/BuildNumber.asset" > "$(stamped_number_file "$1")"
	( cd "$player_dir" && zip -qr "$zip" . -x '*_DoNotShip/*' '*_ButDontShipItWithYourGame/*' )
}

platform_upload() {
	local tag="release/$build_number" zip
	zip="$(windows_zip "$1")"
	if gh release view "$tag" --repo "$GITHUB_REPO" >/dev/null 2>&1; then
		gh release upload "$tag" "$zip" --repo "$GITHUB_REPO" --clobber
	else
		gh release create "$tag" "$zip" --repo "$GITHUB_REPO" --target "$commit" \
			--title "Build $build_number" --notes "$2" --prerelease --latest=false
	fi
}
