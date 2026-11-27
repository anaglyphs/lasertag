using Anaglyph.LaserTag.Player.Teams;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Interface.HUD
{
	/// <summary>
	/// Maps SVG coordinates onto an element whose background draws that SVG with
	/// background-size: contain, centred, so drawing lines up with the background.
	/// </summary>
	internal readonly struct ContainedSvgSpace
	{
		private readonly Vector2 offset;
		public readonly float Scale;

		public ContainedSvgSpace(Rect box, Vector2 svgSize)
		{
			Scale = Mathf.Min(box.width / svgSize.x, box.height / svgSize.y);
			offset = box.position + (box.size - svgSize * Scale) * 0.5f;
		}

		public Vector2 ToElement(float svgX, float svgY) => offset + new Vector2(svgX, svgY) * Scale;
	}

	/// <summary>
	/// Sand that drains from hourglass.svg's top chamber into its bottom chamber.
	/// Both chambers fill by height, like vertical progress bars.
	/// </summary>
	public sealed class HourglassSand : VisualElement
	{
		private static readonly Vector2 SvgSize = new(162f, 170f);
		private static readonly CustomStyleProperty<Color> SandColorProperty = new("--sand-color");

		// the chambers' inner trapezoids, from hourglass.svg's sand paths
		private const float CenterX = 81f;
		private const float RimHalfWidth = 70f;
		private const float NeckHalfWidth = 26.25f;
		private const float TopRimY = 5f, TopNeckY = 55f;
		private const float BottomNeckY = 115f, BottomRimY = 165f;

		private float elapsedFraction;
		private Color sandColor = Color.white;

		public HourglassSand()
		{
			pickingMode = PickingMode.Ignore;
			generateVisualContent += Draw;
			RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
		}

		public void SetElapsedFraction(float fraction)
		{
			fraction = Mathf.Clamp01(fraction);
			if (Mathf.Approximately(fraction, elapsedFraction)) return;

			elapsedFraction = fraction;
			MarkDirtyRepaint();
		}

		private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
		{
			if (!evt.customStyle.TryGetValue(SandColorProperty, out Color color)) return;

			sandColor = color;
			MarkDirtyRepaint();
		}

		private void Draw(MeshGenerationContext context)
		{
			var space = new ContainedSvgSpace(contentRect, SvgSize);
			Painter2D painter = context.painter2D;
			painter.fillColor = sandColor;

			float chamberHeight = TopNeckY - TopRimY;

			float topSurfaceY = TopRimY + elapsedFraction * chamberHeight;
			FillChamberSlice(painter, space, topSurfaceY, TopNeckY, TopRimY, TopNeckY);

			float bottomSurfaceY = BottomRimY - elapsedFraction * chamberHeight;
			FillChamberSlice(painter, space, bottomSurfaceY, BottomRimY, BottomRimY, BottomNeckY);
		}

		private static void FillChamberSlice(Painter2D painter, ContainedSvgSpace space,
			float sliceTopY, float sliceBottomY, float rimY, float neckY)
		{
			if (sliceBottomY - sliceTopY <= 0.01f) return;

			float topHalfWidth = HalfWidthAt(sliceTopY, rimY, neckY);
			float bottomHalfWidth = HalfWidthAt(sliceBottomY, rimY, neckY);

			painter.BeginPath();
			painter.MoveTo(space.ToElement(CenterX - topHalfWidth, sliceTopY));
			painter.LineTo(space.ToElement(CenterX + topHalfWidth, sliceTopY));
			painter.LineTo(space.ToElement(CenterX + bottomHalfWidth, sliceBottomY));
			painter.LineTo(space.ToElement(CenterX - bottomHalfWidth, sliceBottomY));
			painter.ClosePath();
			painter.Fill();
		}

		private static float HalfWidthAt(float y, float rimY, float neckY) =>
			Mathf.Lerp(RimHalfWidth, NeckHalfWidth, Mathf.InverseLerp(rimY, neckY, y));
	}

	/// <summary>
	/// Team-coloured lines racing up mountain.svg: along the flat first, then up
	/// the slope, reaching the peak at the score target.
	/// </summary>
	public sealed class MountainClimb : VisualElement
	{
		private const float SvgWidth = 400f;
		private static readonly Vector2 SvgSize = new(SvgWidth, 147f);
		private const float LineWidth = 16f;
		private const float ProgressPerSecond = 1.5f;

		// blue climbs from the left; red climbs the mirror image from the right
		private static readonly Vector2[] BlueClimbPath =
		{
			new(0f, 122f),
			new(90f, 122f),
			new(205.5f, 6.5f),
		};

		private readonly float[] shownProgress = new float[Teams.NumTeams];

		public MountainClimb()
		{
			pickingMode = PickingMode.Ignore;
			generateVisualContent += Draw;
		}

		/// <summary>Call every frame; the line eases toward the given progress.</summary>
		public void AnimateTowardProgress(byte team, float progress)
		{
			progress = Mathf.Clamp01(progress);
			float shown = shownProgress[team];
			if (shown == progress) return;

			shownProgress[team] = Mathf.MoveTowards(shown, progress, ProgressPerSecond * Time.deltaTime);
			MarkDirtyRepaint();
		}

		private void Draw(MeshGenerationContext context)
		{
			var space = new ContainedSvgSpace(contentRect, SvgSize);
			Painter2D painter = context.painter2D;
			painter.lineWidth = LineWidth * space.Scale;
			painter.lineCap = LineCap.Butt;
			painter.lineJoin = LineJoin.Miter;

			DrawClimb(painter, space, shownProgress[Teams.Blue], Teams.Colors[Teams.Blue], false);
			DrawClimb(painter, space, shownProgress[Teams.Red], Teams.Colors[Teams.Red], true);
		}

		private static void DrawClimb(Painter2D painter, ContainedSvgSpace space,
			float progress, Color color, bool mirrored)
		{
			if (progress <= 0f) return;

			float lengthLeft = progress * PathLength(BlueClimbPath);

			painter.strokeColor = color;
			painter.BeginPath();
			painter.MoveTo(ToElement(space, BlueClimbPath[0], mirrored));

			for (int i = 1; i < BlueClimbPath.Length && lengthLeft > 0f; i++)
			{
				Vector2 from = BlueClimbPath[i - 1];
				Vector2 to = BlueClimbPath[i];
				float segmentLength = Vector2.Distance(from, to);

				Vector2 end = lengthLeft >= segmentLength
					? to
					: Vector2.Lerp(from, to, lengthLeft / segmentLength);

				painter.LineTo(ToElement(space, end, mirrored));
				lengthLeft -= segmentLength;
			}

			painter.Stroke();
		}

		private static Vector2 ToElement(ContainedSvgSpace space, Vector2 svgPoint, bool mirrored) =>
			space.ToElement(mirrored ? SvgWidth - svgPoint.x : svgPoint.x, svgPoint.y);

		private static float PathLength(Vector2[] path)
		{
			float length = 0f;
			for (int i = 1; i < path.Length; i++)
				length += Vector2.Distance(path[i - 1], path[i]);
			return length;
		}
	}
}
