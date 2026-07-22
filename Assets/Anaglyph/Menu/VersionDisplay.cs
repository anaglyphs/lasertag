using TMPro;
using UnityEngine;
using VariableObjects;

namespace Anaglyph.Menu
{
    public class VersionDisplay : MonoBehaviour
    {
	    [SerializeField] private StringObject buildNumberStrObj;

	    [Header("Use {0} for version and {1} for build number")]
	    [SerializeField, TextArea]
	    private string contents =
		    "Version: {0}\nbuild number: {1}.";
	    
	    private TMP_Text label;

	    private void Awake()
	    {
		    TryGetComponent(out label);

		    label.text = string.Format(
			    contents,
			    Application.version,
			    buildNumberStrObj.Value);
	    }
    }
}
