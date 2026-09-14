using System;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>Independent visit tokens reject work from an obsolete layout, reference frame or scan.</summary>
	internal sealed class MapVisitContext
	{
		public Guid MapContext { get; set; } = Guid.NewGuid();
		public Guid ReferenceContext { get; set; } = Guid.NewGuid();
		public Guid ScanContext { get; set; } = Guid.NewGuid();
	}
}
