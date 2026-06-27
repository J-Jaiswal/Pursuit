using UnityEngine;

public enum HazardType { Sharp, Crushing, Explosive }

public class Hazard : MonoBehaviour
{
    [SerializeField] private HazardType type;
    [SerializeField] private float damage = 20f;
    [SerializeField] private float damageInterval = 0.5f;
    [SerializeField] private float explosionRadius = 5f;

    public HazardType Type => type;
    public float Damage => damage;
    public float DamageInterval => damageInterval;
    public float ExplosionRadius => explosionRadius;
}
