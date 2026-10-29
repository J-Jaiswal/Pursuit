using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Static animation helpers shared across UIWarnings and UIPanels.
/// Extracts PulseTextScaleCoroutine and SnapAlphaToZero from UIManager
/// </summary>
public static class UIAnimUtils
{
  
    /// Smoothly oscillates a TMP_Text's localScale using a sine wave on unscaled time.
    /// Uses unscaledTime so pulses work correctly when TimeScale = 0 (game paused).
    /// The caller owns the Coroutine handle -- StartCoroutine / StopCoroutine as needed.
  
   
    public static IEnumerator PulseTextScale(
        TMP_Text text, Vector3 baseScale,
        float minFactor, float maxFactor, float speed)
    {
        if (text == null) yield break;

        Transform t = text.transform;
        while (true)
        {
            float wave  = (Mathf.Sin(Time.unscaledTime * speed) + 1f) * 0.5f;
            float scale = Mathf.Lerp(minFactor, maxFactor, wave);
            if (t != null)
                t.localScale = baseScale * scale;
            yield return null;
        }
    }


    /// Instantly snaps an Image's alpha channel to zero and disables the component.
    /// Safe to call with a null Image -- returns silently.
 
    public static void SnapAlphaToZero(Image img)
    {
        if (img == null) return;
        Color c = img.color;
        c.a     = 0f;
        img.color   = c;
        img.enabled = false;
    }
}
