// -----------------------------------------------------------------------
// <copyright file="PlayerHandler.cs" company="ExMod Team">
// Copyright (c) ExMod Team. All rights reserved.
// Licensed under the CC BY-SA 3.0 license.
// </copyright>
// -----------------------------------------------------------------------

namespace Exiled.CustomItems.Events
{
    using Exiled.API.Extensions;
    using Exiled.API.Features;
    using Exiled.CustomItems.API.Features;
    using Exiled.Events.EventArgs.Player;
    using InventorySystem.Items.Usables;

    /// <summary>
    /// Handles Player events for the CustomItem API.
    /// </summary>
    internal sealed class PlayerHandler
    {
        /// <summary>
        /// Registers the events.
        /// </summary>
        internal void Register()
        {
            Exiled.Events.Handlers.Player.ChangingItem += OnChangingItem;
        }

        /// <summary>
        /// Unregisters the events.
        /// </summary>
        internal void Unregister()
        {
            Exiled.Events.Handlers.Player.ChangingItem -= OnChangingItem;
        }

        private void OnChangingItem(ChangingItemEventArgs ev)
        {
            if (!ev.IsAllowed)
                return;

            if (ev.Item != null && CustomItem.TryGet(ev.Item, out CustomItem? newItem) && (newItem?.ShouldMessageOnGban ?? false))
            {
                SpectatorCustomNickname(ev.Player, $"{ev.Player.CustomName} (CustomItem: {newItem.Name})");
            }
            else
            {
                Exiled.API.Features.Items.Item? currenItem = ev.Player.CurrentItem;
                if (currenItem != null && ev.Player != null && CustomItem.TryGet(currenItem, out _))
                    SpectatorCustomNickname(ev.Player, ev.Player.HasCustomName ? ev.Player.CustomName : string.Empty);
            }
        }

        private void SpectatorCustomNickname(Player player, string itemName)
        {
            foreach (Player spectator in Player.List)
                spectator.SendFakeSyncVar(player.NetworkIdentity, typeof(NicknameSync), nameof(NicknameSync.Network_displayName), itemName);
        }
    }
}