using DaggerfallConnect;
using DaggerfallConnect.FallExe;
using DaggerfallConnect.Save;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallWorkshop.Game.Player;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings;
using DaggerfallWorkshop.Utility;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LootableSpells
{
    public class LootableSpellsMod : MonoBehaviour
    {
        public static LootableSpellsMod Instance;
        static Mod mod;

        private bool shopsHaveSpellPages;
        private bool npcsDropSpellPages;
        private bool dungeonsHaveSpellPages;
        private float spellPageFrequency;

        [Invoke(StateManager.StateTypes.Start, 0)]
        public static void Init(InitParams initParams)
        {
            mod = initParams.Mod;

            var go = new GameObject(mod.Title);
            go.AddComponent<LootableSpellsMod>();
        }

        void Awake()
        {
            Instance = this;

            ModSettings settings = mod.GetSettings();
            shopsHaveSpellPages = settings.GetValue<bool>("Availability", "Shops");
            dungeonsHaveSpellPages = settings.GetValue<bool>("Availability", "DungeonLoot");
            npcsDropSpellPages = settings.GetValue<bool>("Availability", "NPCLoot");
            spellPageFrequency = settings.GetValue<float>("Availability", "FrequencyMultiplier");

            InitMod();
        }

        void InitMod()
        {
            Debug.Log("Begin mod init: Lootable Spells");

            RegisterNewItems();

            // Add spellpage behaviour to scene and attach script
            GameObject go = new GameObject("LootableSpells_SpellbookPageBehaviour");
            go.AddComponent<SpellbookPageBehaviour>();

            if (shopsHaveSpellPages)
                PlayerActivate.OnLootSpawned += AddSpellPages_OnLootSpawned;

            if (dungeonsHaveSpellPages)
                LootTables.OnLootSpawned += AddSpellPages_OnDungeonLootSpawned;

            if (npcsDropSpellPages)
                EnemyDeath.OnEnemyDeath += AddSpellPages_OnEnemyDeath;

            Debug.Log("Finished mod init: Lootable Spells");
            mod.IsReady = true;
        }

        #region Registering
        private void RegisterNewItems()
        {
            StartGameBehaviour startGameBehaviour = GameManager.Instance.StartGameBehaviour;

            DaggerfallUnity.Instance.ItemHelper.RegisterCustomItem(SpellbookPageItem.templateIndex, ItemGroups.MiscItems, typeof(SpellbookPageItem));

            Debug.Log("Lootable Spells: Registered Custom Items");
        }
        #endregion

        #region New Behaviour
        /*Magicono43 — Today at 20:54
            Me personally, I'd probably make pawn shops have the best, but have a very small chance of having them. 
            book stores have the best on average and most commonly stocked, 
            and general stores being the lowest quality but a moderate chance, etc.
        */
        public void AddSpellPages_OnLootSpawned(object sender, ContainerLootSpawnedEventArgs e)
        {
            DaggerfallInterior interior = GameManager.Instance.PlayerEnterExit.Interior;
            if (interior == null || e.ContainerType != LootContainerTypes.ShopShelves)
                return;

            int spellbookPageChance = 0;
            int maxPagesPerShelf = 0;
            switch (interior.BuildingData.BuildingType)
            {
                case DFLocation.BuildingTypes.Bookseller:
                    spellbookPageChance = 25;
                    maxPagesPerShelf = 2;
                    break;

                case DFLocation.BuildingTypes.GeneralStore:
                    spellbookPageChance = 8;
                    maxPagesPerShelf = 1;
                    break;

                case DFLocation.BuildingTypes.PawnShop:
                    spellbookPageChance = 4;
                    maxPagesPerShelf = 1;
                    break;

                default:
                    return;
            }

            if (D100.Roll((int)(spellbookPageChance * spellPageFrequency)))
            {
                int numSpellPages = UnityEngine.Random.Range(1, maxPagesPerShelf);

                for (int i = 0; i < numSpellPages; i++)
                {
                    SpellbookPageItem spellPage = SpellbookPageItem.GenerateRandomSpellbookPage();
                    if (spellPage == null)
                        continue;

                    spellPage.value *= 2;
                    e.Loot.AddItem(spellPage);
                }
            }
        }

        public void AddSpellPages_OnEnemyDeath(object sender, EventArgs e)
        {
            EnemyDeath enemyDeath = sender as EnemyDeath;
            if (enemyDeath == null)
                return;

            DaggerfallEntityBehaviour entityBehaviour = enemyDeath.GetComponent<DaggerfallEntityBehaviour>();
            if (entityBehaviour == null)
                return;

            EnemyEntity enemyEntity = entityBehaviour.Entity as EnemyEntity;
            if (enemyEntity == null)
                return;

            int spellbookPageChance = 0;
            int enemyID = enemyEntity.MobileEnemy.ID;
            switch (enemyID)
            {
                case (int)MobileTypes.Mage:
                    spellbookPageChance = 25;
                    break;

                case (int)MobileTypes.Sorcerer:
                    spellbookPageChance = 18;
                    break;

                case (int)MobileTypes.Healer:
                    spellbookPageChance = 15; //TODO: try to spawn only restoration spells somehow
                    break;

                case (int)MobileTypes.Spellsword:
                case (int)MobileTypes.Battlemage:
                    spellbookPageChance = 10;
                    break;

                default:
                    return;
            }

            if (D100.Roll((int)(spellbookPageChance * spellPageFrequency)))
            {
                SpellbookPageItem spellPage = SpellbookPageItem.GenerateRandomSpellbookPage();
                if (spellPage == null)
                    return;

                entityBehaviour.CorpseLootContainer.Items.AddItem(spellPage);
            }
        }

        public void AddSpellPages_OnDungeonLootSpawned(object sender, TabledLootSpawnedEventArgs lootArgs)
        {
            PlayerEntity playerEntity = GameManager.Instance.PlayerEntity;
            ItemHelper itemHelper = DaggerfallUnity.Instance.ItemHelper;

            int maxSpellPages = 0;

            //TODO: Spell quality - you should get better spells if you're in a harder dungeon
            //      Perhaps even hard code certain spells in certain dungeons - vampires will have sleep and paralysis, orc shamans invisibility
            switch (lootArgs.LocationIndex)
            {
                case (int)DFRegion.DungeonTypes.OrcStronghold:
                    if (D100.Roll((int)(5 * spellPageFrequency)))
                        maxSpellPages = 1;
                    break;

                case (int)DFRegion.DungeonTypes.HumanStronghold:
                    if (D100.Roll((int)(5 * spellPageFrequency)))
                        maxSpellPages = 1;
                    break;

                case (int)DFRegion.DungeonTypes.DesecratedTemple:
                    if (D100.Roll((int)(15 * spellPageFrequency)))
                        maxSpellPages = 1;
                    break;

                case (int)DFRegion.DungeonTypes.Coven:
                    //Do witches even use spellbooks?
                    if (D100.Roll((int)(25 * spellPageFrequency)))
                        maxSpellPages = 2;
                    break;

                case (int)DFRegion.DungeonTypes.VampireHaunt:
                    if (D100.Roll((int)(20 * spellPageFrequency)))
                        maxSpellPages = 1;
                    break;

                case (int)DFRegion.DungeonTypes.Laboratory:
                    if (D100.Roll((int)(30 * spellPageFrequency)))
                        maxSpellPages = 2;
                    break;

                default:
                    return;
            }

            int numSpellPages = UnityEngine.Random.Range(0, maxSpellPages);
            for (int i = 0; i < numSpellPages; i++)
            {
                SpellbookPageItem spellPage = SpellbookPageItem.GenerateRandomSpellbookPage();
                if (spellPage == null)
                    continue;

                lootArgs.Items.AddItem(spellPage);
            }
        }
        #endregion
    }
}
