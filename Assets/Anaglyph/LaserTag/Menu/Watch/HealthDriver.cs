using Anaglyph.LaserTag;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
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

            MainPlayer.Instance.onTakeDamage.AddListener(OnPlayerHit);
        }

        void OnPlayerHit()
        {
            barColor = new Color(255f / 255f, 99f / 255f, 99f / 255f, 1f);
        }

        void Update()
        {
            barColor = Color.Lerp(barColor, Color.white, Time.deltaTime * 3.5f);

            float healthPercent = MainPlayer.Instance.Health / MainPlayer.Instance.currentRole.MaxHealth;

            healthBarImage.fillAmount = healthPercent;

            if (!MainPlayer.Instance.IsAlive)
            {
                healthBarImage.color = Color.Lerp(barColor, new Color(255f / 255f, 99f / 255f, 99f / 255f, 1f), Mathf.Clamp01(MainPlayer.Instance.RespawnTimerSeconds));

                return;
            }

            Color flashColor = Color.Lerp(new Color(255f / 255f, 99f / 255f, 99f / 255f, 1f), barColor, Mathf.Abs(Mathf.Sin(Time.time * 10)));

            healthBarImage.color = Color.Lerp(flashColor, barColor, Mathf.Clamp01((healthPercent - 0.2f) / 0.2f));
        }
    }
}