# Sourced by the build scripts in Tools: batch Unity builds in worktrees beside this checkout, so the
# open editor is untouched. Callers set project_dir and define fail().

side_build_root="$(dirname "$project_dir")/lasertag_release"

unity_version="$(sed -n 's/^m_EditorVersion: //p' "$project_dir/ProjectSettings/ProjectVersion.txt")"
unity_app="/Applications/Unity/Hub/Editor/$unity_version/Unity.app"
[ -x "$unity_app/Contents/MacOS/Unity" ] || fail "Unity $unity_version is not installed at $unity_app"

# prepare_worktree <dir> <commit> [library to clone on first use, default this checkout's]
# A detached worktree at <commit> that keeps its own Library between builds. Prints the folder.
prepare_worktree() {
	local dir="$1" commit="$2" library_source="${3:-$project_dir/Library}"
	if [ ! -e "$dir/.git" ]; then
		git -C "$project_dir" worktree add --quiet --detach "$dir" "$commit"
		echo "cloning $library_source into $dir" >&2
		cp -Rc "$library_source" "$dir/Library"
		rm -f "$dir/Library/EditorInstance.json" "$dir/Library/"*-lock
		# Gradle's intermediates hold absolute paths into the source checkout; Unity regenerates them per build
		rm -rf "$dir/Library/Bee/Android/Prj"
	else
		git -C "$dir" checkout --quiet --detach --force "$commit"
		git -C "$dir" clean -fdq
	fi
	pgrep -f -- "-projectPath $dir( |$)" >/dev/null && fail "Unity is already open on $dir"
	rm -f "$dir/Temp/UnityLockfile"
	echo "$dir"
}

# working_tree_snapshot: a commit of every file here, committed or not (minus ignored ones), leaving
# your index and branches untouched
working_tree_snapshot() {
	local index tree
	index="$(mktemp)"
	cp "$(git -C "$project_dir" rev-parse --path-format=absolute --git-path index)" "$index"
	GIT_INDEX_FILE="$index" git -C "$project_dir" add -A
	tree="$(GIT_INDEX_FILE="$index" git -C "$project_dir" write-tree)"
	rm -f "$index"
	git -C "$project_dir" commit-tree "$tree" -p HEAD -m "working tree snapshot"
}

# run_profile_build <dir> <unity build target> <profile asset> <output path> <log> [ProfileBuild args...]
# A profile with its own build code sets build_method for the call (same arguments and exit codes as ProfileBuild).
run_profile_build() {
	local dir="$1" build_target="$2" profile="$3" output="$4" log="$5"
	shift 5
	local unity_exit attempt
	for attempt in 1 2 3; do
		unity_exit=0
		"$unity_app/Contents/MacOS/Unity" -batchmode -projectPath "$dir" -buildTarget "$build_target" \
			-executeMethod "${build_method:-Anaglyph.Editor.ProfileBuild.BuildFromCommandLine}" \
			-buildProfilePath "$profile" -buildOutputPath "$output" "$@" -logFile "$log" || unity_exit=$?
		[ "$unity_exit" -eq 0 ] || [ "$attempt" -eq 3 ] && break
		# 2: ProfileBuild switched to the profile and needs a relaunch to compile with its defines
		if [ "$unity_exit" -eq 2 ]; then
			echo "switched Unity to $profile, relaunching" >&2
		# Unity's import workers, starting up mid-build on a busy first launch, can delete Addressables'
		# temporary link.xml while the code stripper reads it; the next launch has nothing left to import
		elif grep -q 'link.xml` changed while running `UnityLinker' "$log"; then
			echo "Addressables' link.xml vanished mid-build (Unity import race), building again" >&2
			cp "$log" "$log.attempt$attempt"
		else
			break
		fi
	done
	return "$unity_exit"
}
