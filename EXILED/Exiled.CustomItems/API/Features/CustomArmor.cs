// -----------------------------------------------------------------------
// <copyright file="CustomArmor.cs" company="ExMod Team">
// Copyright (c) ExMod Team. All rights reserved.
// Licensed under the CC BY-SA 3.0 license.
// </copyright>
// -----------------------------------------------------------------------

namespace Exiled.CustomItems.API.Features
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;

    using Exiled.API.Extensions;
    using Exiled.API.Features;
    using Exiled.API.Features.Items;
    using Exiled.API.Structs;

    using InventorySystem.Items.Armor;
    using MEC;

    /// <summary>
    /// The Custom Armor base class.
    /// </summary>
    public abstract class CustomArmor : CustomItem
    {
        /// <summary>
        /// Gets or sets the <see cref="ItemType"/> to use for this armor.
        /// </summary>
        public override ItemType Type
        {
            get => base.Type;
            set
            {
                if (value != ItemType.None && !value.IsArmor())
                    throw new ArgumentOutOfRangeException("Type", value, "Invalid armor type.");

                base.Type = value;
            }
        }

        /// <summary>
        /// Gets or sets how much faster stamina will drain when wearing this armor.
        /// </summary>
        [Description("The value must be above 1 and below 2")]
        public virtual float StaminaUseMultiplier { get; set; } = 1.15f;

        /// <summary>
        /// Gets or sets how strong the helmet on the armor is.
        /// </summary>
        [Description("The value must be above 0 and below 100")]
        public virtual int HelmetEfficacy { get; set; } = 80;

        /// <summary>
        /// Gets or sets how strong the vest on the armor is.
        /// </summary>
        [Description("The value must be above 0 and below 100")]
        public virtual int VestEfficacy { get; set; } = 80;

        /// <summary>
        /// Gets or sets the Ammunition limit the player have.
        /// </summary>
        public virtual List<ArmorAmmoLimit> AmmoLimits { get; set; } = new();

        /// <summary>
        /// Gets or sets the Item Category limit the player have.
        /// </summary>
        public virtual List<BodyArmor.ArmorCategoryLimitModifier> CategoryLimits { get; set; } = new();

        /// <inheritdoc />
        public override void Give(Player player, bool displayMessage = true)
        {
            Armor armor = (Armor)Item.Create(Type);

            armor.Weight = Weight;
            armor.StaminaUseMultiplier = StaminaUseMultiplier;

            armor.VestEfficacy = VestEfficacy;
            armor.HelmetEfficacy = HelmetEfficacy;

            if (AmmoLimits.Count != 0)
                armor.AmmoLimits = AmmoLimits;

            if (AmmoLimits.Count != 0)
                armor.CategoryLimits = CategoryLimits;

            player.AddItem(armor);

            SerialLookupTable[armor.Serial] = this;

            OnAcquired(player, armor, displayMessage);

            if (displayMessage)
                ShowPickedUpMessage(player);
        }
    }
}