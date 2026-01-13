using UnityEngine;
using UnityEngine.UI;

public class EngineThrustBar : MonoBehaviour
{
    [SerializeField] Gradient gradient;
    [SerializeField] Image fill;
    Slider slider;
    Movements movements;
  
    void Start()
    {
        slider = GetComponent<Slider>();
        movements = FindFirstObjectByType<Movements>();

        fill.color = gradient.Evaluate(1f); 

    }

    void Update()
    {
        slider.value = movements.currentThrust / movements.maxThrust;

        fill.color = gradient.Evaluate(slider.normalizedValue); 

    }
}
