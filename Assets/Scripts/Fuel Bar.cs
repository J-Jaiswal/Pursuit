using UnityEngine;
using UnityEngine.UI;

public class FuelBar : MonoBehaviour
{
    [SerializeField] Gradient gradient;
    [SerializeField] Image fill;
    Slider slider;
    Fuel fuel;
    void Start()
    {
        slider = GetComponent<Slider>();
        fuel = FindFirstObjectByType<Fuel>();

        fill.color = gradient.Evaluate(1f); 

    }

    void Update()
    {
        slider.value = fuel.currentFuel / fuel.maxFuel;

        fill.color = gradient.Evaluate(slider.normalizedValue); 

    }
}
