using UnityEngine;

namespace LootableSpells
{
    public class D100
    {
        public static bool Roll(int threshold)
        {
            return UnityEngine.Random.Range(0, 99) < threshold;
        }
    }
}
