// -----------------------------------------------------------------------
// <copyright file="CustomItem.cs" company="ExMod Team">
// Copyright (c) ExMod Team. All rights reserved.
// Licensed under the CC BY-SA 3.0 license.
// </copyright>
// -----------------------------------------------------------------------

namespace Exiled.CustomItems.API.Features
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    using Exiled.API.Enums;
    using Exiled.API.Extensions;
    using Exiled.API.Features;
    using Exiled.API.Features.Attributes;
    using Exiled.API.Features.Lockers;
    using Exiled.API.Features.Pickups;
    using Exiled.API.Features.Pools;
    using Exiled.API.Features.Spawn;
    using Exiled.API.Interfaces;
    using Exiled.CustomItems.API.EventArgs;
    using Exiled.Events.EventArgs.Map;
    using Exiled.Events.EventArgs.Player;
    using Exiled.Events.EventArgs.Scp914;
    using Exiled.Loader;
    using InventorySystem.Items.Pickups;
    using MEC;
    using PlayerRoles;
    using UnityEngine;
    using YamlDotNet.Serialization;

    using static CustomItems;

    using Firearm = Exiled.API.Features.Items.Firearm;
    using Item = Exiled.API.Features.Items.Item;
    using Player = Exiled.API.Features.Player;
    using UpgradingPickupEventArgs = Exiled.Events.EventArgs.Scp914.UpgradingPickupEventArgs;

    /// <summary>
    /// The Custom Item base class.
    /// </summary>
    public abstract class CustomItem
    {
        /// <summary>
        /// Dictionary to map serials to <see cref="CustomItem"/> instances.
        /// </summary>
        internal static readonly Dictionary<ushort, CustomItem?> SerialLookupTable = new();

        private static readonly Dictionary<string, CustomItem?> StringLookupTable = new();
        private static readonly Dictionary<uint, CustomItem?> IdLookupTable = new();

        private ItemType type = ItemType.None;

        /// <summary>
        /// Gets the list of current Item Managers.
        /// </summary>
        public static HashSet<CustomItem> Registered { get; } = new();

        /// <summary>
        /// Gets or sets the custom ItemID of the item.
        /// </summary>
        public abstract uint Id { get; set; }

        /// <summary>
        /// Gets or sets the name of the item.
        /// </summary>
        public abstract string Name { get; set; }

        /// <summary>
        /// Gets or sets the description of the item.
        /// </summary>
        public abstract string Description { get; set; }

        /// <summary>
        /// Gets or sets the weight of the item.
        /// </summary>
        public abstract float Weight { get; set; }

        /// <summary>
        /// Gets or sets the list of spawn locations and chances for each one.
        /// </summary>
        public abstract SpawnProperties? SpawnProperties { get; set; }

        /// <summary>
        /// Gets or sets the scale of the item.
        /// </summary>
        public virtual Vector3 Scale { get; set; } = Vector3.one;

        /// <summary>
        /// Gets or sets the ItemType to use for this item.
        /// </summary>
        public virtual ItemType Type
        {
            get => type;
            set
            {
                if (!Enum.IsDefined(typeof(ItemType), value))
                    throw new ArgumentOutOfRangeException("Type", value, "Invalid Item type.");

                type = value;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this item causes things to happen that may be considered hacks, and thus be shown to global moderators as being present in a player's inventory when they gban them.
        /// </summary>
        [YamlIgnore]
        public virtual bool ShouldMessageOnGban { get; } = false;

        /// <summary>
        /// Gets a <see cref="CustomItem"/> with a specific ID.
        /// </summary>
        /// <param name="id">The <see cref="CustomItem"/> ID.</param>
        /// <returns>The <see cref="CustomItem"/> matching the search, <see langword="null"/> if not registered.</returns>
        public static CustomItem? Get(uint id)
        {
            if (!IdLookupTable.ContainsKey(id))
                IdLookupTable.Add(id, Registered.FirstOrDefault(i => i.Id == id));
            return IdLookupTable[id];
        }

        /// <summary>
        /// Gets a <see cref="CustomItem"/> with a specific name.
        /// </summary>
        /// <param name="name">The <see cref="CustomItem"/> name.</param>
        /// <returns>The <see cref="CustomItem"/> matching the search, <see langword="null"/> if not registered.</returns>
        public static CustomItem? Get(string name)
        {
            if (!StringLookupTable.ContainsKey(name))
                StringLookupTable.Add(name, Registered.FirstOrDefault(i => i.Name == name));
            return StringLookupTable[name];
        }

        /// <summary>
        /// Retrieves a collection of <see cref="CustomItem"/> instances that match a specified type.
        /// </summary>
        /// <param name="t">The <see cref="System.Type"/> type.</param>
        /// <returns>An <see cref="IEnumerable{CustomItem}"/> containing all registered <see cref="CustomItem"/> of the specified type.</returns>
        public static IEnumerable<CustomItem> Get(Type t) => Registered.Where(i => i.GetType() == t);

        /// <summary>
        /// Tries to get a <see cref="CustomItem"/> with a specific ID.
        /// </summary>
        /// <param name="id">The <see cref="CustomItem"/> ID to look for.</param>
        /// <param name="customItem">The found <see cref="CustomItem"/>, <see langword="null"/> if not registered.</param>
        /// <returns>Returns a value indicating whether the <see cref="CustomItem"/> was found.</returns>
        public static bool TryGet(uint id, out CustomItem? customItem)
        {
            customItem = Get(id);

            return customItem is not null;
        }

        /// <summary>
        /// Tries to get a <see cref="CustomItem"/> with a specific name.
        /// </summary>
        /// <param name="name">The <see cref="CustomItem"/> name to look for.</param>
        /// <param name="customItem">The found <see cref="CustomItem"/>, <see langword="null"/> if not registered.</param>
        /// <returns>Returns a value indicating whether the <see cref="CustomItem"/> was found.</returns>
        public static bool TryGet(string name, out CustomItem? customItem)
        {
            customItem = null;
            if (string.IsNullOrEmpty(name))
                return false;

            customItem = uint.TryParse(name, out uint id) ? Get(id) : Get(name);

            return customItem is not null;
        }

        /// <summary>
        /// Attempts to retrieve a collection of <see cref="CustomItem"/> instances of a specified type.
        /// </summary>
        /// <param name="t">The <see cref="System.Type"/> of the item to look for.</param>
        /// <param name="customItems">An output parameter that will contain the found <see cref="CustomItem"/> instances, or an empty collection if none are registered.</param>
        /// <returns>A boolean value indicating whether any <see cref="CustomItem"/> instances of the specified type were found.</returns>
        public static bool TryGet(Type t, out IEnumerable<CustomItem> customItems)
        {
            customItems = Get(t);

            return customItems.Any();
        }

        /// <summary>
        /// Tries to get the player's current <see cref="CustomItem"/> in their hand.
        /// </summary>
        /// <param name="player">The <see cref="Player"/> to check.</param>
        /// <param name="customItem">The <see cref="CustomItem"/> in their hand.</param>
        /// <returns>Returns a value indicating whether the <see cref="Player"/> has a <see cref="CustomItem"/> in their hand or not.</returns>
        public static bool TryGet(Player player, out CustomItem? customItem)
        {
            customItem = null;
            if (player is null)
                return false;

            customItem = Registered?.FirstOrDefault(tempCustomItem => tempCustomItem.Check(player.CurrentItem));

            return customItem is not null;
        }

        /// <summary>
        /// Tries to get the player's <see cref="IEnumerable{T}"/> of <see cref="CustomItem"/>.
        /// </summary>
        /// <param name="player">The <see cref="Player"/> to check.</param>
        /// <param name="customItems">The player's <see cref="IEnumerable{T}"/> of <see cref="CustomItem"/>.</param>
        /// <returns>Returns a value indicating whether the <see cref="Player"/> has a <see cref="CustomItem"/> in their hand or not.</returns>
        public static bool TryGet(Player player, out IEnumerable<CustomItem>? customItems)
        {
            customItems = Enumerable.Empty<CustomItem>();
            if (player is null)
                return false;

            customItems = Registered.Where(tempCustomItem => player.Items.Any(tempCustomItem.Check));

            return customItems.Any();
        }

        /// <summary>
        /// Checks if this serial is a custom item.
        /// </summary>
        /// <param name="serial">The serial to check.</param>
        /// <param name="customItem">The <see cref="CustomItem"/> this item is.</param>
        /// <returns>True if the serial is a custom item.</returns>
        public static bool TryGet(ushort serial, out CustomItem? customItem) => SerialLookupTable.TryGetValue(serial, out customItem);

        /// <summary>
        /// Checks to see if this item is a custom item.
        /// </summary>
        /// <param name="item">The <see cref="Item"/> to check.</param>
        /// <param name="customItem">The <see cref="CustomItem"/> this item is.</param>
        /// <returns>True if the item is a custom item.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="item"/> is <see langword="null"/>.</exception>
        public static bool TryGet(Item item, out CustomItem? customItem) => TryGet(item.Serial, out customItem);

        /// <summary>
        /// Checks if this pickup is a custom item.
        /// </summary>
        /// <param name="pickup">The <see cref="ItemPickupBase"/> to check.</param>
        /// <param name="customItem">The <see cref="CustomItem"/> this pickup is.</param>
        /// <returns>True if the pickup is a custom item.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="pickup"/> is <see langword="null"/>.</exception>
        public static bool TryGet(Pickup pickup, out CustomItem? customItem) => TryGet(pickup.Serial, out customItem);

        /// <summary>
        /// Tries to spawn a specific <see cref="CustomItem"/> at a specific <see cref="Vector3"/> position.
        /// </summary>
        /// <param name="id">The ID of the <see cref="CustomItem"/> to spawn.</param>
        /// <param name="position">The <see cref="Vector3"/> location to spawn the item.</param>
        /// <param name="pickup">The <see cref="ItemPickupBase"/> instance of the <see cref="CustomItem"/>.</param>
        /// <returns>Returns a value indicating whether the <see cref="CustomItem"/> was spawned.</returns>
        public static bool TrySpawn(uint id, Vector3 position, out Pickup? pickup) => TrySpawn(id, position, out pickup, out _);

        /// <summary>
        /// Tries to spawn a specific <see cref="CustomItem"/> at a specific <see cref="Vector3"/> position.
        /// </summary>
        /// <param name="id">The ID of the <see cref="CustomItem"/> to spawn.</param>
        /// <param name="position">The <see cref="Vector3"/> location to spawn the item.</param>
        /// <param name="pickup">The <see cref="ItemPickupBase"/> instance of the <see cref="CustomItem"/>.</param>
        /// <param name="customItem">The <see cref="CustomItem"/>.</param>
        /// <returns>Returns a value indicating whether the <see cref="CustomItem"/> was spawned.</returns>
        public static bool TrySpawn(uint id, Vector3 position, out Pickup? pickup, out CustomItem? customItem)
        {
            pickup = default;

            if (!TryGet(id, out customItem))
                return false;

            pickup = customItem?.Spawn(position);

            return true;
        }

        /// <summary>
        /// Tries to spawn a specific <see cref="CustomItem"/> at a specific <see cref="Vector3"/> position.
        /// </summary>
        /// <param name="name">The name of the <see cref="CustomItem"/> to spawn.</param>
        /// <param name="position">The <see cref="Vector3"/> location to spawn the item.</param>
        /// <param name="pickup">The <see cref="ItemPickupBase"/> instance of the <see cref="CustomItem"/>.</param>
        /// <returns>Returns a value indicating whether the <see cref="CustomItem"/> was spawned.</returns>
        public static bool TrySpawn(string name, Vector3 position, out Pickup? pickup)
        {
            pickup = default;

            if (!TryGet(name, out CustomItem? item))
                return false;

            pickup = item?.Spawn(position, null);

            return true;
        }

        /// <summary>
        /// Gives to a specific <see cref="Player"/> a specic <see cref="CustomItem"/>.
        /// </summary>
        /// <param name="player">The <see cref="Player"/> to give the item to.</param>
        /// <param name="name">The name of the <see cref="CustomItem"/> to give.</param>
        /// <param name="displayMessage">Indicates a value whether <see cref="ShowPickedUpMessage"/> will be called when the player receives the <see cref="CustomItem"/> or not.</param>
        /// <returns>Returns a value indicating if the player was given the <see cref="CustomItem"/> or not.</returns>
        public static bool TryGive(Player player, string name, bool displayMessage = true)
        {
            if (!TryGet(name, out CustomItem? item))
                return false;

            item?.Give(player, displayMessage);

            return true;
        }

        /// <summary>
        /// Gives to a specific <see cref="Player"/> a specic <see cref="CustomItem"/>.
        /// </summary>
        /// <param name="player">The <see cref="Player"/> to give the item to.</param>
        /// <param name="id">The IDs of the <see cref="CustomItem"/> to give.</param>
        /// <param name="displayMessage">Indicates a value whether <see cref="ShowPickedUpMessage"/> will be called when the player receives the <see cref="CustomItem"/> or not.</param>
        /// <returns>Returns a value indicating if the player was given the <see cref="CustomItem"/> or not.</returns>
        public static bool TryGive(Player player, uint id, bool displayMessage = true)
        {
            if (!TryGet(id, out CustomItem? item))
                return false;

            item?.Give(player, displayMessage);

            return true;
        }

        /// <summary>
        /// Registers all the <see cref="CustomItem"/>'s present in the current assembly.
        /// </summary>
        /// <param name="skipReflection">Whether reflection is skipped (more efficient if you are not using your custom item classes as config objects).</param>
        /// <param name="overrideClass">The class to search properties for, if different from the plugin's config class.</param>
        /// <returns>A <see cref="IEnumerable{T}"/> of <see cref="CustomItem"/> which contains all registered <see cref="CustomItem"/>'s.</returns>
        public static IEnumerable<CustomItem> RegisterItems(bool skipReflection = false, object? overrideClass = null)
        {
            List<CustomItem> items = new();
            Assembly assembly = Assembly.GetCallingAssembly();
            foreach (Type type in assembly.GetTypes())
            {
                if ((type.BaseType != typeof(CustomItem) && !type.IsSubclassOf(typeof(CustomItem))) || type.GetCustomAttribute(typeof(CustomItemAttribute)) is null)
                    continue;

                foreach (Attribute attribute in type.GetCustomAttributes(typeof(CustomItemAttribute), true).Cast<Attribute>())
                {
                    CustomItem? customItem = null;
                    bool flag = false;

                    if (!skipReflection && Server.PluginAssemblies.ContainsKey(assembly))
                    {
                        IPlugin<IConfig> plugin = Server.PluginAssemblies[assembly];
                        foreach (PropertyInfo property in overrideClass?.GetType().GetProperties() ?? plugin.Config.GetType().GetProperties())
                        {
                            if (property.PropertyType != type)
                            {
                                if (property.GetValue(overrideClass ?? plugin.Config) is IEnumerable enumerable)
                                {
                                    List<CustomItem> list = ListPool<CustomItem>.Pool.Get();
                                    foreach (object item in enumerable)
                                    {
                                        if (item is CustomItem ci)
                                            list.Add(ci);
                                    }

                                    foreach (CustomItem item in list)
                                    {
                                        if (item.GetType() != type)
                                            break;

                                        if (item.Type == ItemType.None)
                                            item.Type = ((CustomItemAttribute)attribute).ItemType;

                                        if (!item.TryRegister())
                                            continue;

                                        flag = true;
                                        items.Add(item);
                                    }

                                    ListPool<CustomItem>.Pool.Return(list);
                                }

                                continue;
                            }

                            customItem = property.GetValue(overrideClass ?? plugin.Config) as CustomItem;
                        }
                    }

                    if (flag)
                        continue;

                    customItem ??= (CustomItem)Activator.CreateInstance(type);

                    if (customItem.Type == ItemType.None)
                        customItem.Type = ((CustomItemAttribute)attribute).ItemType;

                    if (customItem.TryRegister())
                        items.Add(customItem);
                }
            }

            return items;
        }

        /// <summary>
        /// Registers all the <see cref="CustomItem"/>'s present in the current assembly.
        /// </summary>
        /// <param name="targetTypes">The <see cref="IEnumerable{T}"/> of <see cref="System.Type"/> containing the target types.</param>
        /// <param name="isIgnored">A value indicating whether the target types should be ignored.</param>
        /// <param name="skipReflection">Whether reflection is skipped (more efficient if you are not using your custom item classes as config objects).</param>
        /// <param name="overrideClass">The class to search properties for, if different from the plugin's config class.</param>
        /// <returns>A <see cref="IEnumerable{T}"/> of <see cref="CustomItem"/> which contains all registered <see cref="CustomItem"/>'s.</returns>
        public static IEnumerable<CustomItem> RegisterItems(IEnumerable<Type> targetTypes, bool isIgnored = false, bool skipReflection = false, object? overrideClass = null)
        {
            List<CustomItem> items = new();
            Assembly assembly = Assembly.GetCallingAssembly();
            foreach (Type type in assembly.GetTypes())
            {
                if ((type.BaseType != typeof(CustomItem) && !type.IsSubclassOf(typeof(CustomItem))) || type.GetCustomAttribute(typeof(CustomItemAttribute)) is null ||
                    (isIgnored && targetTypes.Contains(type)) || (!isIgnored && !targetTypes.Contains(type)))
                    continue;

                foreach (Attribute attribute in type.GetCustomAttributes(typeof(CustomItemAttribute), true).Cast<Attribute>())
                {
                    CustomItem? customItem = null;

                    if (!skipReflection && Server.PluginAssemblies.ContainsKey(assembly))
                    {
                        IPlugin<IConfig> plugin = Server.PluginAssemblies[assembly];

                        foreach (PropertyInfo property in overrideClass?.GetType().GetProperties() ?? plugin.Config.GetType().GetProperties())
                        {
                            if (property.PropertyType != type)
                                continue;

                            customItem = property.GetValue(overrideClass ?? plugin.Config) as CustomItem;
                            break;
                        }
                    }

                    customItem ??= (CustomItem)Activator.CreateInstance(type);

                    if (customItem.Type == ItemType.None)
                        customItem.Type = ((CustomItemAttribute)attribute).ItemType;

                    if (customItem.TryRegister())
                        items.Add(customItem);
                }
            }

            return items;
        }

        /// <summary>
        /// Unregisters all the <see cref="CustomItem"/>'s present in the current assembly.
        /// </summary>
        /// <returns>A <see cref="IEnumerable{T}"/> of <see cref="CustomItem"/> which contains all unregistered <see cref="CustomItem"/>'s.</returns>
        public static IEnumerable<CustomItem> UnregisterItems()
        {
            List<CustomItem> unregisteredItems = new();

            foreach (CustomItem customItem in Registered.ToList())
            {
                customItem.TryUnregister();
                unregisteredItems.Add(customItem);
            }

            return unregisteredItems;
        }

        /// <summary>
        /// Unregisters all the <see cref="CustomItem"/>'s present in the current assembly.
        /// </summary>
        /// <param name="targetTypes">The <see cref="IEnumerable{T}"/> of <see cref="System.Type"/> containing the target types.</param>
        /// <param name="isIgnored">A value indicating whether the target types should be ignored.</param>
        /// <returns>A <see cref="IEnumerable{T}"/> of <see cref="CustomItem"/> which contains all unregistered <see cref="CustomItem"/>'s.</returns>
        public static IEnumerable<CustomItem> UnregisterItems(IEnumerable<Type> targetTypes, bool isIgnored = false)
        {
            List<CustomItem> unregisteredItems = new();

            foreach (CustomItem customItem in Registered)
            {
                if ((targetTypes.Contains(customItem.GetType()) && isIgnored) || (!targetTypes.Contains(customItem.GetType()) && !isIgnored))
                    continue;

                customItem.TryUnregister();
                unregisteredItems.Add(customItem);
            }

            return unregisteredItems;
        }

        /// <summary>
        /// Unregisters all the <see cref="CustomItem"/>'s present in the current assembly.
        /// </summary>
        /// <param name="targetItems">The <see cref="IEnumerable{T}"/> of <see cref="CustomItem"/> containing the target items.</param>
        /// <param name="isIgnored">A value indicating whether the target items should be ignored.</param>
        /// <returns>A <see cref="IEnumerable{T}"/> of <see cref="CustomItem"/> which contains all unregistered <see cref="CustomItem"/>'s.</returns>
        public static IEnumerable<CustomItem> UnregisterItems(IEnumerable<CustomItem> targetItems, bool isIgnored = false) => UnregisterItems(targetItems.Select(x => x.GetType()), isIgnored);

        /// <summary>
        /// Spawns the <see cref="CustomItem"/> in a specific location.
        /// </summary>
        /// <param name="x">The x coordinate.</param>
        /// <param name="y">The y coordinate.</param>
        /// <param name="z">The z coordinate.</param>
        /// <returns>The <see cref="Pickup"/> wrapper of the spawned <see cref="CustomItem"/>.</returns>
        public virtual Pickup? Spawn(float x, float y, float z) => Spawn(new Vector3(x, y, z));

        /// <summary>
        /// Spawns a <see cref="Item"/> as a <see cref="CustomItem"/> in a specific location.
        /// </summary>
        /// <param name="x">The x coordinate.</param>
        /// <param name="y">The y coordinate.</param>
        /// <param name="z">The z coordinate.</param>
        /// <param name="item">The <see cref="Item"/> to be spawned as a <see cref="CustomItem"/>.</param>
        /// <returns>The <see cref="Pickup"/> wrapper of the spawned <see cref="CustomItem"/>.</returns>
        public virtual Pickup? Spawn(float x, float y, float z, Item item) => Spawn(new Vector3(x, y, z), item);

        /// <summary>
        /// Spawns the <see cref="CustomItem"/> where a specific <see cref="Player"/> is, and optionally sets the previous owner.
        /// </summary>
        /// <param name="player">The <see cref="Player"/> position where the <see cref="CustomItem"/> will be spawned.</param>
        /// <param name="previousOwner">The previous owner of the pickup, can be null.</param>
        /// <returns>The <see cref="Pickup"/> of the spawned <see cref="CustomItem"/>.</returns>
        public virtual Pickup? Spawn(Player player, Player? previousOwner = null) => Spawn(player.Position, previousOwner);

        /// <summary>
        /// Spawns a <see cref="Item"/> as a <see cref="CustomItem"/> where a specific <see cref="Player"/> is, and optionally sets the previous owner.
        /// </summary>
        /// <param name="player">The <see cref="Player"/> position where the <see cref="CustomItem"/> will be spawned.</param>
        /// <param name="item">The <see cref="Item"/> to be spawned as a <see cref="CustomItem"/>.</param>
        /// <param name="previousOwner">The previous owner of the pickup, can be null.</param>
        /// <returns>The <see cref="Pickup"/> of the spawned <see cref="CustomItem"/>.</returns>
        public virtual Pickup? Spawn(Player player, Item item, Player? previousOwner = null) => Spawn(player.Position, item, previousOwner);

        /// <summary>
        /// Spawns the <see cref="CustomItem"/> in a specific position.
        /// </summary>
        /// <param name="position">The <see cref="Vector3"/> where the <see cref="CustomItem"/> will be spawned.</param>
        /// <param name="previousOwner">The <see cref="Pickup.PreviousOwner"/> of the item. Can be null.</param>
        /// <returns>The <see cref="Pickup"/> of the spawned <see cref="CustomItem"/>.</returns>
        public virtual Pickup? Spawn(Vector3 position, Player? previousOwner = null) => Spawn(position, Item.Create(Type), previousOwner);

        /// <summary>
        /// Spawns the <see cref="CustomItem"/> in a specific position.
        /// </summary>
        /// <param name="position">The <see cref="Vector3"/> where the <see cref="CustomItem"/> will be spawned.</param>
        /// <param name="item">The <see cref="Item"/> to be spawned as a <see cref="CustomItem"/>.</param>
        /// <param name="previousOwner">The <see cref="Pickup.PreviousOwner"/> of the item. Can be null.</param>
        /// <returns>The <see cref="Pickup"/> of the spawned <see cref="CustomItem"/>.</returns>
        public virtual Pickup? Spawn(Vector3 position, Item item, Player? previousOwner = null)
        {
            Pickup? pickup = item.CreatePickup(position);
            pickup.Scale = Scale;
            pickup.Weight = Weight;

            if (previousOwner is not null)
                pickup.PreviousOwner = previousOwner;

            SerialLookupTable[pickup.Serial] = this;

            return pickup;
        }

        /// <summary>
        /// Spawns <see cref="CustomItem"/>s inside <paramref name="spawnPoints"/>.
        /// </summary>
        /// <param name="spawnPoints">The spawn points <see cref="IEnumerable{T}"/>.</param>
        /// <param name="limit">The spawn limit.</param>
        /// <returns>Returns the number of spawned items.</returns>
        public virtual uint Spawn(IEnumerable<SpawnPoint> spawnPoints, uint limit)
        {
            uint spawned = 0;

            foreach (SpawnPoint spawnPoint in spawnPoints)
            {
                Log.Debug($"Attempting to spawn {Name} at {spawnPoint.Position}.");

                if (Loader.Random.NextDouble() * 100 >= spawnPoint.Chance || (limit > 0 && spawned >= limit))
                    continue;

                Pickup? pickup;
                if (spawnPoint is LockerSpawnPoint { UseChamber: true } lockerSpawnPoint)
                {
                    try
                    {
                        lockerSpawnPoint.GetSpawningInfo(out _, out Chamber? chamber, out Vector3 position);
                        pickup = Spawn(position);
                        chamber?.AddItem(pickup);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"CustomItem {Name}({Id} failed to spawn: {e.Message})");
                        continue;
                    }
                }
                else
                {
                    pickup = Spawn(spawnPoint.Position);
                }

                if (pickup == null)
                    continue;

                spawned++;

                /*if (pickup.Is(out FirearmPickup firearmPickup) && this is CustomWeapon customWeapon)
                {
                    // TODO: Set MaxAmmo (if synced)
                }*/
            }

            return spawned;
        }

        /// <summary>
        /// Spawns all items at their dynamic and static positions.
        /// </summary>
        public virtual void SpawnAll()
        {
            if (SpawnProperties is null)
                return;

            // This will go over each spawn property type (static, dynamic, role-based, room-based, and locker-based) to try and spawn the item.
            // It will attempt to spawn in role-based locations, then dynamic ones, followed by room-based, locker-based, and finally static.
            // Math.Min is used here to ensure that our recursive Spawn() calls do not result in exceeding the spawn limit config.
            // This is the same as:
            // int spawned = 0;
            // spawned += Spawn(SpawnProperties.RoleSpawnPoints, SpawnProperties.Limit);
            // if (spawned < SpawnProperties.Limit)
            //    spawned += Spawn(SpawnProperties.DynamicSpawnPoints, SpawnProperties.Limit - spawned);
            // if (spawned < SpawnProperties.Limit)
            //    spawned += Spawn(SpawnProperties.RoomSpawnPoints, SpawnProperties.Limit - spawned);
            // if (spawned < SpawnProperties.Limit)
            //    spawned += Spawn(SpawnProperties.LockerSpawnPoints, SpawnProperties.Limit - spawned);
            // if (spawned < SpawnProperties.Limit)
            //    Spawn(SpawnProperties.StaticSpawnPoints, SpawnProperties.Limit - spawned);
            Spawn(SpawnProperties.StaticSpawnPoints, Math.Min(SpawnProperties.Limit, SpawnProperties.Limit - Math.Min(Spawn(SpawnProperties.DynamicSpawnPoints, SpawnProperties.Limit), SpawnProperties.Limit - Math.Min(Spawn(SpawnProperties.RoleSpawnPoints, SpawnProperties.Limit), SpawnProperties.Limit - Math.Min(Spawn(SpawnProperties.RoomSpawnPoints, SpawnProperties.Limit), SpawnProperties.Limit - Spawn(SpawnProperties.LockerSpawnPoints, SpawnProperties.Limit))))));
        }

        /// <summary>
        /// Gives an <see cref="Item"/> as a <see cref="CustomItem"/> to a <see cref="Player"/>.
        /// </summary>
        /// <param name="player">The <see cref="Player"/> who will receive the item.</param>
        /// <param name="item">The <see cref="Item"/> to be given.</param>
        /// <param name="displayMessage">Indicates whether <see cref="ShowPickedUpMessage"/> will be called when the player receives the item.</param>
        public virtual void Give(Player player, Item item, bool displayMessage = true)
        {
            try
            {
                Log.Debug($"{Name}.{nameof(Give)}: Item Serial: {item.Serial} Ammo: {(item is Firearm firearm ? firearm.MagazineAmmo : -1)}");

                player.AddItem(item);

                Log.Debug($"{nameof(Give)}: Adding {item.Serial} to tracker.");
                SerialLookupTable[item.Serial] = this;

                Timing.CallDelayed(0.05f, () => OnAcquired(player, item, displayMessage));
            }
            catch (Exception e)
            {
                Log.Error($"{nameof(Give)}: {e}");
            }
        }

        /// <summary>
        /// Gives a <see cref="ItemPickupBase"/> as a <see cref="CustomItem"/> to a <see cref="Player"/>.
        /// </summary>
        /// <param name="player">The <see cref="Player"/> who will receive the item.</param>
        /// <param name="pickup">The <see cref="ItemPickupBase"/> to be given.</param>
        /// <param name="displayMessage">Indicates whether <see cref="ShowPickedUpMessage"/> will be called when the player receives the item.</param>
        public virtual void Give(Player player, Pickup pickup, bool displayMessage = true) => Give(player, player.AddItem(pickup), displayMessage);

        /// <summary>
        /// Gives the <see cref="CustomItem"/> to a player.
        /// </summary>
        /// <param name="player">The <see cref="Player"/> who will receive the item.</param>
        /// <param name="displayMessage">Indicates whether <see cref="ShowPickedUpMessage"/> will be called when the player receives the item.</param>
        public virtual void Give(Player player, bool displayMessage = true) => Give(player, Item.Create(Type), displayMessage);

        /// <summary>
        /// Called when the item is registered.
        /// </summary>
        public virtual void Init()
        {
            StringLookupTable.Add(Name, this);
            IdLookupTable.Add(Id, this);

            SubscribeEvents();
        }

        /// <summary>
        /// Called when the item is unregistered.
        /// </summary>
        public virtual void Destroy()
        {
            UnsubscribeEvents();

            StringLookupTable.Remove(Name);
            IdLookupTable.Remove(Id);
        }

        /// <summary>
        /// Checks the specified pickup to see if it is a custom item.
        /// </summary>
        /// <param name="pickup">The <see cref="Pickup"/> to check.</param>
        /// <returns>True if it is a custom item.</returns>
        public virtual bool Check(Pickup? pickup) => pickup is not null && Check(pickup.Serial);

        /// <summary>
        /// Checks the specified inventory item to see if it is a custom item.
        /// </summary>
        /// <param name="item">The <see cref="Item"/> to check.</param>
        /// <returns>True if it is a custom item.</returns>
        public virtual bool Check(Item? item) => item is not null && Check(item.Serial);

        /// <summary>
        /// Checks the specified player's current item to see if it is a custom item.
        /// </summary>
        /// <param name="player">The <see cref="Player"/> who's current item should be checked.</param>
        /// <returns>True if it is a custom item.</returns>
        public virtual bool Check(Player? player) => Check(player?.CurrentItem);

        /// <summary>
        /// Checks the specified player's current item to see if it is a custom item.
        /// </summary>
        /// <param name="serial">The serial of the <see cref="Item"/>/<see cref="Pickup"/> to check.</param>
        /// <returns>True if it is a custom item.</returns>
        public virtual bool Check(ushort serial) => SerialLookupTable.TryGetValue(serial, out CustomItem? customItem) && customItem!.Id == Id;

        /// <inheritdoc/>
        public override string ToString() => $"[{Name} ({Type}) | {Id}] {Description}";

        /// <summary>
        /// Registers a <see cref="CustomItem"/>.
        /// </summary>
        /// <returns>Returns a value indicating whether the <see cref="CustomItem"/> was registered.</returns>
        internal bool TryRegister()
        {
            if (!Instance?.Config.IsEnabled ?? false)
                return false;

            Log.Debug($"Trying to register {Name} ({Id}).");
            if (!Registered.Contains(this))
            {
                Log.Debug("Registered items doesn't contain this item yet..");
                if (Registered.Any(customItem => customItem.Id == Id))
                {
                    Log.Warn($"{Name} has tried to register with the same custom item ID as another item: {Id}. It will not be registered.");

                    return false;
                }

                Log.Debug("Adding item to registered list..");
                Registered.Add(this);

                Init();

                Log.Debug($"{Name} ({Id}) [{Type}] has been successfully registered.");

                return true;
            }

            Log.Warn($"Couldn't register {Name} ({Id}) [{Type}] as it already exists.");

            return false;
        }

        /// <summary>
        /// Tries to unregister a <see cref="CustomItem"/>.
        /// </summary>
        /// <returns>Returns a value indicating whether the <see cref="CustomItem"/> was unregistered.</returns>
        internal bool TryUnregister()
        {
            Destroy();

            if (!Registered.Remove(this))
            {
                Log.Warn($"Cannot unregister {Name} ({Id}) [{Type}], it hasn't been registered yet.");

                return false;
            }

            return true;
        }

        /// <summary>
        /// Called after the manager is initialized, to allow loading of special event handlers.
        /// </summary>
        protected virtual void SubscribeEvents()
        {
            Exiled.Events.Handlers.Player.Dying += OnInternalOwnerDying;
            Exiled.Events.Handlers.Player.DroppingItem += OnInternalDroppingItem;
            Exiled.Events.Handlers.Player.DroppingAmmo += OnInternalDroppingAmmo;
            Exiled.Events.Handlers.Player.ChangingItem += OnInternalChanging;
            Exiled.Events.Handlers.Player.Escaping += OnInternalOwnerEscaping;
            Exiled.Events.Handlers.Player.PickingUpItem += OnInternalPickingUp;
            Exiled.Events.Handlers.Player.ItemAdded += OnInternalItemAdded;
            Exiled.Events.Handlers.Player.Handcuffing += OnInternalOwnerHandcuffing;
            Exiled.Events.Handlers.Player.ChangingRole += OnInternalOwnerChangingRole;

            Exiled.Events.Handlers.Scp914.UpgradingPickup += OnInternalUpgradingPickup;
            Exiled.Events.Handlers.Scp914.UpgradingInventoryItem += OnInternalUpgradingInventoryItem;

            Exiled.Events.Handlers.Server.WaitingForPlayers += OnWaitingForPlayers;
        }

        /// <summary>
        /// Called when the manager is being destroyed, to allow unloading of special event handlers.
        /// </summary>
        protected virtual void UnsubscribeEvents()
        {
            Exiled.Events.Handlers.Player.Dying -= OnInternalOwnerDying;
            Exiled.Events.Handlers.Player.DroppingItem -= OnInternalDroppingItem;
            Exiled.Events.Handlers.Player.DroppingAmmo -= OnInternalDroppingAmmo;
            Exiled.Events.Handlers.Player.ChangingItem -= OnInternalChanging;
            Exiled.Events.Handlers.Player.Escaping -= OnInternalOwnerEscaping;
            Exiled.Events.Handlers.Player.PickingUpItem -= OnInternalPickingUp;
            Exiled.Events.Handlers.Player.ItemAdded -= OnInternalItemAdded;
            Exiled.Events.Handlers.Player.Handcuffing -= OnInternalOwnerHandcuffing;
            Exiled.Events.Handlers.Player.ChangingRole -= OnInternalOwnerChangingRole;

            Exiled.Events.Handlers.Scp914.UpgradingPickup -= OnInternalUpgradingPickup;
            Exiled.Events.Handlers.Scp914.UpgradingInventoryItem -= OnInternalUpgradingInventoryItem;

            Exiled.Events.Handlers.Server.WaitingForPlayers -= OnWaitingForPlayers;
        }

        /// <summary>
        /// Handles tracking items when they a player changes their role.
        /// </summary>
        /// <param name="ev"><see cref="OwnerChangingRoleEventArgs"/>.</param>
        protected virtual void OnOwnerChangingRole(OwnerChangingRoleEventArgs ev)
        {
        }

        /// <summary>
        /// Handles making sure custom items are not "lost" when a player dies.
        /// </summary>
        /// <param name="ev"><see cref="OwnerDyingEventArgs"/>.</param>
        protected virtual void OnOwnerDying(OwnerDyingEventArgs ev)
        {
        }

        /// <summary>
        /// Handles making sure custom items are not "lost" when a player escapes.
        /// </summary>
        /// <param name="ev"><see cref="OwnerEscapingEventArgs"/>.</param>
        protected virtual void OnOwnerEscaping(OwnerEscapingEventArgs ev)
        {
        }

        /// <summary>
        /// Handles making sure custom items are not "lost" when being handcuffed.
        /// </summary>
        /// <param name="ev"><see cref="OwnerHandcuffingEventArgs"/>.</param>
        protected virtual void OnOwnerHandcuffing(OwnerHandcuffingEventArgs ev)
        {
        }

        /// <summary>
        /// Handles tracking items when they are dropped by a player.
        /// </summary>
        /// <param name="ev"><see cref="DroppingItemEventArgs"/>.</param>
        protected virtual void OnDroppingItem(DroppingItemEventArgs ev)
        {
        }

        /// <summary>
        /// Handles tracking items when they are dropped by a player.
        /// </summary>
        /// <param name="ev"><see cref="DroppingItemEventArgs"/>.</param>
        [Obsolete("Use OnDroppingItem instead.", false)]
        protected virtual void OnDropping(DroppingItemEventArgs ev)
        {
        }

        /// <summary>
        /// Handles tracking when player requests drop of item which <see cref="ItemType"/> equals to the <see cref="ItemType"/> specified by <see cref="CustomItem"/>.
        /// </summary>
        /// <param name="ev"><see cref="DroppingAmmoEventArgs"/>.</param>
        protected virtual void OnDroppingAmmo(DroppingAmmoEventArgs ev)
        {
        }

        /// <summary>
        /// Handles tracking items when they are picked up by a player.
        /// </summary>
        /// <param name="ev"><see cref="PickingUpItemEventArgs"/>.</param>
        protected virtual void OnPickingUp(PickingUpItemEventArgs ev)
        {
        }

        /// <summary>
        /// Handles tracking items when they are selected in the player's inventory.
        /// </summary>
        /// <param name="ev"><see cref="ChangingItemEventArgs"/>.</param>
        protected virtual void OnChanging(ChangingItemEventArgs ev) => ShowSelectedMessage(ev.Player);

        /// <summary>
        /// Handles making sure custom items are not affected by SCP-914.
        /// </summary>
        /// <param name="ev"><see cref="UpgradingEventArgs"/>.</param>
        protected virtual void OnUpgrading(UpgradingEventArgs ev)
        {
        }

        /// <inheritdoc cref="OnUpgrading(UpgradingEventArgs)"/>
        protected virtual void OnUpgrading(UpgradingItemEventArgs ev)
        {
        }

        /// <summary>
        /// Called anytime the item enters a player's inventory by any means.
        /// </summary>
        /// <param name="player">The <see cref="Player"/> acquiring the item.</param>
        /// <param name="item">The <see cref="Item"/> being acquired.</param>
        /// <param name="displayMessage">Whether the Pickup hint should be displayed.</param>
        protected virtual void OnAcquired(Player player, Item item, bool displayMessage)
        {
            if (displayMessage)
                ShowPickedUpMessage(player);
        }

        /// <summary>
        /// Clears the lists of item uniqIDs and Pickups since any still in the list will be invalid.
        /// </summary>
        protected virtual void OnWaitingForPlayers()
        {
            SerialLookupTable.Clear();
        }

        /// <summary>
        /// Shows a message to the player upon picking up a custom item.
        /// </summary>
        /// <param name="player">The <see cref="Player"/> who will be shown the message.</param>
        protected virtual void ShowPickedUpMessage(Player player)
        {
            if (Instance!.Config.PickedUpHint.Show)
                player.ShowHint(string.Format(Instance.Config.PickedUpHint.Content, Name, Description), Instance.Config.PickedUpHint.Duration);
        }

        /// <summary>
        /// Shows a message to the player upon selecting a custom item.
        /// </summary>
        /// <param name="player">The <see cref="Player"/> who will be shown the message.</param>
        protected virtual void ShowSelectedMessage(Player player)
        {
            if (Instance!.Config.SelectedHint.Show)
                player.ShowHint(string.Format(Instance.Config.SelectedHint.Content, Name, Description), Instance.Config.SelectedHint.Duration);
        }

        private void OnInternalOwnerChangingRole(ChangingRoleEventArgs ev)
        {
            foreach (Item item in ev.Player.Items)
            {
                if (Check(item))
                    OnOwnerChangingRole(new OwnerChangingRoleEventArgs(item.Base, ev));
            }
        }

        private void OnInternalOwnerDying(DyingEventArgs ev)
        {
            foreach (Item item in ev.Player.Items)
            {
                if (Check(item))
                    OnOwnerDying(new OwnerDyingEventArgs(item, ev));
            }
        }

        private void OnInternalOwnerEscaping(EscapingEventArgs ev)
        {
            foreach (Item item in ev.Player.Items)
            {
                if (Check(item))
                    OnOwnerEscaping(new OwnerEscapingEventArgs(item, ev));
            }
        }

        private void OnInternalOwnerHandcuffing(HandcuffingEventArgs ev)
        {
            foreach (Item item in ev.Target.Items)
            {
                if (Check(item))
                    OnOwnerHandcuffing(new OwnerHandcuffingEventArgs(item, ev));
            }
        }

        private void OnInternalDroppingItem(DroppingItemEventArgs ev)
        {
            if (!Check(ev.Item))
                return;

            OnDroppingItem(ev);

            // TODO: Don't forget to remove this with next update
#pragma warning disable CS0618
            OnDropping(ev);
#pragma warning restore CS0618
        }

        private void OnInternalDroppingAmmo(DroppingAmmoEventArgs ev)
        {
            if (Type == ev.ItemType)
                OnDroppingAmmo(ev);
        }

        private void OnInternalPickingUp(PickingUpItemEventArgs ev)
        {
            if (Check(ev.Pickup) && ev.Player.Items.Count < 8)
                OnPickingUp(ev);
        }

        private void OnInternalItemAdded(ItemAddedEventArgs ev)
        {
            if (Check(ev.Pickup))
                OnAcquired(ev.Player, ev.Item, true);
        }

        private void OnInternalChanging(ChangingItemEventArgs ev)
        {
            if (!Check(ev.Item))
            {
                MirrorExtensions.ResyncSyncVar(ev.Player.ReferenceHub.networkIdentity, typeof(NicknameSync), nameof(NicknameSync.Network_displayName));
                return;
            }

            if (ShouldMessageOnGban)
            {
                foreach (Player player in Player.Get(RoleTypeId.Spectator))
                    Timing.CallDelayed(0.5f, () => player.SendFakeSyncVar(ev.Player.ReferenceHub.networkIdentity, typeof(NicknameSync), nameof(NicknameSync.Network_displayName), $"{ev.Player.Nickname} (CustomItem: {Name})"));
            }

            OnChanging(ev);
        }

        private void OnInternalUpgradingInventoryItem(UpgradingInventoryItemEventArgs ev)
        {
            if (!Check(ev.Item))
                return;

            ev.IsAllowed = false;

            OnUpgrading(new UpgradingItemEventArgs(ev.Player, ev.Item.Base, ev.KnobSetting));
        }

        private void OnInternalUpgradingPickup(UpgradingPickupEventArgs ev)
        {
            if (!Check(ev.Pickup))
                return;

            ev.IsAllowed = false;

            Timing.CallDelayed(3.5f, () =>
            {
                ev.Pickup.Position = ev.OutputPosition;
                OnUpgrading(new UpgradingEventArgs(ev.Pickup.Base, ev.OutputPosition, ev.KnobSetting));
            });
        }
    }
}
