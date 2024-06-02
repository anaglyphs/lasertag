using Anaglyph.LaserTag;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HealthDriver : MonoBehaviour
{

    [SerializeField]
    private RectTransform healthBarTransform;

    [SerializeField]
    private Image healthBarImage;

    private float maxBarWidth = 0;

    private Color barColor = Color.white;

	void Start()
    {
        maxBarWidth = healthBarTransform.sizeDelta.x;

		PlayerLocal.Instance.onTakeDamage.AddListener(OnPlayerHit);
    }

    void OnPlayerHit()
    {
        barColor = new Color(255f / 255f, 99f / 255f, 99f / 255f, 1f);
    }

    void Update()
    {
        barColor = Color.Lerp(barColor, Color.white, Time.deltaTime * 3.5f);

        float healthPercent = PlayerLocal.Instance.health / PlayerLocal.Instance.currentRole.MaxHealth;

        healthBarTransform.sizeDelta = new Vector2(Mathf.Lerp(0, maxBarWidth, healthPercent), healthBarTransform.sizeDelta.y);

        if (!PlayerLocal.Instance.alive)
        {
            healthBarImage.color = Color.Lerp(barColor, new Color(255f / 255f, 99f / 255f, 99f / 255f, 1f), Mathf.Clamp01(PlayerLocal.Instance.currentRole.RespawnTimeSeconds - PlayerLocal.Instance.respawnTimer));

            return;
        }

        Color flashColor = Color.Lerp(new Color(255f / 255f, 99f / 255f, 99f / 255f, 1f), barColor, Mathf.Abs(Mathf.Sin(Time.time * 10)));

        healthBarImage.color = Color.Lerp(flashColor, barColor, Mathf.Clamp01((healthPercent - 0.2f) / 0.2f));
    }
}
