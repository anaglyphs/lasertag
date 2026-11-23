#!/bin/bash
# Builds out/toastprobe.apk with the plain SDK tools (no Gradle).
set -euo pipefail
cd "$(dirname "$0")"

SDK="${ANDROID_HOME:-$HOME/Library/Android/sdk}"
BUILD_TOOLS="$SDK/build-tools/$(ls "$SDK/build-tools" | sort -V | tail -1)"
ANDROID_JAR="$SDK/platforms/android-34/android.jar"
KEYSTORE="$HOME/.android/debug.keystore"

rm -rf out && mkdir -p out/classes
javac --release 11 -classpath "$ANDROID_JAR" -d out/classes $(find src -name '*.java')
"$BUILD_TOOLS/d8" --min-api 29 --lib "$ANDROID_JAR" --output out $(find out/classes -name '*.class')
"$BUILD_TOOLS/aapt2" link -o out/unsigned.apk -I "$ANDROID_JAR" --manifest AndroidManifest.xml
(cd out && zip -q unsigned.apk classes.dex)
"$BUILD_TOOLS/zipalign" -f 4 out/unsigned.apk out/aligned.apk
"$BUILD_TOOLS/apksigner" sign --ks "$KEYSTORE" --ks-pass pass:android --out out/toastprobe.apk out/aligned.apk
echo "built $(pwd)/out/toastprobe.apk"
