# Attaching files to the GitHub release on the release/<number> tag, shared by platform files.
# Needs GITHUB_REPO in release.config and `gh auth login` with push access to it.

require_github_release_access() {
	[ -n "$GITHUB_REPO" ] || fail "set GITHUB_REPO in Tools/release/release.config"
	gh auth status --hostname github.com >/dev/null 2>&1 || fail "run 'gh auth login' first"
	gh repo view "$GITHUB_REPO" --json viewerPermission -q .viewerPermission | grep -qE 'ADMIN|MAINTAIN|WRITE' \
		|| fail "your GitHub account can't publish releases to $GITHUB_REPO"
}

highest_github_release_number() {
	gh release list --repo "$GITHUB_REPO" --limit 100 --json tagName -q '.[].tagName' \
		| sed -n 's#^release/\([0-9][0-9]*\)$#\1#p' | sort -n | tail -1
}

# attach_to_github_release <file> <notes>: replaces an asset of the same name, so reruns are safe
attach_to_github_release() {
	local tag="release/$build_number"
	if gh release view "$tag" --repo "$GITHUB_REPO" >/dev/null 2>&1; then
		gh release upload "$tag" "$1" --repo "$GITHUB_REPO" --clobber
	else
		gh release create "$tag" "$1" --repo "$GITHUB_REPO" --target "$commit" \
			--title "Build $build_number" --notes "$2" --prerelease --latest=false
	fi
}
