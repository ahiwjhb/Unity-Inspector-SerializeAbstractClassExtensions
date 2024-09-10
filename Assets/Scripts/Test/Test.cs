using Core;
using Demo.Test;
using UnityEngine;

public class Test : MonoBehaviour
{
    [SerializeExtension(nameInEditorWindow: "血量", proxyPropertyName: nameof(Health))]
    [SerializeField] int health;

    [SerializeExtension]
    [SerializeReference] IPerson peopel;


    [SerializeField] int a = 10;

    [SerializeExtension(canWrite: false)]
    [SerializeField] int b = 20;

    public int Health {
        get => health;
        set => health = Mathf.Clamp(value, 0, 100);
    }

}
