using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
    public class GlobalMessage : SingletonBehavior<GlobalMessage>
    {
        [SerializeField] private Text text;

		private void OnValidate()
		{
			this.SetComponent(ref text);
		}

		public void Set(string str)
		{
			text.text = str;
		}

		protected override void OnSingletonDestroy()
		{
			
		}

		protected override void SingletonAwake()
		{
			
		}
	}
}
