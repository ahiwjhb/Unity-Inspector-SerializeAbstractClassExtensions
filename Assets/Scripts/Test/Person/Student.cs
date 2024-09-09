using Demo.Test;
using System;
using UnityEngine;

namespace Demo.Test
{
    [Serializable]
    public class Student : IPerson
    {
        [SerializeField] int student_id;
    }
}
