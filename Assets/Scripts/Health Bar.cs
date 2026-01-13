using UnityEngine;
using UnityEngine.UI;

public class HealthBar : MonoBehaviour
{
    [SerializeField] Gradient gradient;
    [SerializeField] Image fill;
    Slider slider;
    CollisionHandler collisionHandler;
  
    void Start()
    {
        collisionHandler = FindFirstObjectByType<CollisionHandler>();  
        slider = GetComponent<Slider>();

        
        fill.color = gradient.Evaluate(1f); 

    }

    void Update()
    {
        
        slider.value = collisionHandler.currentHealth / collisionHandler.maxHealth;
        fill.color = gradient.Evaluate(slider.normalizedValue); 

    }
}
