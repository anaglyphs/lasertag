using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.VisualLocalization;
using NUnit.Framework;
using Unity.Mathematics;

namespace Anaglyph.LaserTag.Tests
{
	/// <summary>Player 2's solves as TwoPlayerTest logged them, through the gate and the watch as the provider runs them.</summary>
	public class ColocationGateReplayTests
	{
		// 20261103-204150: the room's poses put the truth at yaw -157.4°, and -67° was applied, 16 m off for the whole run
		private static readonly string[] WrongJoin =
		{
			"17.55 yaw=112.74°±0.53 t=(7.843, 6.030, -16.936)±3.6cm inliers=53/107 (ranged 53)",
			"19.77 yaw=-156.39°±1.01 t=(1.615, 6.037, -6.304)±16.2cm inliers=75/100 (ranged 74)",
			"20.95 yaw=-156.86°±0.20 t=(1.607, 6.058, -6.324)±3.5cm inliers=105/145 (ranged 105)",
			"21.86 yaw=-77.39°±2.25 t=(2.540, 6.024, -2.073)±28.6cm inliers=61/127 (ranged 57)",
			"22.80 yaw=-134.65°±2.25 t=(-3.125, 3.035, 7.416)±48.0cm inliers=12/30 (ranged 10)",
			"23.71 yaw=-63.94°±1.56 t=(12.054, 5.900, -1.958)±15.5cm inliers=36/59 (ranged 36)",
			"24.99 yaw=-67.30°±0.88 t=(11.826, 6.021, -1.487)±6.0cm inliers=43/94 (ranged 43)",
			"25.28 yaw=-66.85°±0.56 t=(11.884, 6.016, -1.550)±4.4cm inliers=46/102 (ranged 46)",
		};

		private const float TrueYawDegrees = -157.4f, WrongYawDegrees = -67f;

		// 20261103-191636: two strong frames of one view; right, and applied within 0.3 s
		private static readonly string[] StrongPair =
		{
			"44.87 yaw=-161.17°±0.32 t=(-14.452, 6.037, 1.174)±3.3cm inliers=215/233 (ranged 144)",
			"45.20 yaw=-161.65°±0.31 t=(-14.422, 6.028, 1.084)±3.1cm inliers=230/266 (ranged 130)",
		};

		// 20261103-003337: two weak frames of one view; right, but a mimic looks just the same
		private static readonly string[] WeakPair =
		{
			"59.69 yaw=171.40°±0.41 t=(1.387, 6.051, -14.715)±4.7cm inliers=54/97 (ranged 46)",
			"60.09 yaw=170.84°±0.60 t=(1.296, 6.072, -14.673)±6.3cm inliers=43/90 (ranged 35)",
		};

		// 20261103-201756: right frames, then three frames of a mimic before the right place comes back
		private static readonly string[] RightThroughMimics =
		{
			"09.22 yaw=-10.25°±0.36 t=(14.209, -0.697, 6.058)±3.8cm inliers=216/221 (ranged 126)",
			"18.58 yaw=-101.90°±0.83 t=(2.925, -0.719, 11.348)±4.4cm inliers=75/113 (ranged 75)",
			"24.74 yaw=85.71°±0.58 t=(7.571, -0.872, -3.136)±7.5cm inliers=92/139 (ranged 91)",
			"26.34 yaw=82.00°±0.54 t=(8.080, -0.709, -3.520)±4.2cm inliers=78/101 (ranged 78)",
			"27.07 yaw=-9.88°±0.33 t=(14.127, -0.854, 5.969)±3.1cm inliers=154/188 (ranged 130)",
			"27.85 yaw=-9.96°±0.34 t=(14.246, -0.866, 6.234)±3.7cm inliers=196/200 (ranged 158)",
			"28.44 yaw=-10.72°±0.54 t=(14.252, -0.746, 6.023)±5.8cm inliers=40/77 (ranged 39)",
		};

		private static readonly Regex Solve = new(@"^([\d.]+) yaw=(-?[\d.]+)°±([\d.]+) t=\((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\)±([\d.]+)cm inliers=(\d+)/(\d+) \(ranged (\d+)\)$");

		private static float Number(Match match, int group) => float.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

		private const float WrongJoinWalkStart = 20.95f;
		private static readonly float3 WrongJoinHead = new(-9.2f, -7.2f, -4f);
		// where 20261103-201756's log has the head at its walk start
		private static readonly float3 RightJoinHead = new(-4.1f, -7.2f, 3.3f);

		// the head walked straight at 1.4 m/s through a run's solves, from where it was at walkStart
		private static MeasurementGate.Frame Frame(string line, float walkStart = WrongJoinWalkStart, float3? head = null)
		{
			Match match = Solve.Match(line);
			Assert.That(match.Success, line);
			float time = Number(match, 1);
			Pose4 correction = new(new float3(Number(match, 4), Number(match, 5), Number(match, 6)), math.radians(Number(match, 2)));
			float3 forward = math.normalize(new float3(0.57f, 0, 0.82f));
			float3 camera = (head ?? WrongJoinHead) + forward * 1.4f * (time - walkStart);
			return new MeasurementGate.Frame
			{
				purpose = MeasurementPurpose.Colocate,
				camera = camera,
				correction = correction,
				spaceInMap = correction,
				cameraInSpace = camera,
				forwardInSpace = forward,
				solve = new GravityPnPResult
				{
					success = true,
					yaw = correction.yaw,
					translation = correction.translation,
					yawSigma = math.radians(Number(match, 3)),
					translationSigma = Number(match, 7) / 100,
					inliers = (int)Number(match, 8),
					correspondences = (int)Number(match, 9),
					rangedInliers = (int)Number(match, 10),
					normalizedRms = 1,
				},
			};
		}

		private static MeasurementGate Gate(MeasurementGate.Settings settings) =>
			new(settings, (_, _) => 0, (_, _) => 0);

		// the alignment a run's solves come to, as the provider would feed them; false when none
		private static bool Replay(MeasurementGate gate, IEnumerable<string> lines, out MeasurementGate.Agreed agreed, float walkStart = WrongJoinWalkStart, float3? head = null)
		{
			agreed = default;
			foreach (string line in lines)
			{
				MeasurementGate.Frame frame = Frame(line, walkStart, head);
				if (gate.Rejection(frame.solve, frame.purpose) != null) continue;
				if (gate.TryAgree(frame, out agreed)) return true;
			}

			return false;
		}

		private static float YawDegrees(in MeasurementGate.Agreed agreed) => math.degrees(agreed.correction.yaw);

		[Test]
		public void TheWrongPairIsNotApplied()
		{
			Assert.That(Replay(Gate(new MeasurementGate.Settings()), WrongJoin, out _), Is.False);
		}

		[Test]
		public void TheWrongPairIsHeldForLookingLikeOneViewEvenWithNoRivalInSight()
		{
			MeasurementGate gate = Gate(new MeasurementGate.Settings());
			Assert.That(Replay(gate, new[] { WrongJoin[6], WrongJoin[7] }, out _), Is.False);
			Assert.That(gate.HeldBack, Does.Contain("one view"));
		}

		[Test]
		public void TheWrongPlaceSeenFromAnotherViewStillLosesToTheRightOne()
		{
			MeasurementGate gate = Gate(new MeasurementGate.Settings());
			List<string> lines = new(WrongJoin)
			{
				// the same place 1.4 m further on, as the log's frames at 24.99 and 25.28 say it
				"26.28 yaw=-66.85°±0.56 t=(11.884, 6.016, -1.550)±4.4cm inliers=46/102 (ranged 46)",
			};
			Assert.That(Replay(gate, lines, out _), Is.False);
			Assert.That(gate.HeldBack, Does.Contain("another place"));
		}

		[Test]
		public void TheRightPlaceWinsOnceSeenAgain()
		{
			List<string> lines = new(WrongJoin)
			{
				"26.70 yaw=-157.10°±0.30 t=(1.620, 6.050, -6.340)±3.4cm inliers=90/120 (ranged 90)",
			};
			Assert.That(Replay(Gate(new MeasurementGate.Settings()), lines, out MeasurementGate.Agreed agreed), Is.True);
			Assert.That(YawDegrees(agreed), Is.EqualTo(TrueYawDegrees).Within(1f));
		}

		[Test]
		public void ASecondViewAcceptsWhatOneWeakViewCouldNot()
		{
			MeasurementGate gate = Gate(new MeasurementGate.Settings());
			Assert.That(Replay(gate, WeakPair, out _, 59.69f), Is.False);
			string secondView = "61.20 yaw=171.20°±0.45 t=(1.360, 6.060, -14.700)±5.0cm inliers=50/90 (ranged 45)";
			Assert.That(Replay(gate, new[] { secondView }, out MeasurementGate.Agreed agreed, 59.69f), Is.True);
			Assert.That(YawDegrees(agreed), Is.EqualTo(171).Within(1f));
		}

		[Test]
		public void AStrongPairOfOneViewStillJoinsAtOnce()
		{
			Assert.That(Replay(Gate(new MeasurementGate.Settings()), StrongPair, out MeasurementGate.Agreed agreed, 44.87f), Is.True);
			Assert.That(YawDegrees(agreed), Is.EqualTo(-161.4f).Within(1f));
		}

		[Test]
		public void TheRightPlaceJoinsDespiteMimicsBetweenItsFrames()
		{
			Assert.That(Replay(Gate(new MeasurementGate.Settings()), RightThroughMimics, out MeasurementGate.Agreed agreed, 27.07f, RightJoinHead), Is.True);
			Assert.That(YawDegrees(agreed), Is.EqualTo(-10f).Within(1f));
		}

		[Test]
		public void AConfidentFrameDoesNotOverruleAStrongerPlace()
		{
			MeasurementGate gate = Gate(new MeasurementGate.Settings());
			string strong = "10.00 yaw=-10.00°±0.30 t=(14.200, -0.700, 6.000)±3.5cm inliers=216/221 (ranged 126)";
			string confidentMimic = "12.00 yaw=82.00°±0.30 t=(8.080, -0.709, -3.520)±2.5cm inliers=110/130 (ranged 90)";
			Assert.That(Replay(gate, new[] { strong, confidentMimic }, out _), Is.False);
			Assert.That(gate.HeldBack, Does.Contain("another place"));
		}

		[Test]
		public void LoopFramesAreNotContested()
		{
			MeasurementGate gate = Gate(new MeasurementGate.Settings());
			MeasurementGate.Frame first = Frame(WeakPair[0], 59.69f), second = Frame(WeakPair[1], 59.69f);
			first.purpose = second.purpose = MeasurementPurpose.Loop;
			Assert.That(gate.TryAgree(first, out _), Is.False);
			Assert.That(gate.TryAgree(second, out _), Is.True);
		}

		// ---- after the join ----------------------------------------------------------

		private static AlignmentWatch Watch(out AlignmentWatch.Settings settings)
		{
			settings = new AlignmentWatch.Settings();
			return new AlignmentWatch(settings, new PlaceVotes.Settings());
		}

		private static Pose4 Place(string line) => Frame(line).spaceInMap;

		// the place this frame says, seen meters further along so it counts as another view
		private static MeasurementGate.Frame Further(string line, float meters)
		{
			MeasurementGate.Frame frame = Frame(line);
			frame.cameraInSpace += frame.forwardInSpace * meters;
			return frame;
		}

		[Test]
		public void AWrongAlignmentIsOverturnedByTheRightPlace()
		{
			AlignmentWatch watch = Watch(out _);
			Pose4 wrong = Place(WrongJoin[7]);
			watch.Begin(new[] { Frame(WrongJoin[6]), Frame(WrongJoin[7]), Frame(WrongJoin[2]) }, wrong, 30, 25);
			Assert.That(watch.HoldsContributions(26), Is.True);

			Assert.That(watch.Add(Further(WrongJoin[2], 5), wrong, 35, out _), Is.EqualTo(AlignmentWatch.Verdict.None), "two views aren't enough");
			Assert.That(watch.Add(Further(WrongJoin[2], 10), wrong, 40, out string why), Is.EqualTo(AlignmentWatch.Verdict.Overturned));
			Assert.That(why, Does.Contain("3 views"));
		}

		[Test]
		public void ARightAlignmentIsConfirmedOnceWalkedAwayAndMimicsDoNotOverturnIt()
		{
			AlignmentWatch watch = Watch(out _);
			Pose4 right = Place(RightThroughMimics[5]);
			watch.Begin(new[] { Frame(RightThroughMimics[0]), Frame(RightThroughMimics[4]), Frame(RightThroughMimics[5]) }, right, 30, 28);

			Assert.That(watch.Add(Further(RightThroughMimics[5], 1), right, 31, out _), Is.EqualTo(AlignmentWatch.Verdict.None), "too close to prove anything");
			Assert.That(watch.HoldsContributions(30), Is.True);
			Assert.That(watch.Add(Further(RightThroughMimics[5], 6), right, 36, out _), Is.EqualTo(AlignmentWatch.Verdict.Confirmed));
			Assert.That(watch.HoldsContributions(31), Is.False);
			Assert.That(watch.CheckSeconds, Is.GreaterThan(1));

			string mimic = RightThroughMimics[3];
			for (int i = 0; i < 3; i++)
				Assert.That(watch.Add(Further(mimic, 40 + i * 2), right, 40 + i * 2, out _), Is.EqualTo(AlignmentWatch.Verdict.None));
		}

		[Test]
		public void ChecksFollowTheRigAsItsCorrectionMoves()
		{
			AlignmentWatch watch = Watch(out _);
			Pose4 right = Place(StrongPair[1]);
			watch.Begin(new[] { Frame(StrongPair[0]), Frame(StrongPair[1]) }, right, 0, 0);
			// tracking drifted, the rig followed, and the map still agrees with where it now is
			Pose4 drifted = new(right.translation + new float3(1f, 0, 0), right.yaw + math.radians(3f));
			MeasurementGate.Frame check = Further(StrongPair[1], 6);
			check.spaceInMap = drifted;
			Assert.That(watch.Add(check, drifted, 6, out _), Is.EqualTo(AlignmentWatch.Verdict.Confirmed));
		}

		[Test]
		public void WithNothingToCheckAgainstContributionsOpenAfterAWhile()
		{
			AlignmentWatch watch = Watch(out AlignmentWatch.Settings settings);
			Pose4 right = Place(StrongPair[1]);
			watch.Begin(new[] { Frame(StrongPair[1]) }, right, 0, 0);
			Assert.That(watch.HoldsContributions(settings.holdSeconds - 1), Is.True);
			Assert.That(watch.HoldsContributions(settings.holdSeconds + 1), Is.False);
		}

		[Test]
		public void AContradictedAlignmentKeepsHoldingPastTheTimeout()
		{
			AlignmentWatch watch = Watch(out AlignmentWatch.Settings settings);
			Pose4 wrong = Place(WrongJoin[7]);
			watch.Begin(new[] { Frame(WrongJoin[7]), Frame(WrongJoin[2]) }, wrong, 0, 0);
			Assert.That(watch.HoldsContributions(settings.holdSeconds + 1), Is.True);
		}

		[Test]
		public void AnEndedWatchHoldsNothing()
		{
			AlignmentWatch watch = Watch(out _);
			watch.Begin(new[] { Frame(WrongJoin[7]) }, Place(WrongJoin[7]), 0, 0);
			watch.End();
			Assert.That(watch.Watching, Is.False);
			Assert.That(watch.HoldsContributions(0), Is.False);
		}
	}
}
