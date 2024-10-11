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

        public static string SCROLL_EFFECT_KEY = "!LootableSpells_ScrollEffect";

        const ushort identifiedMask = 0x20;

        #region Settings
        private bool shopsHaveSpellScrolls;
        private bool npcsDropSpellScrolls;
        private bool dungeonsHaveSpellScrolls;
        private float spellScrollFrequency;
        private bool unleveledLoot;
        private bool autoidentify;
        #endregion

        #region Constants

        //in lieu of a proper enum
        private const int QUALITY_UNLEVELED = -1;
        private const int QUALITY_LOWEST = 0;
        private const int QUALITY_LOW = 1;
        private const int QUALITY_MED = 2;
        private const int QUALITY_HIGH = 3;
        private const int QUALITY_HIGHEST = 4;

        //Dungeon loot chances
        private const int CHANCE_ORC_STRONGHOLD = 5;
        private const int CHANCE_HUMAN_STRONGHOLD = 5;
        private const int CHANCE_DESECRATED_TEMPLE = 15;
        private const int CHANCE_WITCH_COVEN = 25;
        private const int CHANCE_VAMPIRE_HAUNT = 20;
        private const int CHANCE_LABORATORY = 35;

        //NPC drop chances
        private const int CHANCE_MAGE = 15;
        private const int CHANCE_SORCERER = 10;
        private const int CHANCE_HEALER = 10;
        private const int CHANCE_SPELLSWORD = 5;
        private const int CHANCE_BATTLEMAGE = 5;

        //Shop stock chances
        private const int CHANCE_BOOKSELLER = 10;
        private const int CHANCE_GENERAL_STORE = 8;
        private const int CHANCE_PAWN_SHOP = 4;

        #endregion

        #region Spell Quality
        private Dictionary<int, List<int>> spellIndicesByQuality = new Dictionary<int, List<int>>();

        //TODO: These should be dynamically calculated in case a mod updates the spell costs (Kab's unleveled spells)
        private int[] QUALITY_GOLD_THRESHOLDS = {
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
            RegisterNewEffects();

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
                for (int i = 0; i < QUALITY_GOLD_THRESHOLDS.Length; i++)
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
            List<DaggerfallUnityItem> spellScrolls = GameManager.Instance.PlayerEntity.Items.SearchItems(ItemGroups.UselessItems2, SpellScrollItem.templateIndex);
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
            DaggerfallUnity.Instance.ItemHelper.RegisterCustomItem(SpellScrollItem.templateIndex, ItemGroups.UselessItems2, typeof(SpellScrollItem));

            Debug.Log("Lootable Spells: Registered Custom Items");
        }
        private void RegisterNewEffects()
        {
            GameManager.Instance.EntityEffectBroker.RegisterEffectTemplate(new ScrollEffect(), true);

            Debug.Log("Lootable Spells: Registered Custom Effects");
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
                    spellScrollChance = CHANCE_BOOKSELLER;
                    maxScrollsPerShelf = 2;
                    break;

                case DFLocation.BuildingTypes.GeneralStore:
                    spellScrollChance = CHANCE_GENERAL_STORE;
                    maxScrollsPerShelf = 1;
                    //General stores have a chance for lower quality spells
                    if (D100.Roll(50))
                        spellQuality = Mathf.Clamp(spellQuality - 1, QUALITY_LOWEST, QUALITY_HIGHEST);
                    break;

                case DFLocation.BuildingTypes.PawnShop:
                    spellScrollChance = CHANCE_PAWN_SHOP;
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
                int numSpellScrolls = UnityEngine.Random.Range(1, maxScrollsPerShelf + 1);

                for (int i = 0; i < numSpellScrolls; i++)
                {
                    SpellScrollItem spellScroll = GenerateRandomSpellScroll(spellQuality);
                    if (spellScroll == null)
                        continue;

                    spellScroll.value *= 2;
                    spellScroll.flags |= identifiedMask;
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
            switch (enemyEntity.MobileEnemy.ID)
            {
                case (int)MobileTypes.Mage:
                    spellScrollChance = CHANCE_MAGE;
                    break;

                case (int)MobileTypes.Sorcerer:
                    spellScrollChance = CHANCE_SORCERER;
                    break;

                case (int)MobileTypes.Healer:
                    //TODO: try to spawn only restoration spells somehow
                    spellScrollChance = CHANCE_HEALER;
                    break;

                case (int)MobileTypes.Spellsword:
                    spellScrollChance = CHANCE_SPELLSWORD;
                    break;

                case (int)MobileTypes.Battlemage:
                    spellScrollChance = CHANCE_BATTLEMAGE;
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
            int maxSpellScrolls = 0;

            //TODO: Perhaps hard code certain spells in certain dungeons - vampires will have sleep and paralysis, orc shamans invisibility
            switch (lootArgs.LocationIndex)
            {
                case (int)DFRegion.DungeonTypes.OrcStronghold:
                    if (D100.Roll((int)(CHANCE_ORC_STRONGHOLD * spellScrollFrequency)))
                        maxSpellScrolls = 1;
                    break;

                case (int)DFRegion.DungeonTypes.HumanStronghold:
                    if (D100.Roll((int)(CHANCE_HUMAN_STRONGHOLD * spellScrollFrequency)))
                        maxSpellScrolls = 1;
                    break;

                case (int)DFRegion.DungeonTypes.DesecratedTemple:
                    if (D100.Roll((int)(CHANCE_DESECRATED_TEMPLE * spellScrollFrequency)))
                        maxSpellScrolls = 1;
                    break;

                case (int)DFRegion.DungeonTypes.Coven:
                    if (D100.Roll((int)(CHANCE_WITCH_COVEN * spellScrollFrequency)))
                        maxSpellScrolls = 2;
                    break;

                case (int)DFRegion.DungeonTypes.VampireHaunt:
                    if (D100.Roll((int)(CHANCE_VAMPIRE_HAUNT * spellScrollFrequency)))
                        maxSpellScrolls = 1;
                    break;

                case (int)DFRegion.DungeonTypes.Laboratory:
                    if (D100.Roll((int)(CHANCE_LABORATORY * spellScrollFrequency)))
                        maxSpellScrolls = 2;
                    break;

                default:
                    return;
            }

            if (maxSpellScrolls == 0)
                return;

            PlayerEntity playerEntity = GameManager.Instance.PlayerEntity;

            WeaponMaterialTypes materialType = FormulaHelper.RandomMaterial(playerEntity.Level);
            int spellQuality = WeaponQualityToSpellQuality(materialType);

            int numSpellScrolls = UnityEngine.Random.Range(1, maxSpellScrolls + 1);
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
                quality = UnityEngine.Random.Range(0, spellIndicesByQuality.Count);

            if (spellIndicesByQuality.Count <= quality)
                return null;

            List<int> spellIndices = spellIndicesByQuality[quality];
            int spellIndex = spellIndices[UnityEngine.Random.Range(0, spellIndices.Count)];

            if (spellIndex == -1)
                return null;

            SpellScrollItem spellScroll = new SpellScrollItem();
            spellScroll.SpellID = spellIndex;

            if (autoidentify)
                spellScroll.flags |= identifiedMask;

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
            if (!GameManager.Instance.EntityEffectBroker.GetClassicSpellRecord(spellID, out spellData))
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
            //Ties spell progression to the material type melee characters would be finding
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
