using System;
using System.Collections;
using UnityEngine;
using DaggerfallConnect;
using DaggerfallConnect.Save;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Player;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using System.Collections.Generic;
using DaggerfallConnect.FallExe;

namespace LootableSpells
{
    public class LootableSpellsMod : MonoBehaviour
    {
        public static LootableSpellsMod Instance;
        static Mod mod;

        // Settings
        public static int spellPageChancePercent = 35; //config
        public static int spellTomeChancePercent = 5; //config

        //Prefabs
        public GameObject Glyph;

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

            // ModSettings settings = mod.GetSettings();
            // string spawnFrequencySection = "Spawn Frequency";
            // spellPageShopFrequency = settings.GetFloat(spawnFrequencySection, "Spell Page Shop Spawn Frequency");

            InitMod(/*TODO: Mod settings*/);
        }

        void InitMod(/*TODO: Mod settings*/)
        {
            Debug.Log("Begin mod init: Lootable Spells");

            RegisterNewItems();

            // Add spellpage behaviour to scene and attach script
            GameObject go = new GameObject("LootableSpells_SpellbookPageBehaviour");
            go.AddComponent<SpellbookPageBehaviour>();

            //TODO Make these settings
            PlayerActivate.OnLootSpawned += AddSpellPages_OnLootSpawned;
            EnemyDeath.OnEnemyDeath += AddSpellPages_OnEnemyDeath;
            LootTables.OnLootSpawned += AddSpellPages_OnDungeonLootSpawned;

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
        public static void AddSpellPages_OnLootSpawned(object sender, ContainerLootSpawnedEventArgs e)
        {
            if (UnityEngine.Random.Range(0, 100) > spellPageChancePercent)
                return;

            DaggerfallInterior interior = GameManager.Instance.PlayerEnterExit.Interior;
            if (interior == null || e.ContainerType != LootContainerTypes.ShopShelves)
                return;

            //TODO: Tweak this until it feels right, they should be rare but not impossible to find
            //      Needs to be exponentially harder to get more spell pages, use a log?
            //      Also allow this to be configurable
            int maxSpellPages = 0;
            switch (interior.BuildingData.BuildingType)
            {
                case DFLocation.BuildingTypes.Bookseller:
                    maxSpellPages = 6;
                    break;
                case DFLocation.BuildingTypes.GeneralStore:
                    maxSpellPages = 3;
                    break;
                case DFLocation.BuildingTypes.PawnShop:
                    maxSpellPages = 2;
                    break;
                default:
                    return;
            }

            const float maxBuildingQuality = 20;
            int numSpellPages = UnityEngine.Random.Range(0, (maxSpellPages * (int)((float)interior.BuildingData.Quality / maxBuildingQuality)));

            for (int i = 0; i < numSpellPages; i++)
            {
                SpellbookPageItem spellPage = SpellbookPageItem.GenerateRandomSpellbookPage();
                if (spellPage == null)
                {
                    continue;
                }
                spellPage.value *= 2; //Maybe configurable too?
                e.Loot.AddItem(spellPage);

            }
        }

        public static void AddSpellPages_OnEnemyDeath(object sender, EventArgs e)
        {
            if (UnityEngine.Random.Range(0, 100) > spellPageChancePercent)
                return;

            EnemyDeath enemyDeath = sender as EnemyDeath;
            if (enemyDeath == null)
                return;

            DaggerfallEntityBehaviour entityBehaviour = enemyDeath.GetComponent<DaggerfallEntityBehaviour>();
            if (entityBehaviour == null)
                return;

            EnemyEntity enemyEntity = entityBehaviour.Entity as EnemyEntity;
            if (enemyEntity == null)
                return;

            int maxSpellPages = 0;
            int enemyID = enemyEntity.MobileEnemy.ID;
            switch (enemyID)
            {
                case (int)MobileTypes.Mage:
                    maxSpellPages = 3;
                    break;

                case (int)MobileTypes.Sorcerer:
                    maxSpellPages = 2;
                    break;

                case (int)MobileTypes.Healer:
                    maxSpellPages = 2; //TODO: but try to spawn only restoration spells somehow
                    break;

                case (int)MobileTypes.Spellsword:
                case (int)MobileTypes.Battlemage:
                    maxSpellPages = 1;
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

                entityBehaviour.CorpseLootContainer.Items.AddItem(spellPage);
            }
        }

        public static void AddSpellPages_OnDungeonLootSpawned(object sender, TabledLootSpawnedEventArgs lootArgs)
        {
            //No global spell page chance check since it varies based on dungeon type

            PlayerEntity playerEntity = GameManager.Instance.PlayerEntity;
            ItemHelper itemHelper = DaggerfallUnity.Instance.ItemHelper;

            int maxSpellPages = 0;

            //TODO: Spell quality - you should get better spells if you're in a harder dungeon or are a higher level
            //      Perhaps even hard code certain spells in certain dungeons - vampires will have sleep and paralysis, orc shamans invisibility
            switch (lootArgs.LocationIndex)
            {
                case (int)DFRegion.DungeonTypes.OrcStronghold:
                    if (UnityEngine.Random.Range(0, 100) < 5)
                        maxSpellPages = 1;
                    break;

                case (int)DFRegion.DungeonTypes.HumanStronghold:
                    if (UnityEngine.Random.Range(0, 100) < 5)
                        maxSpellPages = 1;
                    break;

                case (int)DFRegion.DungeonTypes.DesecratedTemple:
                    if (UnityEngine.Random.Range(0, 100) < 15)
                        maxSpellPages = 1;
                    break;

                case (int)DFRegion.DungeonTypes.Coven:
                    //Do witches even use spellbooks?
                    if (UnityEngine.Random.Range(0, 100) < 25)
                        maxSpellPages = 2;
                    break;

                case (int)DFRegion.DungeonTypes.VampireHaunt:
                    if (UnityEngine.Random.Range(0, 100) < 20)
                        maxSpellPages = 1;
                    break;

                case (int)DFRegion.DungeonTypes.Laboratory:
                    if (UnityEngine.Random.Range(0, 100) < 30)
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
