# Meta Quest: Release profile APK to the Meta release channel in release.config.
# A platform file sets PLATFORM_PROFILE, PLATFORM_BUILD_TARGET (Unity -buildTarget) and PLATFORM_OUTPUT
# (relative to the project), and defines:
#   platform_preflight                       fail early on missing credentials
#   platform_highest_store_build_number      highest number the store already has (0 when unknown)
#   platform_built_number <artifact>         the number inside the built artifact
#   platform_upload <artifact> <notes>
PLATFORM_PROFILE="Assets/Settings/Build Profiles/Release.asset"
PLATFORM_BUILD_TARGET=Android
PLATFORM_OUTPUT="Builds/Release/Quest/Lasertag.apk"

platform_preflight() {
	[ -n "$META_APP_ID" ] || fail "set META_APP_ID in Tools/release/release.config"
	metavr auth status 2>&1 | grep -qi "not authenticated" && fail "run 'metavr auth login' first"
	require_android_keystore_passwords
}

platform_highest_store_build_number() {
	metavr --json store dist channels --app-id "$META_APP_ID" | highest_version_code_in_json
}

platform_built_number() { android_version_code "$1"; }

platform_upload() {
	metavr store dist upload --app-id "$META_APP_ID" --channel "$META_CHANNEL" --apk "$1" --notes "$2" --publish
}
