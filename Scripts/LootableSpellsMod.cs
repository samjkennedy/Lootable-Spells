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

        public static string SCROLL_EFFECT_KEY = "LootableSpells_ScrollEffect";

        const ushort identifiedMask = 0x20;

        #region Settings
        private bool shopsHaveSpellScrolls;
        private bool npcsDropSpellScrolls;
        private bool dungeonsHaveSpellScrolls;
        private float spellScrollFrequency;
        private bool unleveledLoot;
        private bool autoidentify;
        #endregion

        #region Spell Quality
        private Dictionary<int, List<int>> spellIndicesByQuality = new Dictionary<int, List<int>>();

        //in lieu of a proper enum
        private const int QUALITY_UNLEVELED = 0;
        private const int QUALITY_LOWEST = 1;
        private const int QUALITY_LOW = 2;
        private const int QUALITY_MED = 3;
        private const int QUALITY_HIGH = 4;
        private const int QUALITY_HIGHEST = 5;

        //TODO: These should be dynamically calculated in case a mod updates the spell costs (Kab's unleveled spells)
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
            shopsHaveSpellScrolls = settings.GetValue<bool>("Availability", "Shops");
            dungeonsHaveSpellScrolls = settings.GetValue<bool>("Availability", "DungeonLoot");
            npcsDropSpellScrolls = settings.GetValue<bool>("Availability", "FoeLoot");
            spellScrollFrequency = settings.GetValue<float>("Availability", "FrequencyMultiplier");
            unleveledLoot = settings.GetValue<bool>("Availability", "UnleveledLoot");

            autoidentify = settings.GetValue<bool>("Gameplay", "AutoIdentifyScrolls");

            InitMod();
        }

        void InitMod()
        {
            Debug.Log("Begin mod init: Lootable Spells");

            SaveLoadManager.OnLoad += RefreshSpellList_OnLoad;
            StartGameBehaviour.OnStartGame += RefreshSpellList_OnNewGame;

            SaveLoadManager.OnLoad += RestoreScrollState_OnLoad;

            RegisterNewItems();

            if (shopsHaveSpellScrolls)
                PlayerActivate.OnLootSpawned += AddSpellScrolls_OnLootSpawned;

            if (dungeonsHaveSpellScrolls)
                LootTables.OnLootSpawned += AddSpellScrolls_OnDungeonLootSpawned;

            if (npcsDropSpellScrolls)
                EnemyDeath.OnEnemyDeath += AddSpellScrolls_OnEnemyDeath;

            Debug.Log("Finished mod init: Lootable Spells");
            mod.IsReady = true;
        }

        #region Event listeners
        private void RefreshSpellList_OnLoad(SaveData_v1 saveData)
        {
            RefreshSpellList();
        }

        private void RefreshSpellList_OnNewGame(object sender, EventArgs e)
        {
            RefreshSpellList();
        }

        private void RefreshSpellList()
        {
            spellIndicesByQuality.Clear();

            //TODO: This should definitely be optimised
            foreach (SpellRecord.SpellRecordData spell in GameManager.Instance.EntityEffectBroker.StandardSpells)
            {
                if (spell.spellName.StartsWith("!"))
                    continue;

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

        private void RestoreScrollState_OnLoad(SaveData_v1 saveData)
        {
            List<DaggerfallUnityItem> spellScrolls = GameManager.Instance.PlayerEntity.Items.SearchItems(ItemGroups.MagicItems, SpellScrollItem.templateIndex);
            foreach (DaggerfallUnityItem item in spellScrolls)
            {
                if (item is SpellScrollItem spellScroll)
                {
                    spellScroll.SpellID = spellScroll.message;
                }
            }
        }
        #endregion

        #region Registering
        private void RegisterNewItems()
        {
            DaggerfallUnity.Instance.ItemHelper.RegisterCustomItem(SpellScrollItem.templateIndex, ItemGroups.MiscItems, typeof(SpellScrollItem));

            Debug.Log("Lootable Spells: Registered Custom Items");
        }
        #endregion

        #region New Behaviour
        public void AddSpellScrolls_OnLootSpawned(object sender, ContainerLootSpawnedEventArgs e)
        {
            DaggerfallInterior interior = GameManager.Instance.PlayerEnterExit.Interior;
            if (interior == null || e.ContainerType != LootContainerTypes.ShopShelves)
                return;

            PlayerEntity playerEntity = GameManager.Instance.PlayerEntity;
            WeaponMaterialTypes materialType = FormulaHelper.RandomMaterial(playerEntity.Level);
            int spellQuality = WeaponQualityToSpellQuality(materialType);

            int spellScrollChance = 0;
            int maxScrollsPerShelf = 0;
            switch (interior.BuildingData.BuildingType)
            {
                case DFLocation.BuildingTypes.Bookseller:
                    spellScrollChance = 10;
                    maxScrollsPerShelf = 2;
                    break;

                case DFLocation.BuildingTypes.GeneralStore:
                    spellScrollChance = 8;
                    maxScrollsPerShelf = 1;
                    //General stores have a chance for lower quality spells
                    if (D100.Roll(50))
                        spellQuality = Mathf.Clamp(spellQuality - 1, QUALITY_LOWEST, QUALITY_HIGHEST);
                    break;

                case DFLocation.BuildingTypes.PawnShop:
                    spellScrollChance = 4;
                    maxScrollsPerShelf = 1;
                    //Pawn shops have a chance for better quality spells
                    if (D100.Roll(50))
                        spellQuality = Mathf.Clamp(spellQuality + 1, QUALITY_LOWEST, QUALITY_HIGHEST);

                    break;

                default:
                    return;
            }

            if (D100.Roll((int)(spellScrollChance * spellScrollFrequency)))
            {
                int numSpellScrolls = UnityEngine.Random.Range(1, maxScrollsPerShelf);

                for (int i = 0; i < numSpellScrolls; i++)
                {
                    SpellScrollItem spellScroll = GenerateRandomSpellScroll(spellQuality);
                    if (spellScroll == null)
                        continue;

                    spellScroll.value *= 2;
                    e.Loot.AddItem(spellScroll);
                }
            }
        }

        public void AddSpellScrolls_OnEnemyDeath(object sender, EventArgs e)
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

            int spellScrollChance = 0;
            int enemyID = enemyEntity.MobileEnemy.ID;
            switch (enemyID)
            {
                case (int)MobileTypes.Mage:
                    spellScrollChance = 15;
                    break;

                case (int)MobileTypes.Sorcerer:
                    spellScrollChance = 10;
                    break;

                case (int)MobileTypes.Healer:
                    spellScrollChance = 10; //TODO: try to spawn only restoration spells somehow
                    break;

                case (int)MobileTypes.Spellsword:
                case (int)MobileTypes.Battlemage:
                    spellScrollChance = 5;
                    break;

                default:
                    return;
            }

            PlayerEntity playerEntity = GameManager.Instance.PlayerEntity;
            WeaponMaterialTypes materialType = FormulaHelper.RandomMaterial(playerEntity.Level);
            int spellQuality = WeaponQualityToSpellQuality(materialType);

            if (D100.Roll((int)(spellScrollChance * spellScrollFrequency)))
            {
                SpellScrollItem spellScroll = GenerateRandomSpellScroll(spellQuality);
                if (spellScroll == null)
                    return;

                entityBehaviour.CorpseLootContainer.Items.AddItem(spellScroll);
            }
        }

        public void AddSpellScrolls_OnDungeonLootSpawned(object sender, TabledLootSpawnedEventArgs lootArgs)
        {
            PlayerEntity playerEntity = GameManager.Instance.PlayerEntity;
            ItemHelper itemHelper = DaggerfallUnity.Instance.ItemHelper;

            int maxSpellScrolls = 0;

            //TODO: Perhaps hard code certain spells in certain dungeons - vampires will have sleep and paralysis, orc shamans invisibility
            switch (lootArgs.LocationIndex)
            {
                case (int)DFRegion.DungeonTypes.OrcStronghold:
                    if (D100.Roll((int)(5 * spellScrollFrequency)))
                        maxSpellScrolls = 1;
                    break;

                case (int)DFRegion.DungeonTypes.HumanStronghold:
                    if (D100.Roll((int)(5 * spellScrollFrequency)))
                        maxSpellScrolls = 1;
                    break;

                case (int)DFRegion.DungeonTypes.DesecratedTemple:
                    if (D100.Roll((int)(15 * spellScrollFrequency)))
                        maxSpellScrolls = 1;
                    break;

                case (int)DFRegion.DungeonTypes.Coven:
                    //Do witches even use spells?
                    if (D100.Roll((int)(25 * spellScrollFrequency)))
                        maxSpellScrolls = 2;
                    break;

                case (int)DFRegion.DungeonTypes.VampireHaunt:
                    if (D100.Roll((int)(20 * spellScrollFrequency)))
                        maxSpellScrolls = 1;
                    break;

                case (int)DFRegion.DungeonTypes.Laboratory:
                    if (D100.Roll((int)(35 * spellScrollFrequency)))
                        maxSpellScrolls = 2;
                    break;

                default:
                    return;
            }

            WeaponMaterialTypes materialType = FormulaHelper.RandomMaterial(playerEntity.Level);
            int spellQuality = WeaponQualityToSpellQuality(materialType);

            int numSpellScrolls = UnityEngine.Random.Range(0, maxSpellScrolls);
            for (int i = 0; i < numSpellScrolls; i++)
            {
                SpellScrollItem spellScroll = GenerateRandomSpellScroll(spellQuality);
                if (spellScroll == null)
                    continue;

                lootArgs.Items.AddItem(spellScroll);
            }
        }
        #endregion

        #region Generating Spell Scrolls
        private SpellScrollItem GenerateRandomSpellScroll(int quality)
        {
            if (quality == QUALITY_UNLEVELED)
                quality = UnityEngine.Random.Range(QUALITY_LOWEST, QUALITY_HIGHEST);

            List<int> spellIndices = spellIndicesByQuality[quality];
            int spellIndex = spellIndices[UnityEngine.Random.Range(0, spellIndices.Count - 1)];

            SpellScrollItem spellScroll = new SpellScrollItem();
            spellScroll.SpellID = spellIndex;

            if (autoidentify)
            {
                spellScroll.flags |= identifiedMask;
            }

            return spellScroll;
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
