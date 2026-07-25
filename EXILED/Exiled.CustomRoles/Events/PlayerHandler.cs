// -----------------------------------------------------------------------
// <copyright file="PlayerHandler.cs" company="ExMod Team">
// Copyright (c) ExMod Team. All rights reserved.
// Licensed under the CC BY-SA 3.0 license.
// </copyright>
// -----------------------------------------------------------------------

namespace Exiled.CustomRoles.Events
{
    using System.Collections.Generic;

    using Exiled.API.Enums;
    using Exiled.CustomRoles.API;
    using Exiled.CustomRoles.API.Features;
    using Exiled.Events.EventArgs.Player;

    using UnityEngine;

    /// <summary>
    /// Handles general events for players.
    /// </summary>
    internal sealed class PlayerHandler
    {
        private static readonly HashSet<SpawnReason> ValidSpawnReasons = new()
        {
            SpawnReason.RoundStart,
            SpawnReason.Respawn,
            SpawnReason.LateJoin,
            SpawnReason.Revived,
            SpawnReason.Escaped,
            SpawnReason.ItemUsage,
        };

        private readonly CustomRoles plugin;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlayerHandler"/> class.
        /// </summary>
        /// <param name="plugin">The <see cref="CustomRoles"/> plugin instance.</param>
        internal PlayerHandler(CustomRoles plugin)
        {
            this.plugin = plugin;
        }

        /// <summary>
        /// Registers the events.
        /// </summary>
        internal void Register()
        {
            Exiled.Events.Handlers.Player.Spawned += OnSpawned;
            Exiled.Events.Handlers.Player.SpawningRagdoll += OnSpawningRagdoll;

            Exiled.Events.Handlers.Server.WaitingForPlayers += OnWaitingForPlayers;
        }

        /// <summary>
        /// Unregisters the events.
        /// </summary>
        internal void Unregister()
        {
            Exiled.Events.Handlers.Player.Spawned -= OnSpawned;
            Exiled.Events.Handlers.Player.SpawningRagdoll -= OnSpawningRagdoll;

            Exiled.Events.Handlers.Server.WaitingForPlayers -= OnWaitingForPlayers;
        }

        /// <inheritdoc cref="Exiled.Events.Handlers.Server.WaitingForPlayers"/>
        private void OnWaitingForPlayers()
        {
            foreach (CustomRole role in CustomRole.Registered)
                role.SpawnedPlayers = 0;
        }

        /// <inheritdoc cref="Exiled.Events.Handlers.Player.SpawningRagdoll"/>
        private void OnSpawningRagdoll(SpawningRagdollEventArgs ev)
        {
            if (plugin.StopRagdollPlayers.Remove(ev.Player))
                ev.IsAllowed = false;
        }

        /// <inheritdoc cref="Exiled.Events.Handlers.Player.Spawning"/>
        private void OnSpawned(SpawnedEventArgs ev)
        {
            if (ev.Player == null || !ValidSpawnReasons.Contains(ev.Reason) || ev.Player.HasAnyCustomRole())
                return;

            float totalChance = 0f;
            List<CustomRole> eligibleRoles = new(8);

            foreach (CustomRole role in CustomRole.Registered)
            {
                if (!role.IgnoreSpawnSystem && role.Role == ev.Player.Role.Type && role.SpawnChance > 0 && role.ValidSpawnReasons.Contains(ev.Reason) && !role.Check(ev.Player) && (role.SpawnProperties is null || role.SpawnedPlayers < role.SpawnProperties.Limit) && (role.MinPlayers is 0 || Server.PlayerConnectedCount >= role.MinPlayers))
                {
                    eligibleRoles.Add(role);
                    totalChance += role.SpawnChance;
                }
            }

            if (eligibleRoles.Count == 0)
                return;

            float lotterySize = Mathf.Max(100f, totalChance);
            float randomRoll = (float)Loader.Loader.Random.NextDouble() * lotterySize;

            if (randomRoll >= totalChance)
                return;

            foreach (CustomRole candidateRole in eligibleRoles)
            {
                if (randomRoll >= candidateRole.SpawnChance)
                {
                    randomRoll -= candidateRole.SpawnChance;
                    continue;
                }

                candidateRole.SpawnedPlayers++;
                candidateRole.AddRole(ev.Player, ev.Reason, false, ev.SpawnFlags);
                break;
            }
        }
    }
}