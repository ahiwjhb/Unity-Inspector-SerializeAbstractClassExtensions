using Core;
using System;
using UnityEngine;

namespace Demo.Test
{
    [Serializable]
    public class Student : IPerson
    {
        [SerializeExtension(canWrite: false)]
        [SerializeField] string m_name = "ABC";
    }
}
