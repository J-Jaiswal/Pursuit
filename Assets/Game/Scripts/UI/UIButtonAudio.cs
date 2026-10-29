using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Self-contained button audio component.
/// Uses its own AudioSource — no external manager dependency,
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class UIButtonAudio : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    [Header("Clips")]
    [Tooltip("Sound played when the pointer enters this button.")]
    [SerializeField] private AudioClip hoverClip;

    [Tooltip("Sound played when this button is clicked.")]
    [SerializeField] private AudioClip clickClip;

    [Header("Settings")]
    [Range(0f, 1f)]
    [SerializeField] private float volume = 0.8f;

    private AudioSource _source;
    private Button      _button;

    private void Awake()
    {
        _button = GetComponent<Button>();

        // Create a dedicated 2D AudioSource on this GameObject
        _source              = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake  = false;
        _source.spatialBlend = 0f;   // 2D stereo — no positional falloff
        _source.volume       = volume;
    }

    // IPointerEnterHandler 

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_button != null && !_button.interactable) return;
        Play(hoverClip);
    }

    // IPointerClickHandler 

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_button != null && !_button.interactable) return;
        Play(clickClip);
    }

    //  Internal 

    private void Play(AudioClip clip)
    {
        if (clip == null || _source == null) return;
        _source.PlayOneShot(clip, volume);
    }
}
