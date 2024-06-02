using UnityEngine.UI;

namespace Anaglyph.Menu
{
	public class SliderLabel : EasyLabel
    {
        private Slider _slider;
        private Text _label;
        
        private void Start()
        {
            _slider = GetComponentInChildren<Slider>();
            _label = GetComponentInChildren<Text>();
            
            _slider.onValueChanged.AddListener(delegate(float f)
            {
                _label.text = gameObject.name + ": <b>" + f.ToString("0.00") + "</b>";
            });
            
            _slider.onValueChanged.Invoke(_slider.value);
        }
    }
}
