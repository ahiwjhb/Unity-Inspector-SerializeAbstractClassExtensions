using Core;
using Demo.Test;
using UnityEngine;

public class Test : MonoBehaviour
{
    //[SerializeExtension]
    //[SerializeReference] IPerson peopel;

    //[SerializeExtension(proxyPropertyName: nameof(Health))]
    //[SerializeField] int health;

    //public int Health {
    //    get => health;
    //    set => health = Mathf.Clamp(value, 0, 100);
    //}

    [SerializeExtension]
    [SerializeReference] IPerson peopel;


    [SerializeField] int a = 10;

    [SerializeExtension(canWrite: false)]
    [SerializeField] int b = 20;
}
