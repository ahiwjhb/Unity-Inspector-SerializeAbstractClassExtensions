using System;
using UnityEngine;

namespace Demo.Test
{
    [Serializable]
    public class Teacher : IPerson
    {
        [SerializeField] float teach_id;

        [SerializeField] int student_count;
    }
}
