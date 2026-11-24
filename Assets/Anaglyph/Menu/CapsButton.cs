using UnityEngine.UIElements;

namespace Anaglyph.Menu
{
	/// <summary>A button whose text always displays in capitals, as nav titles do.</summary>
	[UxmlElement]
	public partial class CapsButton : Button
	{
		public override string text
		{
			get => base.text;
			set => base.text = value?.ToUpperInvariant();
		}
	}
}
