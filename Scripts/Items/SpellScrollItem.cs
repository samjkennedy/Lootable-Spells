using DaggerfallConnect.Save;
using DaggerfallConnect.FallExe;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Utility;
using System;
using System.Linq;
using UnityEngine;

namespace LootableSpells
{
    public class SpellScrollItem : DaggerfallUnityItem
    {
        public const int templateIndex = 6000;

        private EffectBundleSettings effectBundle;
        public EffectBundleSettings EffectBundle { get { return effectBundle; } }

        private const float valueMult = 1.5f;

        public SpellScrollItem() : base(ItemGroups.MagicItems, templateIndex)
        {
        }

        //Bit of a hack to use the message field to serialize the effect bundle ID
        public int SpellID
        {
            get { return message; }
            set
            {
                message = value;
                effectBundle = LootableSpellsMod.GetEffectBundleSettings(value);
                shortName = "Spell Scroll: " + effectBundle.Name;

                this.value = (int)(FormulaHelper.CalculateTotalEffectCosts(
                    effectBundle.Effects,
                    effectBundle.TargetType,
                    null, //Player caster
                    effectBundle.MinimumCastingCost
                ).goldCost * valueMult);

                //Custom enchantment allows it to call ScrollEffect.cs when used
                this.customMagic = new CustomEnchantment[] {
                    new CustomEnchantment() {
                        EffectKey = LootableSpellsMod.SCROLL_EFFECT_KEY,
                        CustomParam = value.ToString(), //Send ScrollEffect.cs the spell ID as a custom param
                    }
                };
                //Daggerfall enchantment performs the spell effect
                this.legacyMagic = new DaggerfallEnchantment[] {
                    new DaggerfallEnchantment() {
                        type = EnchantmentTypes.CastWhenUsed,
                        param = (short) value
                    }
                };
            }
        }

        public override bool UseItem(ItemCollection collection)
        {
            if (collection == null)
                return true;

            if (!IsIdentified)
            {
                DaggerfallUI.MessageBox("You must identify the spell before it can be copied into your spellbook.");
                return true;
            }

            if (collection.GetItem(ItemGroups.MiscItems, (int)MiscItems.Spellbook) == null)
            {
                DaggerfallUI.MessageBox(TextManager.Instance.GetLocalizedText("noSpellbook"));
                return true;
            }

            PlayerEntity playerEntity = GameManager.Instance.PlayerEntity;
            if (playerEntity.GetSpells().Any(spell => spell.Name == EffectBundle.Name))
            {
                DaggerfallUI.MessageBox("Your spellbook already contains the spell '" + EffectBundle.Name + "'.");
                return true;
            }

            //Must play sound before the message box opens for some reason
            if (DaggerfallUI.Instance.DaggerfallAudioSource)
                DaggerfallUI.Instance.DaggerfallAudioSource.PlayClipAtPoint
                (
                    SoundClips.ParchmentScratching,
                    GameManager.Instance.PlayerObject.transform.position,
                    1f
                );

            //I'd rather prompt the player if they wish to copy the spell into their spellbook, 
            //  but removing the page inside the lambda wasn't working right
            DaggerfallMessageBox messageText = new DaggerfallMessageBox
            (
                DaggerfallUI.UIManager,
                DaggerfallMessageBox.CommonMessageBoxButtons.Nothing,
                "You copy the spell '" + EffectBundle.Name + "' into your spellbook.",
                DaggerfallUI.UIManager.TopWindow
            );
            messageText.ClickAnywhereToClose = true;
            messageText.Show();

            collection.RemoveOne(this);
            playerEntity.AddSpell(EffectBundle);

            return true;
        }

        public override ItemData_v1 GetSaveData()
        {
            ItemData_v1 data = base.GetSaveData();
            data.className = typeof(SpellScrollItem).ToString();
            return data;
        }
    }
}
