using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class ButtonHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Animation Settings")]
    [Tooltip("Scale multiplier applied on hover (1.12 = 12% bigger).")]
    [SerializeField] private float hoverScale = 1.12f;

    [Tooltip("How fast the scale animates, in seconds.")]
    [SerializeField] private float animSpeed = 0.12f;

    private RectTransform _rect;
    private Button _button;
    private Vector3 _baseScale;
    private Vector3 _targetScale;
    private Coroutine _scaleRoutine;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _button = GetComponent<Button>();
        _baseScale = _rect.localScale;
        _targetScale = _baseScale;
    }

    private void OnEnable()
    {
        if (_rect != null)
        {
            _rect.localScale = _baseScale;
            _targetScale = _baseScale;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_button != null && !_button.interactable) return;
        SetTarget(_baseScale * hoverScale);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetTarget(_baseScale);
    }

    private void SetTarget(Vector3 target)
    {
        _targetScale = target;
        if (_scaleRoutine != null) StopCoroutine(_scaleRoutine);
        _scaleRoutine = StartCoroutine(AnimateScale());
    }

    private System.Collections.IEnumerator AnimateScale()
    {
        while (Vector3.Distance(_rect.localScale, _targetScale) > 0.001f)
        {
            _rect.localScale = Vector3.Lerp(_rect.localScale, _targetScale, Time.unscaledDeltaTime / animSpeed);
            yield return null;
        }
        _rect.localScale = _targetScale;
    }
}