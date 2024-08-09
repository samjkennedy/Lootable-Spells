using DaggerfallConnect;
using DaggerfallConnect.FallExe;
using DaggerfallConnect.Save;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
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
using System.Linq;
using UnityEngine;

namespace LootableSpells
{
    public class LootableSpellsMod : MonoBehaviour
    {
        public static LootableSpellsMod Instance;

        static Mod mod;

        #region Settings
        private bool shopsHaveSpellPages;
        private bool npcsDropSpellPages;
        private bool dungeonsHaveSpellPages;
        private float spellPageFrequency;
        private bool unleveledLoot;
        #endregion

        #region Spell Quality
        private Dictionary<int, List<int>> spellIndicesByQuality;

        //in lieu of a proper enum
        private const int QUALITY_UNLEVELED = 0;
        private const int QUALITY_LOWEST = 1;
        private const int QUALITY_LOW = 2;
        private const int QUALITY_MED = 3;
        private const int QUALITY_HIGH = 4;
        private const int QUALITY_HIGHEST = 5;

        private int[] QUALITY_GOLD_THRESHOLDS = {
            -1,
            200,
            500,
            1000,
            2000,
            Int32.MaxValue
        };
        #endregion

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
            //TODO: struct?
            shopsHaveSpellPages = settings.GetValue<bool>("Availability", "Shops");
            dungeonsHaveSpellPages = settings.GetValue<bool>("Availability", "DungeonLoot");
            npcsDropSpellPages = settings.GetValue<bool>("Availability", "NPCLoot");
            spellPageFrequency = settings.GetValue<float>("Availability", "FrequencyMultiplier");

            unleveledLoot = settings.GetValue<bool>("Availability", "UnleveledLoot");

            InitMod();
        }

        void InitMod()
        {
            Debug.Log("Begin mod init: Lootable Spells");

            //Order matters here
            SaveLoadManager.OnLoad += RefreshSpellList_OnLoad;
            SaveLoadManager.OnLoad += RestorePageState_OnLoad;

            RegisterNewItems();

            if (shopsHaveSpellPages)
                PlayerActivate.OnLootSpawned += AddSpellPages_OnLootSpawned;

            if (dungeonsHaveSpellPages)
                LootTables.OnLootSpawned += AddSpellPages_OnDungeonLootSpawned;

            if (npcsDropSpellPages)
                EnemyDeath.OnEnemyDeath += AddSpellPages_OnEnemyDeath;

            Debug.Log("Finished mod init: Lootable Spells");
            mod.IsReady = true;
        }

        #region Event listeners
        private void RefreshSpellList_OnLoad(SaveData_v1 saveData)
        {
            //TODO: This should definitely be optimised
            spellIndicesByQuality = new Dictionary<int, List<int>>();
            foreach (SpellRecord.SpellRecordData spell in GameManager.Instance.EntityEffectBroker.StandardSpells)
            {
                int goldCost = GetGoldCost(spell);
                for (int i = 1; i < QUALITY_GOLD_THRESHOLDS.Length; i++)
                {
                    if (goldCost < QUALITY_GOLD_THRESHOLDS[i])
                    {
                        if (!spellIndicesByQuality.ContainsKey(i))
                            spellIndicesByQuality.Add(i, new List<int>());

                        spellIndicesByQuality[i].Add(spell.index);
                        break;
                    }
                }
            }
        }

        private void RestorePageState_OnLoad(SaveData_v1 saveData)
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

        #region Registering
        private void RegisterNewItems()
        {
            StartGameBehaviour startGameBehaviour = GameManager.Instance.StartGameBehaviour;

            DaggerfallUnity.Instance.ItemHelper.RegisterCustomItem(SpellbookPageItem.templateIndex, ItemGroups.MiscItems, typeof(SpellbookPageItem));

            Debug.Log("Lootable Spells: Registered Custom Items");
        }
        #endregion

        #region New Behaviour
        public void AddSpellPages_OnLootSpawned(object sender, ContainerLootSpawnedEventArgs e)
        {
            DaggerfallInterior interior = GameManager.Instance.PlayerEnterExit.Interior;
            if (interior == null || e.ContainerType != LootContainerTypes.ShopShelves)
                return;

            PlayerEntity playerEntity = GameManager.Instance.PlayerEntity;
            WeaponMaterialTypes materialType = FormulaHelper.RandomMaterial(playerEntity.Level);
            int spellQuality = WeaponQualityToSpellQuality(materialType);

            int spellbookPageChance = 0;
            int maxPagesPerShelf = 0;
            switch (interior.BuildingData.BuildingType)
            {
                case DFLocation.BuildingTypes.Bookseller:
                    spellbookPageChance = 10;
                    maxPagesPerShelf = 2;
                    break;

                case DFLocation.BuildingTypes.GeneralStore:
                    spellbookPageChance = 8;
                    maxPagesPerShelf = 1;
                    //General stores have lower quality spells
                    if (D100.Roll(50) && spellQuality > 0)
                        spellQuality = Mathf.Clamp(spellQuality - 1, QUALITY_LOWEST, QUALITY_HIGHEST);
                    break;

                case DFLocation.BuildingTypes.PawnShop:
                    spellbookPageChance = 4;
                    maxPagesPerShelf = 1;
                    //Pawn shops have a chance for better quality spells
                    if (D100.Roll(50))
                        spellQuality = Mathf.Clamp(spellQuality + 1, QUALITY_LOWEST, QUALITY_HIGHEST);

                    break;

                default:
                    return;
            }

            if (D100.Roll((int)(spellbookPageChance * spellPageFrequency)))
            {
                int numSpellPages = UnityEngine.Random.Range(1, maxPagesPerShelf);

                for (int i = 0; i < numSpellPages; i++)
                {
                    SpellbookPageItem spellPage = GenerateRandomSpellbookPage(spellQuality);
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
                    spellbookPageChance = 15;
                    break;

                case (int)MobileTypes.Sorcerer:
                    spellbookPageChance = 10;
                    break;

                case (int)MobileTypes.Healer:
                    spellbookPageChance = 10; //TODO: try to spawn only restoration spells somehow
                    break;

                case (int)MobileTypes.Spellsword:
                case (int)MobileTypes.Battlemage:
                    spellbookPageChance = 5;
                    break;

                default:
                    return;
            }

            PlayerEntity playerEntity = GameManager.Instance.PlayerEntity;
            WeaponMaterialTypes materialType = FormulaHelper.RandomMaterial(playerEntity.Level);
            int spellQuality = WeaponQualityToSpellQuality(materialType);

            if (D100.Roll((int)(spellbookPageChance * spellPageFrequency)))
            {
                SpellbookPageItem spellPage = GenerateRandomSpellbookPage(spellQuality);
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

            //TODO: Perhaps hard code certain spells in certain dungeons - vampires will have sleep and paralysis, orc shamans invisibility
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
                    if (D100.Roll((int)(35 * spellPageFrequency)))
                        maxSpellPages = 2;
                    break;

                default:
                    return;
            }

            WeaponMaterialTypes materialType = FormulaHelper.RandomMaterial(playerEntity.Level);
            int spellQuality = WeaponQualityToSpellQuality(materialType);

            int numSpellPages = UnityEngine.Random.Range(0, maxSpellPages);
            for (int i = 0; i < numSpellPages; i++)
            {
                SpellbookPageItem spellPage = GenerateRandomSpellbookPage(spellQuality);
                if (spellPage == null)
                    continue;

                lootArgs.Items.AddItem(spellPage);
            }
        }
        #endregion

        #region Generating Spell Pages
        private SpellbookPageItem GenerateRandomSpellbookPage(int quality)
        {
            if (quality == QUALITY_UNLEVELED)
                quality = UnityEngine.Random.Range(QUALITY_LOWEST, QUALITY_HIGHEST);

            List<int> spellIndices = spellIndicesByQuality[quality];
            int spellIndex = spellIndices[UnityEngine.Random.Range(0, spellIndices.Count - 1)];

            SpellbookPageItem spellbookPage = new SpellbookPageItem();
            spellbookPage.SpellID = spellIndex;

            return spellbookPage;
        }

        private static int GetGoldCost(SpellRecord.SpellRecordData spell)
        {
            EffectBundleSettings effectBundle = GetEffectBundleSettings(spell.index);

            return FormulaHelper.CalculateTotalEffectCosts(
                    effectBundle.Effects,
                    effectBundle.TargetType,
                    null, //Player caster
                    effectBundle.MinimumCastingCost
                ).goldCost;
        }

        public static EffectBundleSettings GetEffectBundleSettings(int spellID)
        {
            SpellRecord.SpellRecordData spellData;
            GameManager.Instance.EntityEffectBroker.GetClassicSpellRecord(spellID, out spellData);
            if (spellData.index == -1)
            {
                Debug.LogError("Failed to locate spell " + spellID + " in standard spells list.");
                return GetEffectBundleSettings(1);
            }

            EffectBundleSettings bundle;
            if (!GameManager.Instance.EntityEffectBroker.ClassicSpellRecordDataToEffectBundleSettings(spellData, BundleTypes.Spell, out bundle))
            {
                Debug.LogError("Failed to create effect bundle for spell: " + spellData.spellName);
                return GetEffectBundleSettings(1);
            }
            return bundle;
        }

        private int WeaponQualityToSpellQuality(WeaponMaterialTypes materialType)
        {
            //Tie spell progression to the material type melee characters would be finding
            //That way if another mod edits the progression curve for materials, the spell progression
            //curve is edited to match also

            if (unleveledLoot)
                return QUALITY_UNLEVELED;

            switch (materialType)
            {
                case WeaponMaterialTypes.Iron:
                case WeaponMaterialTypes.Steel:
                    return QUALITY_LOWEST;

                case WeaponMaterialTypes.Silver:
                case WeaponMaterialTypes.Elven:
                    return QUALITY_LOW;

                case WeaponMaterialTypes.Dwarven:
                case WeaponMaterialTypes.Mithril:
                    return QUALITY_MED;

                case WeaponMaterialTypes.Adamantium:
                case WeaponMaterialTypes.Ebony:
                    return QUALITY_HIGH;

                case WeaponMaterialTypes.Orcish:
                case WeaponMaterialTypes.Daedric:
                    return QUALITY_HIGHEST;

                default:
                    return QUALITY_UNLEVELED;
            }
        }

        #endregion

    }

}
