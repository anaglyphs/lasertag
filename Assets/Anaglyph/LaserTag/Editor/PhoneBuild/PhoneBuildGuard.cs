using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>
	/// The Android Phone profile builds only through <see cref="PhoneBuild"/>, which puts ARCore and
	/// the phone's manifest in place of Quest's: built any other way it would be a Quest app.
	/// </summary>
	public class PhoneBuildGuard : IPreprocessBuildWithReport
	{
		public int callbackOrder => -1000;

		public void OnPreprocessBuild(BuildReport report)
		{
			BuildProfile active = BuildProfile.GetActiveBuildProfile();
			if (!PhoneBuild.InProgress && active != null && active.name == "Android Phone")
				throw new BuildFailedException("Build the Android Phone profile with Lasertag > Build Android Phone APK");
		}
	}
}
