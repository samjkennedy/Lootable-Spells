using DaggerfallConnect.Save;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using System;
using UnityEngine;

namespace LootableSpells
{
    public class ScrollEffect : BaseEntityEffect
    {
        public override void SetProperties()
        {
            properties.Key = LootableSpellsMod.SCROLL_EFFECT_KEY;
            properties.EnchantmentPayloadFlags = EnchantmentPayloadFlags.Used;
        }

        public override PayloadCallbackResults? EnchantmentPayloadCallback(
            EnchantmentPayloadFlags context,
            EnchantmentParam? param = null,
            DaggerfallEntityBehaviour sourceEntity = null,
            DaggerfallEntityBehaviour targetEntity = null,
            DaggerfallUnityItem sourceItem = null,
            int sourceDamage = 0
        )
        {
            //The actual casting logic is handled by CastWhenUsed.cs, 
            //this just needs to do the extra work of playing the sounds and deleting the scroll once used
            base.EnchantmentPayloadCallback(context, param, sourceEntity, targetEntity, sourceItem);

            if (context != EnchantmentPayloadFlags.Used || sourceEntity == null || param == null)
                return null;

            DaggerfallUI.Instance.DaggerfallAudioSource.PlayClipAtPoint
            (
                SoundClips.PageTurn,
                GameManager.Instance.PlayerObject.transform.position,
                1f
            );

            EntityEffectBroker entityEffectBroker = GameManager.Instance.EntityEffectBroker;
            if (int.TryParse(param.Value.CustomParam, out int spellID)
                && entityEffectBroker.GetClassicSpellRecord(spellID, out SpellRecord.SpellRecordData spell)
                && entityEffectBroker.ClassicSpellRecordDataToEffectBundleSettings(spell, BundleTypes.Spell, out EffectBundleSettings bundleSettings))
            {
                //Annoyingly CastWhenUsed doesn't play a CastSpell sound if the effect is a Self effect... so let's do that for it
                if (bundleSettings.TargetType == TargetTypes.CasterOnly)
                {
                    DaggerfallUI.Instance.DaggerfallAudioSource.PlayClipAtPoint
                    (
                        SoundClips.CastSpell1,
                        GameManager.Instance.PlayerObject.transform.position,
                        1f
                    );
                }
            }

            //Alas, CastWhenUsed.cs will always break the scroll anyway, so I can't display a custom HUD text :sob:
            return new PayloadCallbackResults()
            {
                removeItem = true,
            };
        }
    }
}