using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallConnect.Save;
using UnityEngine;

namespace LootableSpells
{
    public class SpellbookPageBehaviour : MonoBehaviour
    {
        void Start()
        {
            // Register listeners for loading game and exiting dungeons - so that state can be updated
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
                    //This is a little weird, maybe move the logic from the property to here?
                    Debug.LogFormat("Restoring page with spell id {0}", spellbookPage.message);
                    int spellID = spellbookPage.message;
                    spellbookPage.SpellID = spellID;
                }
            }
        }
        #endregion
    }
}
