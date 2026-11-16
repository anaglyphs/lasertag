#!/bin/bash
# Archives the Unity-exported Xcode project in Builds/iOS and uploads it to App Store Connect,
# where it appears in TestFlight once Apple has processed it.
# Usage: Tools/ios-testflight.sh <team-id> [xcode-project-folder, default Builds/iOS] [archive|upload|all, default all]
# Needs: the team's Apple ID signed into Xcode (Settings > Accounts), and an app record in
# App Store Connect for the build's bundle ID.
set -euo pipefail

team="${1:?usage: $0 <team-id>}"
project_dir="$(cd "$(dirname "$0")/.." && pwd)"
xcode_dir="$(cd "${2:-$project_dir/Builds/iOS}" && pwd)"
xcode_project="$xcode_dir/Unity-iPhone.xcodeproj"
archive="${xcode_dir}Archive/Lasertag.xcarchive"
export_dir="${xcode_dir}Export"
options="${xcode_dir}Archive/ExportOptions.plist"
step="${3:-all}"

if [[ "$step" == archive || "$step" == all ]]; then
	rm -rf "$archive"
	xcodebuild -project "$xcode_project" -scheme Unity-iPhone -configuration Release \
		-destination "generic/platform=iOS" -archivePath "$archive" \
		-allowProvisioningUpdates DEVELOPMENT_TEAM="$team" CODE_SIGN_STYLE=Automatic archive
fi
[[ "$step" == upload || "$step" == all ]] || exit 0
[ -d "$archive" ] || { echo "no archive at $archive; run the archive step first" >&2; exit 1; }

mkdir -p "$(dirname "$archive")"
cat > "$options" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>method</key><string>app-store-connect</string>
	<key>destination</key><string>upload</string>
	<key>teamID</key><string>$team</string>
	<key>signingStyle</key><string>automatic</string>
	<key>uploadSymbols</key><true/>
	<key>manageAppVersionAndBuildNumber</key><false/>
</dict>
</plist>
EOF

xcodebuild -exportArchive -archivePath "$archive" -exportPath "$export_dir" \
	-exportOptionsPlist "$options" -allowProvisioningUpdates

echo "Uploaded. It shows in App Store Connect > TestFlight once Apple finishes processing (usually 5-30 minutes)."
