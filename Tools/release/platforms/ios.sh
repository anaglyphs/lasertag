# iPhone: iOS profile Xcode project, archived and uploaded to TestFlight. See quest.sh for the platform contract.
# Signing uses the Apple account signed in to Xcode (Settings > Accounts) for APPLE_TEAM_ID.
PLATFORM_PROFILE="Assets/Settings/Build Profiles/iOS.asset"
PLATFORM_BUILD_TARGET=iOS
PLATFORM_OUTPUT="Builds/Release/iOS"

platform_preflight() {
	[ -n "$APPLE_TEAM_ID" ] || fail "set APPLE_TEAM_ID in Tools/release/release.config"
}

# reading TestFlight's numbers needs an App Store Connect API key; the release tags cover it
platform_highest_store_build_number() { echo 0; }

platform_built_number() { /usr/libexec/PlistBuddy -c 'Print :CFBundleVersion' "$1/Info.plist"; }

platform_upload() { "$project_dir/Tools/ios-testflight.sh" "$APPLE_TEAM_ID" "$1"; }
