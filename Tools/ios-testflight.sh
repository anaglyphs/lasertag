#!/bin/bash
# Archives the Unity-exported Xcode project in Builds/iOS and uploads it to App Store Connect,
# where it appears in TestFlight once Apple has processed it.
# Usage: Tools/ios-testflight.sh <team-id>
# Needs: the team's Apple ID signed into Xcode (Settings > Accounts), and an app record in
# App Store Connect for the build's bundle ID.
set -euo pipefail

team="${1:?usage: $0 <team-id>}"
project_dir="$(cd "$(dirname "$0")/.." && pwd)"
xcode_project="$project_dir/Builds/iOS/Unity-iPhone.xcodeproj"
archive="$project_dir/Builds/iOSArchive/Lasertag.xcarchive"
export_dir="$project_dir/Builds/iOSExport"
options="$project_dir/Builds/iOSArchive/ExportOptions.plist"

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

xcodebuild -project "$xcode_project" -scheme Unity-iPhone -configuration Release \
	-destination "generic/platform=iOS" -archivePath "$archive" \
	-allowProvisioningUpdates DEVELOPMENT_TEAM="$team" CODE_SIGN_STYLE=Automatic archive

xcodebuild -exportArchive -archivePath "$archive" -exportPath "$export_dir" \
	-exportOptionsPlist "$options" -allowProvisioningUpdates

echo "Uploaded. It shows in App Store Connect > TestFlight once Apple finishes processing (usually 5-30 minutes)."
