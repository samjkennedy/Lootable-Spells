using DaggerfallConnect.Save;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LootableSpells
{
    public class SpellbookPageBehaviour : MonoBehaviour
    {
        void Start()
        {
            SaveLoadManager.OnLoad += SaveLoadManager_OnLoad;
        }

        #region Event listeners
        private void SaveLoadManager_OnLoad(SaveData_v1 saveData)
        {
            List<DaggerfallUnityItem> spellPages = GameManager.Instance.PlayerEntity.Items.SearchItems(ItemGroups.MagicItems, SpellbookPageItem.templateIndex);
            foreach (DaggerfallUnityItem item in spellPages)
            {
                if (item is SpellbookPageItem spellbookPage)
                {
                    spellbookPage.SpellID = spellbookPage.message;
                }
            }
        }
        #endregion
    }
}
