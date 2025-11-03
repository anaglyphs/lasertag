using System;
using Anaglyph.Lasertag.Networking;
using UnityEngine;

namespace Anaglyph.Lasertag
{
    public class FlagSFX : MonoBehaviour
    {
	    [SerializeField] private Flag flag;
	    
	    [SerializeField] private AudioClip scored;
	    [SerializeField] private AudioClip enemyCapturedFlag;
	    [SerializeField] private AudioClip enemyStoleFlag;

	    private void OnEnable()
	    {
		    flag.PickedUp += OnPickedUp;
		    flag.Captured += OnCaptured;
	    }

	    private void OnDisable()
	    {
		    flag.PickedUp -= OnPickedUp;
		    flag.Captured -= OnCaptured;
	    }

	    private void OnPickedUp(PlayerAvatar holder)
	    {
		    if (PlayerAvatar.Local?.Team != holder.Team)
		    {
			    var pos = holder.HeadTransform.position;
			    AudioSource.PlayClipAtPoint(enemyStoleFlag, pos);
		    }
	    }

	    private void OnCaptured(PlayerAvatar holder)
	    {
		    var pos = holder.HeadTransform.position;
		    var sfx = scored;

		    if (PlayerAvatar.Local && PlayerAvatar.Local?.Team != holder.Team)
			    sfx = enemyCapturedFlag;
		    
		    AudioSource.PlayClipAtPoint(sfx, pos);
	    }
    }
}
