using System;
using UnityEngine;

namespace Demo.Test
{
    [Serializable]
    public class Teacher : IPerson
    {
        [SerializeField] int m_teachID;
    }
}
