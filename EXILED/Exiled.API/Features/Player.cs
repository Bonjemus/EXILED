// -----------------------------------------------------------------------
// <copyright file="Player.cs" company="ExMod Team">
// Copyright (c) ExMod Team. All rights reserved.
// Licensed under the CC BY-SA 3.0 license.
// </copyright>
// -----------------------------------------------------------------------

namespace Exiled.API.Features
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;

    using Core;
    using CustomPlayerEffects;
    using CustomPlayerEffects.Danger;
    using DamageHandlers;
    using Enums;
    using Exiled.API.Features.Core.Interfaces;
    using Exiled.API.Features.CustomStats;
    using Exiled.API.Features.Doors;
    using Exiled.API.Features.Hazards;
    using Exiled.API.Features.Items;
    using Exiled.API.Features.Pickups;
    using Exiled.API.Features.Pools;
    using Exiled.API.Features.Roles;
    using Exiled.API.Interfaces;
    using Exiled.API.Structs;
    using Extensions;
    using Footprinting;
    using global::Scp914;
    using Hints;
    using Interactables.Interobjects;
    using InventorySystem;
    using InventorySystem.Disarming;
    using InventorySystem.Items;
    using InventorySystem.Items.Armor;
    using InventorySystem.Items.Firearms.Attachments;
    using InventorySystem.Items.Firearms.Modules;
    using InventorySystem.Items.Firearms.ShotEvents;
    using InventorySystem.Items.Usables;
    using InventorySystem.Items.Usables.Scp330;
    using MapGeneration.Distributors;
    using MEC;
    using Mirror;
    using Mirror.LiteNetLib4Mirror;
    using PlayerRoles;
    using PlayerRoles.FirstPersonControl;
    using PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers;
    using PlayerRoles.RoleAssign;
    using PlayerRoles.Spectating;
    using PlayerRoles.Voice;
    using PlayerStatsSystem;
    using RelativePositioning;
    using RemoteAdmin;
    using RoundRestarting;
    using UnityEngine;
    using Utils;
    using Utils.Networking;
    using VoiceChat;
    using VoiceChat.Playbacks;

    using static DamageHandlers.DamageHandlerBase;

    using DamageHandlerBase = PlayerStatsSystem.DamageHandlerBase;
    using Firearm = Items.Firearm;
    using FirearmPickup = Pickups.FirearmPickup;
    using HumanRole = Roles.HumanRole;

    /// <summary>
    /// Represents the in-game player, by encapsulating a <see cref="global::ReferenceHub"/>.
    /// </summary>
    public class Player : TypeCastObject<Player>, IEntity, IWorldSpace
    {
#pragma warning disable SA1401
        /// <summary>
        /// A list of the player's CustomRole Ids.
        /// </summary>
        public readonly List<uint> CustomRoleIds = new();

        /// <summary>
        /// A list of the player's items.
        /// </summary>
        internal readonly List<Item> ItemsValue = new(8);

        /// <summary>
        /// A dictionary of custom item category limits.
        /// </summary>
        internal Dictionary<ItemCategory, sbyte> CustomCategoryLimits = new();

        /// <summary>
        /// A dictionary of custom ammo limits.
        /// </summary>
        internal Dictionary<AmmoType, ushort> CustomAmmoLimits = new();
#pragma warning restore SA1401

        private readonly HashSet<EActor> componentsInChildren = new();

        private ReferenceHub referenceHub;

        private Role role;

        /// <summary>
        /// Initializes a new instance of the <see cref="Player"/> class.
        /// </summary>
        /// <param name="referenceHub">The <see cref="global::ReferenceHub"/> of the player to be encapsulated.</param>
        public Player(ReferenceHub referenceHub)
        {
            ReferenceHub = referenceHub;
            Items = ItemsValue.AsReadOnly();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Player"/> class.
        /// </summary>
        /// <param name="gameObject">The <see cref="UnityEngine.GameObject"/> of the player.</param>
        public Player(GameObject gameObject)
        {
            ReferenceHub = ReferenceHub.GetHub(gameObject);
            Items = ItemsValue.AsReadOnly();
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="Player"/> class.
        /// </summary>
        ~Player()
        {
            DictionaryPool<string, object>.Pool.Return(SessionVariables);
            DictionaryPool<RoleTypeId, float>.Pool.Return(FriendlyFireMultiplier);
            DictionaryPool<string, Dictionary<RoleTypeId, float>>.Pool.Return(CustomRoleFriendlyFireMultiplier);
        }

        /// <summary>
        /// Gets a <see cref="Dictionary{TKey, TValue}"/> containing all <see cref="Player"/>'s on the server.
        /// </summary>
        public static Dictionary<GameObject, Player> Dictionary { get; } = new(Server.MaxPlayerCount, new ReferenceHub.GameObjectComparer());

        /// <summary>
        /// Gets a list of all <see cref="Player"/>'s on the server.
        /// </summary>
        public static IReadOnlyCollection<Player> List => Dictionary.Values.ToList();

        /// <summary>
        /// Gets a <see cref="Dictionary{TKey, TValue}"/> containing cached <see cref="Player"/> and their user ids.
        /// </summary>
        public static Dictionary<string, Player> UserIdsCache { get; } = new(20);

        /// <summary>
        /// Gets or sets a <see cref="CustomHumeShieldStat"/>.
        /// </summary>
        public CustomHumeShieldStat CustomHumeShieldStat { get; protected set; }

        /// <inheritdoc/>
        public IReadOnlyCollection<EActor> ComponentsInChildren => componentsInChildren;

        /// <summary>
        /// Gets or sets a <see cref="Dictionary{TKey, TValue}"/> containing cached <see cref="RoleTypeId"/> and their FF multiplier. This is for non-unique roles.
        /// </summary>
        public Dictionary<RoleTypeId, float> FriendlyFireMultiplier { get; set; } = DictionaryPool<RoleTypeId, float>.Pool.Get();

        /// <summary>
        /// Gets or sets a <see cref="Dictionary{TKey, TValue}"/> containing cached <see cref="string"/> and their  <see cref="Dictionary{TKey, TValue}"/> which is cached Role with FF multiplier. This is for unique custom roles.
        /// </summary>
        /// <remarks> Consider adding this as object, Dict so that CustomRoles, and Strings can be parsed. </remarks>
        public Dictionary<string, Dictionary<RoleTypeId, float>> CustomRoleFriendlyFireMultiplier { get; set; } = DictionaryPool<string, Dictionary<RoleTypeId, float>>.Pool.Get();

        /// <summary>
        /// Gets or sets a unique custom role that does not abide to base game for this player. Used in conjunction with <see cref="CustomRoleFriendlyFireMultiplier"/>.
        /// </summary>
        public string UniqueRole { get; set; } = string.Empty;

        /// <summary>
        /// Gets the encapsulated <see cref="global::ReferenceHub"/>.
        /// </summary>
        public ReferenceHub ReferenceHub
        {
            get => referenceHub;
            private set
            {
                referenceHub = value ?? throw new NullReferenceException("Player's ReferenceHub cannot be null!");
                GameObject = value.gameObject;
                HintDisplay = value.hints;
                Inventory = value.inventory;
                CameraTransform = value.PlayerCameraReference;

                CustomHealthStat = (HealthStat)value.playerStats._dictionarizedTypes[typeof(HealthStat)];
                value.playerStats._dictionarizedTypes[typeof(HumeShieldStat)] = value.playerStats.StatModules[Array.IndexOf(PlayerStats.DefinedModules, typeof(HumeShieldStat))] = CustomHumeShieldStat = new CustomHumeShieldStat { Hub = value };
            }
        }

        /// <summary>
        /// Gets the <see cref="PlayerRoleManager"/>.
        /// </summary>
        public PlayerRoleManager RoleManager => ReferenceHub.roleManager;

        /// <summary>
        /// Gets the player's ammo.
        /// </summary>
        public Dictionary<ItemType, ushort> Ammo => Inventory.UserInventory.ReserveAmmo;

        /// <summary>
        /// Gets the encapsulated <see cref="UnityEngine.GameObject"/>.
        /// </summary>
        public GameObject GameObject { get; private set; }

        /// <summary>
        /// Gets the <see cref="ReferenceHub"/>'s <see cref="UnityEngine.Transform"/>.
        /// </summary>
        public Transform Transform => ReferenceHub.transform;

        /// <summary>
        /// Gets the hint currently watched by the player.
        /// </summary>
        /// May be <see langword="null"/>.
        public Hint CurrentHint { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether the player is viewing a hint.
        /// </summary>
        public bool HasHint => CurrentHint != null;

        /// <summary>
        /// Gets the <see cref="ReferenceHub"/>'s <see cref="PersonalRadioPlayback"/>, can be null.
        /// </summary>
        public PersonalRadioPlayback RadioPlayback => Role is Roles.IVoiceRole voiceRole ? voiceRole.VoiceModule is IRadioVoiceModule radioVoiceModule ? radioVoiceModule.RadioPlayback : null : null;

        /// <summary>
        /// Gets the <see cref="Hints.HintDisplay"/> of the player.
        /// </summary>
        public HintDisplay HintDisplay { get; private set; }

        /// <summary>
        /// Gets the player's <see cref="InventorySystem.Inventory"/>.
        /// </summary>
        public Inventory Inventory { get; private set; }

        /// <summary>
        /// Gets the encapsulated <see cref="ReferenceHub"/>'s <see cref="Transform">PlayerCameraReference</see>.
        /// </summary>
        public Transform CameraTransform { get; private set; }

        /// <summary>
        /// Gets or sets the player's <see cref="VcMuteFlags"/>.
        /// </summary>
        public VcMuteFlags VoiceChatMuteFlags
        {
            get => VoiceChatMutes.GetFlags(ReferenceHub);
            set => VoiceChatMutes.SetFlags(ReferenceHub, value);
        }

        /// <summary>
        /// Gets or sets the player's id.
        /// </summary>
        public int Id
        {
            get => ReferenceHub.PlayerId;
            set => ReferenceHub._playerId = new(value);
        }

        /// <summary>
        /// Gets the player's user id.
        /// </summary>
        public string UserId => referenceHub.authManager.UserId;

        /// <summary>
        /// Gets the player's user id without the authentication.
        /// </summary>
        public string RawUserId { get; internal set; }

        /// <summary>
        /// Gets the player's authentication token.
        /// </summary>
        public string AuthenticationToken => ReferenceHub.authManager.GetAuthToken();

        /// <summary>
        /// Gets the player's authentication type.
        /// </summary>
        public AuthenticationType AuthenticationType
        {
            get
            {
                if (string.IsNullOrEmpty(UserId))
                    return AuthenticationType.Unknown;

                return UserId.Substring(UserId.LastIndexOf('@') + 1) switch
                {
                    "steam" => AuthenticationType.Steam,
                    "discord" => AuthenticationType.Discord,
                    "northwood" => AuthenticationType.Northwood,
                    "localhost" => AuthenticationType.LocalHost,
                    "ID_Dedicated" => AuthenticationType.DedicatedServer,
                    "offline" => AuthenticationType.Offline,
                    _ => AuthenticationType.Unknown,
                };
            }
        }

        /// <summary>
        /// Gets a value indicating whether the player is verified.
        /// </summary>
        /// <remarks>
        /// This is always <see langword="false"/> if <c>online_mode</c> is set to <see langword="false"/>.
        /// </remarks>
        public bool IsVerified { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether the player is a NPC.
        /// </summary>
        public bool IsNPC => ReferenceHub.IsDummy;

        /// <summary>
        /// Gets a value indicating whether the player has an active CustomName.
        /// </summary>
        public bool HasCustomName => ReferenceHub.nicknameSync.HasCustomName;

        /// <summary>
        /// Gets or sets the player's nickname displayed to other player.
        /// </summary>
        public string DisplayNickname
        {
            get => ReferenceHub.nicknameSync.DisplayName;
            set => ReferenceHub.nicknameSync.DisplayName = value;
        }

        /// <summary>
        /// Gets or sets the player's nickname, if null it sets the original nickname.
        /// </summary>
        public string CustomName
        {
            get => ReferenceHub.nicknameSync.Network_displayName ?? Nickname;
            set => ReferenceHub.nicknameSync.Network_displayName = value;
        }

        /// <summary>
        /// Gets the player's nickname.
        /// </summary>
        public string Nickname => ReferenceHub.nicknameSync.Network_myNickSync;

        /// <summary>
        /// Gets or sets the player's player info area bitmask.
        /// This property can be used to hide player name elements, such as the player's name, badges, etc.
        /// </summary>
        public PlayerInfoArea InfoArea
        {
            get => ReferenceHub.nicknameSync.Network_playerInfoToShow;
            set => ReferenceHub.nicknameSync.Network_playerInfoToShow = value;
        }

        /// <summary>
        /// Gets the player's current aspect ratio type.
        /// </summary>
        public AspectRatioType AspectRatio => ReferenceHub.aspectRatioSync.AspectRatio.GetAspectRatioLabel();

        /// <summary>
        /// Gets or sets the player's custom player info string. This string is displayed along with the player's <see cref="InfoArea"/>.
        /// </summary>
        public string CustomInfo
        {
            get => ReferenceHub.nicknameSync.Network_customPlayerInfoString;
            set
            {
                if (!NicknameSync.ValidateCustomInfo(value, out string rejectionText))
                {
                    Log.Error($"Could not set CustomInfo for {Nickname}. Reason: {rejectionText}");
                }

                InfoArea = string.IsNullOrEmpty(value) ? InfoArea & ~PlayerInfoArea.CustomInfo : InfoArea |= PlayerInfoArea.CustomInfo;
                ReferenceHub.nicknameSync.Network_customPlayerInfoString = value;
            }
        }

        /// <summary>
        /// Gets or sets the range at which this player's info can be viewed by others.
        /// </summary>
        public float InfoViewRange
        {
            get => ReferenceHub.nicknameSync.NetworkViewRange;
            set => ReferenceHub.nicknameSync.NetworkViewRange = value;
        }

        /// <summary>
        /// Gets the dictionary of the player's session variables.
        /// <para>
        /// Session variables can be used to save temporary data on players. Data is stored in a <see cref="Dictionary{TKey, TValue}"/>.
        /// The key of the data is always a <see cref="string"/>, whereas the value can be any <see cref="object"/>.
        /// The data stored in a player's session variables can be accessed by different assemblies; it is recommended to uniquely identify stored data so that it does not conflict with other plugins that may also be using the same name.
        /// Data saved with session variables is not being saved on player disconnect. If the data must be saved after the player's disconnects, a database must be used instead.
        /// </para>
        /// </summary>
        public Dictionary<string, object> SessionVariables { get; } = DictionaryPool<string, object>.Pool.Get();

        /// <summary>
        /// Gets a value indicating whether the player has Do Not Track (DNT) enabled. If this value is <see langword="true"/>, data about the player unrelated to server security shouldn't be stored.
        /// </summary>
        public bool DoNotTrack => ReferenceHub.authManager.DoNotTrack;

        /// <summary>
        /// Gets a value indicating whether the player is fully connected to the server.
        /// </summary>
        public bool IsConnected => GameObject != null;

        /// <summary>
        /// Gets a value indicating whether the player has a reserved slot.
        /// </summary>
        /// <seealso cref="GiveReservedSlot(bool)"/>
        /// <seealso cref="AddReservedSlot(string, bool)"/>
        public bool HasReservedSlot => ReservedSlot.HasReservedSlot(UserId);

        /// <summary>
        /// Gets a value indicating whether the player is in whitelist.
        /// </summary>
        /// <remarks>It will always return <see langword="true"/> if a whitelist is disabled on the server.</remarks>
        /// <seealso cref="GrantWhitelist(bool)"/>
        /// <seealso cref="AddToWhitelist(string, bool)"/>
        public bool IsWhitelisted => WhiteList.IsWhitelisted(UserId);

        /// <summary>
        /// Gets a value indicating whether the player has Remote Admin access.
        /// </summary>
        public bool RemoteAdminAccess => ReferenceHub.serverRoles.RemoteAdmin;

        /// <summary>
        /// Gets a value indicating whether the player has Admin Chat access.
        /// </summary>
        public bool AdminChatAccess => ReferenceHub.serverRoles.AdminChatPerms;

        /// <summary>
        /// Gets a value indicating a player's kick power.
        /// </summary>
        public byte KickPower => ReferenceHub.serverRoles.KickPower;

        /// <summary>
        /// Gets or sets a value indicating whether the player's overwatch is enabled.
        /// </summary>
        public bool IsOverwatchEnabled
        {
            get => ReferenceHub.serverRoles.IsInOverwatch;
            set => ReferenceHub.serverRoles.IsInOverwatch = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the player is allowed to enter noclip mode.
        /// </summary>
        /// <remarks>For forcing the player into noclip mode, see <see cref="FpcRole.IsNoclipEnabled"/>.</remarks>
        /// <seealso cref="FpcRole.IsNoclipEnabled"/>
        public bool IsNoclipPermitted
        {
            get => FpcNoclip.IsPermitted(ReferenceHub);
            set
            {
                if (value)
                    FpcNoclip.PermitPlayer(ReferenceHub);
                else
                    FpcNoclip.UnpermitPlayer(ReferenceHub);
            }
        }

        /// <summary>
        /// Gets or sets a value indicating the <see cref="Player"/> that currently has the player cuffed.
        /// <para>
        /// This value will be <see langword="null"/> if the player is not cuffed. Setting this value to <see langword="null"/> will uncuff the player if they are cuffed.
        /// </para>
        /// </summary>
        public Player Cuffer
        {
            get => Get(DisarmedPlayers.Entries.FirstOrDefault(entry => entry.DisarmedPlayer == NetworkIdentity.netId).Disarmer);
            set
            {
                for (int i = 0; i < DisarmedPlayers.Entries.Count; i++)
                {
                    if (DisarmedPlayers.Entries[i].DisarmedPlayer == Inventory.netId)
                    {
                        DisarmedPlayers.Entries.RemoveAt(i);
                        break;
                    }
                }

                if (value is not null)
                {
                    Inventory.SetDisarmedStatus(value.Inventory);
                    new DisarmedPlayersListMessage(DisarmedPlayers.Entries).SendToAuthenticated();
                }
            }
        }

        /// <summary>
        /// Gets or sets the player's position.
        /// </summary>
        /// <seealso cref="Teleport(Vector3)"/>
        /// <seealso cref="Teleport(object)"/>
        public virtual Vector3 Position
        {
            get => Transform.position;
            set => ReferenceHub.TryOverridePosition(value);
        }

        /// <summary>
        /// Gets or sets the relative player's position.
        /// </summary>
        /// <remarks>The value will be default if the player's role is not an <see cref="FpcRole"/>.</remarks>
        public RelativePosition RelativePosition
        {
            get => Role is FpcRole fpcRole ? fpcRole.RelativePosition : default;
            set => Position = value.Position;
        }

        /// <summary>
        /// Gets or sets the player's rotation.
        /// </summary>
        /// <returns>Returns the direction the player is looking at.</returns>
        public Quaternion Rotation
        {
            get => Transform.rotation;
            set => ReferenceHub.TryOverrideRotation(value.eulerAngles);
        }

        /// <summary>
        /// Gets the <see cref="Player"/>'s current movement speed.
        /// </summary>
        public Vector3 Velocity => ReferenceHub.GetVelocity();

        /// <summary>
        /// Gets the player's <see cref="Enums.LeadingTeam"/>.
        /// </summary>
        public LeadingTeam LeadingTeam => Role.Team.GetLeadingTeam();

        /// <summary>
        /// Gets or sets a value indicating the actual RA permissions.
        /// </summary>
        public PlayerPermissions RemoteAdminPermissions
        {
            get => (PlayerPermissions)ReferenceHub.serverRoles.Permissions;
            set => ReferenceHub.serverRoles.Permissions = (ulong)value;
        }

        /// <summary>
        /// Gets a <see cref="Roles.Role"/> that is unique to this player and this class. This allows modification of various aspects related to the role solely.
        /// <para>
        /// The type of the Role is different based on the <see cref="RoleTypeId"/> of the player, and casting should be used to modify the role.
        /// <br /><see cref="RoleTypeId.Spectator"/> = <see cref="Roles.SpectatorRole"/>.
        /// <br /><see cref="RoleTypeId.Overwatch"/> = <see cref="Roles.OverwatchRole"/>.
        /// <br /><see cref="RoleTypeId.None"/> = <see cref="Roles.NoneRole"/>.
        /// <br /><see cref="RoleTypeId.Scp049"/> = <see cref="Scp049Role"/>.
        /// <br /><see cref="RoleTypeId.Scp0492"/> = <see cref="Scp0492Role"/>.
        /// <br /><see cref="RoleTypeId.Scp079"/> = <see cref="Scp079Role"/>.
        /// <br /><see cref="RoleTypeId.Scp096"/> = <see cref="Scp096Role"/>.
        /// <br /><see cref="RoleTypeId.Scp106"/> = <see cref="Scp106Role"/>.
        /// <br /><see cref="RoleTypeId.Scp173"/> = <see cref="Scp173Role"/>.
        /// <br /><see cref="RoleTypeId.Scp3114"/> = <see cref="Scp3114Role"/>.
        /// <br /><see cref="RoleTypeId.Scp939"/> = <see cref="Scp939Role"/>.
        /// <br />If not listed above, the type of Role will be <see cref="HumanRole"/>.
        /// </para>
        /// <para>
        /// If the role object is stored, it may become invalid if the player changes roles. Thus, the <see cref="Role.IsValid"/> property can be checked. If this property is <see langword="false"/>, the role should be discarded and this property should be used again to get the new Role.
        /// This role is automatically cached until it changes, and it is recommended to use this property directly rather than storing the property yourself.
        /// </para>
        /// <para>
        /// Roles and RoleTypeIds can be compared directly. <c>Player.Role == RoleTypeId.Scp079</c> is valid and will return <see langword="true"/> if the player is SCP-079. To set the player's role, see <see cref="Role.Set(RoleTypeId, SpawnReason, RoleSpawnFlags)"/>.
        /// </para>
        /// </summary>
        /// <seealso cref="Role.Set(RoleTypeId, SpawnReason, RoleSpawnFlags)"/>
        public Role Role
        {
            get => role ??= Role.Create(RoleManager.CurrentRole);
            internal set
            {
                PreviousRole = role?.Type ?? RoleTypeId.None;
                role = value;
            }
        }

        /// <summary>
        /// Gets the role that player had before changing role.
        /// </summary>
        public RoleTypeId PreviousRole { get; private set; }

        /// <summary>
        /// Gets or sets the player's SCP preferences.
        /// </summary>
        public ScpSpawnPreferences.SpawnPreferences ScpPreferences
        {
            get
            {
                if (ScpSpawnPreferences.Preferences.TryGetValue(Connection.connectionId, out ScpSpawnPreferences.SpawnPreferences value))
                    return value;

                return default;
            }
            set => ScpSpawnPreferences.Preferences[Connection.connectionId] = value;
        }

        /// <summary>
        /// Gets a value indicating whether the player is cuffed.
        /// </summary>
        /// <remarks>Players can be cuffed without another player being the cuffer.</remarks>
        public bool IsCuffed => Inventory.IsDisarmed();

        /// <summary>
        /// Gets a value indicating whether the player is reloading a weapon.
        /// </summary>
        public bool IsReloading => CurrentItem is Firearm firearm && !firearm.IsReloading;

        /// <summary>
        /// Gets a value indicating whether the player is aiming with a weapon.
        /// </summary>
        public bool IsAimingDownWeapon => CurrentItem is Firearm firearm && firearm.Aiming;

        /// <summary>
        /// Gets a value indicating whether the player has enabled weapon's flashlight module.
        /// </summary>
        public bool HasFlashlightModuleEnabled => CurrentItem is Firearm firearm && firearm.FlashlightEnabled;

        /// <summary>
        /// Gets a value indicating whether the player is jumping.
        /// </summary>
        public bool IsJumping => Role is FpcRole fpc && fpc.FirstPersonController.FpcModule.Motor.IsJumping;

        /// <summary>
        /// Gets the player's IP address.
        /// </summary>
        public string IPAddress => ReferenceHub.networkIdentity.connectionToClient.address;

        /// <summary>
        /// Gets the player's command sender instance.
        /// </summary>
        public PlayerCommandSender Sender => ReferenceHub.queryProcessor._sender;

        /// <summary>
        /// Gets player's <see cref="NetworkConnection"/>.
        /// </summary>
        public NetworkConnection Connection => ReferenceHub.connectionToClient;

        /// <summary>
        /// Gets the player's <see cref="Mirror.NetworkIdentity"/>.
        /// </summary>
        public NetworkIdentity NetworkIdentity => ReferenceHub.networkIdentity;

        /// <summary>
        /// Gets the player's net ID.
        /// </summary>
        public uint NetId => ReferenceHub.netId;

        /// <summary>
        /// Gets a value indicating whether the player is the host.
        /// </summary>
        public bool IsHost => ReferenceHub.isLocalPlayer;

        /// <summary>
        /// Gets a value indicating whether the player is alive.
        /// </summary>
        public bool IsAlive => !IsDead;

        /// <summary>
        /// Gets a value indicating whether the player is dead.
        /// </summary>
        public bool IsDead => Role?.IsDead ?? false;

        /// <summary>
        /// Gets a value indicating whether the player's <see cref="RoleTypeId"/> is any Foundation Forces.
        /// Equivalent to checking the player's <see cref="Team"/>.
        /// </summary>
        public bool IsFoundationForces => Role?.Team is Team.FoundationForces;

        /// <summary>
        /// Gets a value indicating whether the player's <see cref="RoleTypeId"/> is any NTF rank and not a Facility Guard.
        /// Equivalent to checking the player's <see cref="Team"/>.
        /// </summary>
        public bool IsNTF => Role?.Team is Team.FoundationForces && Role?.Type is not RoleTypeId.FacilityGuard;

        /// <summary>
        /// Gets a value indicating whether the player's <see cref="RoleTypeId"/> is any Chaos rank.
        /// Equivalent to checking the player's <see cref="Team"/>.
        /// </summary>
        public bool IsCHI => Role?.Team is Team.ChaosInsurgency;

        /// <summary>
        /// Gets a value indicating whether the player's <see cref="RoleTypeId"/> is any SCP.
        /// Equivalent to checking the player's <see cref="Team"/>.
        /// </summary>
        public bool IsScp => Role?.Type.IsScp() ?? false;

        /// <summary>
        /// Gets a value indicating whether the player's <see cref="RoleTypeId"/> is any human rank.
        /// </summary>
        public bool IsHuman => Role is not null && Role.Is(out HumanRole _);

        /// <summary>
        /// Gets a value indicating whether the player's <see cref="RoleTypeId"/> is equal to <see cref="RoleTypeId.Tutorial"/>.
        /// </summary>
        public bool IsTutorial => Role?.Type is RoleTypeId.Tutorial;

        /// <summary>
        /// Gets a value indicating whether the player's friendly fire is enabled.
        /// <br>This property only determines if this player can deal damage to players on the same team;</br>
        /// <br>This player can be damaged by other players on their own team even if this property is <see langword="false"/>.</br>
        /// </summary>
        public bool IsFriendlyFireEnabled => FriendlyFireMultiplier.Count > 0 || CustomRoleFriendlyFireMultiplier.Count > 0;

        /// <summary>
        /// Gets or sets the player's scale.
        /// </summary>
        public Vector3 Scale
        {
            get => Role.Base is FpcStandardRoleBase fpcRole ? fpcRole.FpcModule.Motor.ScaleController.Scale : Vector3.one;
            set => SetScale(value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether the player's bypass mode is enabled.
        /// </summary>
        public bool IsBypassModeEnabled
        {
            get => ReferenceHub.serverRoles.BypassMode;
            set => ReferenceHub.serverRoles.BypassMode = value;
        }

        /// <summary>
        /// Gets or sets the player's emotion.
        /// </summary>
        public EmotionPresetType Emotion
        {
            get => EmotionSync.GetEmotionPreset(ReferenceHub);
            set => EmotionSync.ServerSetEmotionPreset(ReferenceHub, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether the player is muted.
        /// </summary>
        /// <remarks>This property will NOT persistently mute and unmute the player. For persistent mutes, see <see cref="Mute(bool)"/> and <see cref="UnMute(bool)"/>.</remarks>
        public bool IsMuted
        {
            get => VoiceChatMutes.QueryLocalMute(UserId, false);
            set
            {
                if (value)
                    VoiceChatMuteFlags |= VcMuteFlags.LocalRegular;
                else
                    VoiceChatMuteFlags &= ~VcMuteFlags.LocalRegular;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the player is global muted.
        /// </summary>
        /// <remarks>This property will NOT persistently mute and unmute the player. For persistent mutes, see <see cref="Mute(bool)"/> and <see cref="UnMute(bool)"/>.</remarks>
        public bool IsGlobalMuted
        {
            get => VoiceChatMutes.Mutes.Contains(UserId) && VoiceChatMuteFlags.HasFlag(VcMuteFlags.GlobalRegular);
            set
            {
                if (value)
                    VoiceChatMuteFlags |= VcMuteFlags.GlobalRegular;
                else
                    VoiceChatMuteFlags &= ~VcMuteFlags.GlobalRegular;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the player is intercom muted.
        /// </summary>
        /// <remarks>This property will NOT persistently mute and unmute the player. For persistent mutes, see <see cref="Mute(bool)"/> and <see cref="UnMute(bool)"/>.</remarks>
        public bool IsIntercomMuted
        {
            get => VoiceChatMutes.QueryLocalMute(UserId, true);
            set
            {
                if (value)
                    VoiceChatMuteFlags |= VcMuteFlags.LocalIntercom;
                else
                    VoiceChatMuteFlags &= ~VcMuteFlags.LocalIntercom;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the player is speaking.
        /// </summary>
        public bool IsSpeaking => Role is Roles.IVoiceRole voiceRole && voiceRole.VoiceModule.ServerIsSending;

        /// <summary>
        /// Gets the player's voice color.
        /// </summary>
        public Color VoiceColor => ReferenceHub.serverRoles.GetVoiceColor();

        /// <summary>
        /// Gets or sets the player's voice channel.
        /// </summary>
        public VoiceChatChannel VoiceChannel
        {
            get => Role is Roles.IVoiceRole voiceRole ? voiceRole.VoiceModule.CurrentChannel : VoiceChatChannel.None;
            set
            {
                if (Role is not Roles.IVoiceRole voiceRole)
                    return;

                voiceRole.VoiceModule.CurrentChannel = value;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the player is transmitting on a Radio.
        /// </summary>
        public bool IsTransmitting => PersonalRadioPlayback.IsTransmitting(ReferenceHub);

        /// <summary>
        /// Gets or sets a value indicating whether the player has godmode enabled.
        /// </summary>
        public bool IsGodModeEnabled
        {
            get => ReferenceHub.characterClassManager.GodMode;
            set => ReferenceHub.characterClassManager.GodMode = value;
        }

        /// <summary>
        /// Gets the player's unit name.
        /// </summary>
        public string UnitName => Role is HumanRole humanRole ? humanRole.UnitName : string.Empty;

        /// <summary>
        /// Gets or sets the player's unit id.
        /// </summary>
        public byte UnitId
        {
            get => Role.Base is PlayerRoles.HumanRole humanRole ? humanRole.UnitNameId : byte.MinValue;
            set => _ = Role.Base is PlayerRoles.HumanRole humanRole ? humanRole.UnitNameId = value : _ = value;
        }

        /// <summary>
        /// Gets an array of <see cref="DangerStackBase"/> if the Scp1853 effect is enabled or an empty array if it is not enabled.
        /// </summary>
        public DangerStackBase[] Dangers
        {
            get
            {
                if (!TryGetEffect(EffectType.Scp1853, out StatusEffectBase scp1853Effect) || !scp1853Effect.IsEnabled)
                    return Array.Empty<DangerStackBase>();

                return (scp1853Effect as Scp1853).Dangers;
            }
        }

        /// <summary>
        /// Gets a list of active <see cref="DangerStackBase"/> the player has.
        /// </summary>
        public IEnumerable<DangerStackBase> ActiveDangers => Dangers.Where(d => d.IsActive);

        /// <summary>
        /// Gets or sets the player's health.
        /// If the health is greater than the <see cref="MaxHealth"/>, the MaxHealth will also be changed to match the health.
        /// </summary>
        public float Health
        {
            get => CustomHealthStat.CurValue;
            set
            {
                if (value > MaxHealth)
                    MaxHealth = value;

                CustomHealthStat.CurValue = value;
            }
        }

        /// <summary>
        /// Gets or sets the player's maximum health.
        /// </summary>
        public float MaxHealth
        {
            get => CustomHealthStat.MaxValue;
            set => CustomHealthStat.MaxValue = value;
        }

        /// <summary>
        /// Gets or sets the player's artificial health.
        /// If the health is greater than the <see cref="MaxArtificialHealth"/>, it will also be changed to match the artificial health.
        /// </summary>
        public float ArtificialHealth
        {
            get => ActiveArtificialHealthProcesses.FirstOrDefault()?.CurrentAmount ?? 0f;
            set
            {
                if (value > MaxArtificialHealth)
                    MaxArtificialHealth = value;

                AhpStat.AhpProcess ahp = ActiveArtificialHealthProcesses.FirstOrDefault();

                if (ahp is not null)
                    ahp.CurrentAmount = value;
            }
        }

        /// <summary>
        /// Gets or sets the player's maximum artificial health.
        /// </summary>
        public float MaxArtificialHealth
        {
            get => ActiveArtificialHealthProcesses.FirstOrDefault()?.Limit ?? 0f;
            set
            {
                if (!ActiveArtificialHealthProcesses.Any())
                    AddAhp(value);

                AhpStat.AhpProcess ahp = ActiveArtificialHealthProcesses.FirstOrDefault();

                if (ahp is not null)
                    ahp.Limit = value;
            }
        }

        /// <summary>
        /// Gets or sets the player's Hume Shield.
        /// </summary>
        /// <remarks>This value can bypass the role's hume shield maximum. However, this value will only be visible to the end-player as Hume Shield if <see cref="FpcRole.IsHumeShieldedRole"/> is <see langword="true"/>. Otherwise, the game will treat the player as though they have the amount of Hume Shield specified, even though they cannot see it.</remarks>
        public float HumeShield
        {
            get => CustomHumeShieldStat.CurValue;
            set => CustomHumeShieldStat.CurValue = value;
        }

        /// <summary>
        /// Gets or sets the players maximum Hume Shield.
        /// </summary>
        public float MaxHumeShield
        {
            get => CustomHumeShieldStat.MaxValue;
            set => CustomHumeShieldStat.MaxValue = value;
        }

        /// <summary>
        /// Gets or sets the players multiplier for gaining HumeShield.
        /// </summary>
        public float HumeShieldRegenerationMultiplier
        {
            get => CustomHumeShieldStat.ShieldRegenerationMultiplier;
            set => CustomHumeShieldStat.ShieldRegenerationMultiplier = value;
        }

        /// <summary>
        /// Gets a <see cref="IEnumerable{T}"/> of all active Artificial Health processes on the player.
        /// </summary>
        public IEnumerable<AhpStat.AhpProcess> ActiveArtificialHealthProcesses => ReferenceHub.playerStats.GetModule<AhpStat>()._activeProcesses;

        /// <summary>
        /// Gets the player's <see cref="PlayerStatsSystem.HumeShieldStat"/>.
        /// </summary>
        [Obsolete("Use " + nameof(CustomHumeShieldStat) + " instead.")]
        public HumeShieldStat HumeShieldStat => CustomHumeShieldStat;

        /// <summary>
        /// Gets or sets the item in the player's hand. Value will be <see langword="null"/> if the player is not holding anything.
        /// </summary>
        /// <seealso cref="DropHeldItem()"/>
        public Item CurrentItem
        {
            get => Item.Get(Inventory.CurInstance);
            set
            {
                if (CurrentItem is MicroHid microHid)
                    microHid.State = InventorySystem.Items.MicroHID.Modules.MicroHidPhase.Standby;

                if (value is null || value.Type == ItemType.None)
                {
                    Inventory.ServerSelectItem(0);
                    return;
                }

                if (!Inventory.UserInventory.Items.TryGetValue(value.Serial, out _))
                    AddItem(value.Base);

                Inventory.ServerSelectItem(value.Serial);
            }
        }

        /// <summary>
        /// Gets the armor that the player is currently wearing. Value will be <see langword="null"/> if the player is not wearing any armor.
        /// </summary>
        public Armor CurrentArmor => Inventory.TryGetBodyArmor(out BodyArmor armor) ? Item.Get<Armor>(armor) : null;

        /// <summary>
        /// Gets the <see cref="StaminaStat"/> class.
        /// </summary>
        public StaminaStat StaminaStat => ReferenceHub.playerStats.GetModule<StaminaStat>();

        /// <summary>
        /// Gets or sets the amount of stamina the player has.
        /// </summary>
        /// <remarks>This will always be a value between <c>0-1</c>, <c>0</c> representing no stamina and <c>1</c> representing maximum stamina.</remarks>
        public float Stamina
        {
            get => StaminaStat.CurValue;
            set => StaminaStat.CurValue = value;
        }

        /// <summary>
        /// Gets a value indicating whether the staff bypass is enabled.
        /// </summary>
        public bool IsStaffBypassEnabled => ReferenceHub.authManager.BypassBansFlagSet;

        /// <summary>
        /// Gets or sets the player's group name.
        /// </summary>
        public string GroupName
        {
            get => ServerStatic.PermissionsHandler.Members.TryGetValue(UserId, out string groupName) ? groupName : null;
            set => ServerStatic.PermissionsHandler.Members[UserId] = value;
        }

        /// <summary>
        /// Gets the current <see cref="Room"/> the player is in.
        /// </summary>
        public Room CurrentRoom => Room.FindParentRoom(GameObject);

        /// <summary>
        /// Gets the current zone the player is in.
        /// </summary>
        public ZoneType Zone => CurrentRoom?.Zone ?? ZoneType.Unspecified;

        /// <summary>
        /// Gets the current <see cref="Features.Lift"/> the player is in. Can be <see langword="null"/>.
        /// </summary>
        public Lift Lift => Lift.Get(Position);

        /// <summary>
        /// Gets all currently active <see cref="StatusEffectBase"> effects</see>.
        /// </summary>
        /// <seealso cref="EnableEffect(EffectType, float, bool)"/>
        /// <seealso cref="EnableEffect(StatusEffectBase, float, bool)"/>
        /// <seealso cref="EnableEffect(string, float, bool)"/>
        /// <seealso cref="EnableEffect{T}(float, bool)"/>
        /// <seealso cref="EnableEffects(IEnumerable{EffectType}, float, bool)"/>
        public IEnumerable<StatusEffectBase> ActiveEffects => referenceHub.playerEffectsController.AllEffects.Where(effect => effect.Intensity > 0);

        /// <summary>
        /// Gets or sets the player's group.
        /// </summary>
        public UserGroup Group
        {
            get => ReferenceHub.serverRoles.Group;
            set => ReferenceHub.serverRoles.SetGroup(value, false);
        }

        /// <summary>
        /// Gets or sets the player's rank color.
        /// </summary>
        public string RankColor
        {
            get => ReferenceHub.serverRoles.Network_myColor;
            set => ReferenceHub.serverRoles.SetColor(value);
        }

        /// <summary>
        /// Gets or sets the player's rank name.
        /// </summary>
        public string RankName
        {
            get => ReferenceHub.serverRoles.Network_myText;
            set => ReferenceHub.serverRoles.SetText(value);
        }

        /// <summary>
        /// Gets the global badge of the player. Value will be <see langword="null"/> if the player does not have a global badge.
        /// </summary>
        public Badge? GlobalBadge
        {
            get
            {
                if (string.IsNullOrEmpty(ReferenceHub.serverRoles.NetworkGlobalBadge))
                    return null;

                ServerRoles serverRoles = ReferenceHub.serverRoles;

                return new Badge(serverRoles._bgt, serverRoles._bgc, true);
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the player's badge is hidden.
        /// </summary>
        public bool BadgeHidden
        {
            get => !string.IsNullOrEmpty(ReferenceHub.serverRoles.HiddenBadge);
            set
            {
                if (value)
                    ReferenceHub.serverRoles.TryHideTag();
                else
                    ReferenceHub.serverRoles.RefreshLocalTag();
            }
        }

        /// <summary>
        /// Gets a value indicating whether the player is Northwood staff.
        /// </summary>
        public bool IsNorthwoodStaff => ReferenceHub.authManager.NorthwoodStaff;

        /// <summary>
        /// Gets a value indicating whether the player is a global moderator.
        /// </summary>
        public bool IsGlobalModerator => ReferenceHub.authManager.RemoteAdminGlobalAccess;

        /// <summary>
        /// Gets a value indicating whether the player is in the pocket dimension.
        /// </summary>
        public bool IsInPocketDimension => CurrentRoom?.Type is RoomType.Pocket;

        /// <summary>
        /// Gets or sets a value indicating whether the player should use stamina system.
        /// </summary>
        public bool IsUsingStamina { get; set; } = true;

        /// <summary>
        /// Gets the player's ping.
        /// </summary>
        public int Ping => LiteNetLib4MirrorServer.GetPing(Connection.connectionId);

        /// <summary>
        /// Gets the player's items.
        /// </summary>
        public IReadOnlyCollection<Item> Items { get; }

        /// <summary>
        /// Gets a value indicating whether the player's inventory is empty.
        /// </summary>
        public bool IsInventoryEmpty => Items.Count is 0;

        /// <summary>
        /// Gets a value indicating whether the player's inventory is full.
        /// </summary>
        public bool IsInventoryFull => Items.Count >= Inventory.MaxSlots;

        /// <summary>
        /// Gets a value indicating whether the player has agreed to microphone recording.
        /// </summary>
        public bool AgreedToRecording => VoiceChatPrivacySettings.CheckUserFlags(ReferenceHub, VcPrivacyFlags.SettingsSelected | VcPrivacyFlags.AllowRecording | VcPrivacyFlags.AllowMicCapture);

        /// <summary>
        /// Gets a <see cref="Player"/> <see cref="IEnumerable{T}"/> of spectators that are currently spectating this <see cref="Player"/>.
        /// </summary>
        public IEnumerable<Player> CurrentSpectatingPlayers => List.Where(player => ReferenceHub.IsSpectatedBy(player.ReferenceHub));

        /// <summary>
        /// Gets a <see cref="Dictionary{TKey, TValue}"/> which contains all player's preferences.
        /// </summary>
        public Dictionary<FirearmType, AttachmentIdentifier[]> Preferences => Firearm.PlayerPreferences.FirstOrDefault(kvp => kvp.Key == this).Value;

        /// <summary>
        /// Gets the player's <see cref="Footprinting.Footprint"/>.
        /// </summary>
        public Footprint Footprint => new(ReferenceHub);

        /// <summary>
        /// Gets or sets a value indicating whether the player is spawn protected.
        /// </summary>
        public bool IsSpawnProtected
        {
            get => IsEffectActive<SpawnProtected>();
            set
            {
                if (value)
                    EnableEffect<SpawnProtected>(SpawnProtected.SpawnDuration);
                else
                    DisableEffect<SpawnProtected>();
            }
        }

        /// <summary>
        /// Gets a dictionary for storing player objects of connected but not yet verified players.
        /// </summary>
        internal static ConditionalWeakTable<GameObject, Player> UnverifiedPlayers { get; } = new();

        /// <summary>
        /// Gets or sets a <see cref="CustomHealthStat"/>.
        /// </summary>
        protected HealthStat CustomHealthStat { get; set; }

        /// <summary>
        /// Converts LabApi player to EXILED player.
        /// </summary>
        /// <param name="player">The LabApi player.</param>
        /// <returns>EXILED player.</returns>
        public static implicit operator Player(LabApi.Features.Wrappers.Player player) => Get(player);

        /// <summary>
        /// Converts LabApi player to EXILED player.
        /// </summary>
        /// <param name="player">The LabApi player.</param>
        /// <returns>EXILED player.</returns>
        public static implicit operator LabApi.Features.Wrappers.Player(Player player) => LabApi.Features.Wrappers.Player.Get(player.ReferenceHub);

        /// <summary>
        /// Gets a <see cref="Player"/> <see cref="IEnumerable{T}"/> filtered by side. Can be empty.
        /// </summary>
        /// <param name="side">The players' side.</param>
        /// <returns>The filtered <see cref="IEnumerable{T}"/>.</returns>
        public static IEnumerable<Player> Get(Side side) => List.Where(player => player.Role.Side == side);

        /// <summary>
        /// Gets a <see cref="Player"/> <see cref="IEnumerable{T}"/> filtered by team. Can be empty.
        /// </summary>
        /// <param name="team">The players' team.</param>
        /// <returns>The filtered <see cref="IEnumerable{T}"/>.</returns>
        public static IEnumerable<Player> Get(Team team) => List.Where(player => player.Role.Team == team);

        /// <summary>
        /// Gets a <see cref="Player"/> <see cref="IEnumerable{T}"/> filtered by role. Can be empty.
        /// </summary>
        /// <param name="role">The players' role.</param>
        /// <returns>The filtered <see cref="IEnumerable{T}"/>.</returns>
        public static IEnumerable<Player> Get(RoleTypeId role) => List.Where(player => player.Role == role);

        /// <summary>
        /// Gets a <see cref="IEnumerable{T}"/> of <see cref="Player"/> filtered based on a predicate.
        /// </summary>
        /// <param name="predicate">The condition to satisfy.</param>
        /// <returns>A <see cref="IEnumerable{T}"/> of <see cref="Player"/> which contains elements that satisfy the condition.</returns>
        public static IEnumerable<Player> Get(Func<Player, bool> predicate) => List.Where(predicate);

        /// <summary>
        /// Gets the <see cref="Player"/> belonging to the <see cref="CommandSystem.ICommandSender"/>, if any.
        /// </summary>
        /// <param name="sender">The command sender.</param>
        /// <returns>A <see cref="Player"/> or <see langword="null"/> if not found.</returns>
        public static Player Get(CommandSystem.ICommandSender sender) => Get(sender as CommandSender);

        /// <summary>
        /// Gets the <see cref="Player"/> belonging to the <see cref="Footprinting.Footprint"/>, if any.
        /// </summary>
        /// <param name="footprint">The Footprint.</param>
        /// <returns>A <see cref="Player"/> or <see langword="null"/> if not found.</returns>
        public static Player Get(Footprint footprint) => Get(footprint.Hub);

        /// <summary>
        /// Gets the <see cref="Player"/> belonging to the <see cref="CommandSender"/>, if any.
        /// </summary>
        /// <param name="sender">The command sender.</param>
        /// <returns>A <see cref="Player"/> or <see langword="null"/> if not found.</returns>
        public static Player Get(CommandSender sender) => Get(sender.SenderId);

        /// <summary>
        /// Gets the <see cref="Player"/> belonging to the <see cref="global::ReferenceHub"/>, if any.
        /// </summary>
        /// <param name="referenceHub">The player's <see cref="global::ReferenceHub"/>.</param>
        /// <returns>A <see cref="Player"/> or <see langword="null"/> if not found.</returns>
        public static Player Get(ReferenceHub referenceHub)
        {
            try
            {
                return referenceHub == null || referenceHub.gameObject == null ? null : Get(referenceHub.gameObject);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the <see cref="Player"/> belonging to the <see cref="Collider"/>, if any.
        /// </summary>
        /// <param name="collider"><see cref="Collider"/>.</param>
        /// <returns>A <see cref="Player"/> or <see langword="null"/> if not found.</returns>
        public static Player Get(Collider collider) => Get(collider.transform.root.gameObject);

        /// <summary>
        /// Gets the <see cref="Player"/> belonging to a specific netId, if any.
        /// </summary>
        /// <param name="netId">The player's <see cref="NetworkIdentity.netId"/>.</param>
        /// <returns>The <see cref="Player"/> owning the netId, or <see langword="null"/> if not found.</returns>
        public static Player Get(uint netId) => ReferenceHub.TryGetHubNetID(netId, out ReferenceHub hub) ? Get(hub) : null;

        /// <summary>
        /// Gets the <see cref="Player"/> belonging to a specific <see cref="Mirror.NetworkIdentity"/>, if any.
        /// </summary>
        /// <param name="netIdentity">The player's <see cref="Mirror.NetworkIdentity"/>.</param>
        /// <returns>The <see cref="Player"/> owning the <see cref="Mirror.NetworkIdentity"/>, or <see langword="null"/> if not found.</returns>
        public static Player Get(NetworkIdentity netIdentity) => Get(netIdentity.netId);

        /// <summary>
        /// Gets the <see cref="Player"/> belonging to a specific <see cref="NetworkConnection"/>, if any.
        /// </summary>
        /// <param name="conn">The player's <see cref="NetworkConnection"/>.</param>
        /// <returns>The <see cref="Player"/> owning the <see cref="NetworkConnection"/>, or <see langword="null"/> if not found.</returns>
        public static Player Get(NetworkConnection conn) => Get(conn.identity);

        /// <summary>
        /// Gets the <see cref="Player"/> belonging to the <see cref="UnityEngine.GameObject"/>, if any.
        /// </summary>
        /// <param name="gameObject">The player's <see cref="UnityEngine.GameObject"/>.</param>
        /// <returns>A <see cref="Player"/> or <see langword="null"/> if not found.</returns>
        public static Player Get(GameObject gameObject)
        {
            if (gameObject == null)
                return null;

            if (Dictionary.TryGetValue(gameObject, out Player player))
                return player;

            if (UnverifiedPlayers.TryGetValue(gameObject, out player))
                return player;

            if (ReferenceHub.TryGetHub(gameObject, out ReferenceHub hub))
                return new(hub);

            return null;
        }

        /// <summary>
        /// Gets the player belonging to the specified id.
        /// </summary>
        /// <param name="id">The player id.</param>
        /// <returns>Returns the player found or <see langword="null"/> if not found.</returns>
        public static Player Get(int id) => ReferenceHub.TryGetHub(id, out ReferenceHub referenceHub) ? Get(referenceHub) : null;

        /// <summary>
        /// Gets the <see cref="Player"/> by identifier.
        /// </summary>
        /// <param name="args">The player's nickname, ID, steamID64 or Discord ID.</param>
        /// <returns>Returns the player found or <see langword="null"/> if not found.</returns>
        public static Player Get(string args)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(args))
                    return null;

                if (UserIdsCache.TryGetValue(args, out Player playerFound) && playerFound.IsConnected)
                    return playerFound;

                if (int.TryParse(args, out int id))
                    return Get(id);

                if (args.EndsWith("@steam") || args.EndsWith("@discord") || args.EndsWith("@northwood") || args.EndsWith("@offline"))
                {
                    foreach (Player player in Dictionary.Values)
                    {
                        if (player.UserId == args)
                        {
                            playerFound = player;
                            break;
                        }
                    }
                }
                else
                {
                    int lastnameDifference = 31;
                    string firstString = args.ToLower();

                    foreach (Player player in Dictionary.Values)
                    {
                        if (!player.IsVerified || player.Nickname is null)
                            continue;

                        if (!player.Nickname.ToLower().Contains(args.ToLower()))
                            continue;

                        string secondString = player.Nickname;

                        int nameDifference = secondString.Length - firstString.Length;
                        if (nameDifference < lastnameDifference)
                        {
                            lastnameDifference = nameDifference;
                            playerFound = player;
                        }
                    }
                }

                if (playerFound is not null)
                    UserIdsCache[playerFound.UserId] = playerFound;

                return playerFound;
            }
            catch (Exception exception)
            {
                Log.Error($"{typeof(Player).FullName}.{nameof(Get)} error: {exception}");
                return null;
            }
        }

        /// <summary>
        /// Gets the <see cref="Player"/> from LabApi class.
        /// </summary>
        /// <param name="apiPlayer">The <see cref="LabApi.Features.Wrappers.Player"/> class.</param>
        /// <returns>A <see cref="Player"/> or <see langword="null"/> if not found.</returns>
        public static Player Get(LabApi.Features.Wrappers.Player apiPlayer) => Get(apiPlayer.ReferenceHub);

        /// <summary>
        /// Try-get a player given a <see cref="CommandSystem.ICommandSender"/>.
        /// </summary>
        /// <param name="sender">The <see cref="CommandSystem.ICommandSender"/>.</param>
        /// <param name="player">The player that matches the given <see cref="CommandSystem.ICommandSender"/>, or <see langword="null"/> if no player is found.</param>
        /// <returns>A boolean indicating whether a player was found.</returns>
        public static bool TryGet(CommandSystem.ICommandSender sender, out Player player) => (player = Get(sender)) is not null;

        /// <summary>
        /// Try-get a player given a <see cref="Footprinting.Footprint"/>.
        /// </summary>
        /// <param name="footprint">The <see cref="Footprinting.Footprint"/>.</param>
        /// <param name="player">The player that matches the given <see cref="Footprinting.Footprint"/>, or <see langword="null"/> if no player is found.</param>
        /// <returns>A boolean indicating whether a player was found.</returns>
        public static bool TryGet(Footprint footprint, out Player player) => (player = Get(footprint)) is not null;

        /// <summary>
        /// Try-get a player given a <see cref="CommandSender"/>.
        /// </summary>
        /// <param name="sender">The <see cref="CommandSender"/>.</param>
        /// <param name="player">The player that matches the given <see cref="CommandSender"/>, or <see langword="null"/> if no player is found.</param>
        /// <returns>A boolean indicating whether a player was found.</returns>
        public static bool TryGet(CommandSender sender, out Player player) => (player = Get(sender)) is not null;

        /// <summary>
        /// Try-get a player given a <see cref="global::ReferenceHub"/>.
        /// </summary>
        /// <param name="referenceHub">The <see cref="global::ReferenceHub"/>.</param>
        /// <param name="player">The player that matches the given <see cref="global::ReferenceHub"/>, or <see langword="null"/> if no player is found.</param>
        /// <returns>A boolean indicating whether a player was found.</returns>
        public static bool TryGet(ReferenceHub referenceHub, out Player player) => (player = Get(referenceHub)) is not null;

        /// <summary>
        /// Try-get a player given a network ID.
        /// </summary>
        /// <param name="netId">The network ID.</param>
        /// <param name="player">The player that matches the given net ID, or <see langword="null"/> if no player is found.</param>
        /// <returns>A boolean indicating whether a player was found.</returns>
        public static bool TryGet(uint netId, out Player player) => (player = Get(netId)) is not null;

        /// <summary>
        /// Try-get a player given a <see cref="Mirror.NetworkIdentity"/>.
        /// </summary>
        /// <param name="netIdentity">The <see cref="Mirror.NetworkIdentity"/>.</param>
        /// <param name="player">The player that matches the given <see cref="Mirror.NetworkIdentity"/>, or <see langword="null"/> if no player is found.</param>
        /// <returns>A boolean indicating whether a player was found.</returns>
        public static bool TryGet(NetworkIdentity netIdentity, out Player player) => (player = Get(netIdentity)) is not null;

        /// <summary>
        /// Try-get a player given a <see cref="NetworkConnection"/>.
        /// </summary>
        /// <param name="conn">The <see cref="NetworkConnection"/>.</param>
        /// <param name="player">The player that matches the given <see cref="NetworkConnection"/>, or <see langword="null"/> if no player is found.</param>
        /// <returns>A boolean indicating whether a player was found.</returns>
        public static bool TryGet(NetworkConnection conn, out Player player) => (player = Get(conn)) is not null;

        /// <summary>
        /// Try-get a player given a <see cref="UnityEngine.GameObject"/>.
        /// </summary>
        /// <param name="gameObject">The <see cref="UnityEngine.GameObject"/>.</param>
        /// <param name="player">The player that matches the given <see cref="UnityEngine.GameObject"/>, or <see langword="null"/> if no player is found.</param>
        /// <returns>A boolean indicating whether a player was found.</returns>
        public static bool TryGet(GameObject gameObject, out Player player) => (player = Get(gameObject)) is not null;

        /// <summary>
        /// Try-get a player given an ID.
        /// </summary>
        /// <param name="id">The user ID.</param>
        /// <param name="player">The player that matches the given ID, or <see langword="null"/> if no player is found.</param>
        /// <returns>A boolean indicating whether a player was found.</returns>
        public static bool TryGet(int id, out Player player) => (player = Get(id)) is not null;

        /// <summary>
        /// Try-get a player by identifier.
        /// </summary>
        /// <param name="args">The player's nickname, ID, steamID64 or Discord ID.</param>
        /// <param name="player">The player found or <see langword="null"/> if not found.</param>
        /// <returns>A boolean indicating whether a player was found.</returns>
        public static bool TryGet(string args, out Player player) => (player = Get(args)) is not null;

        /// <summary>
        /// Try-get the <see cref="Player"/> from LabApi class.
        /// </summary>
        /// <param name="apiPlayer">The <see cref="LabApi.Features.Wrappers.Player"/> class.</param>
        /// <param name="player">The player found or <see langword="null"/> if not found.</param>
        /// <returns>A boolean indicating whether a player was found.</returns>
        public static bool TryGet(LabApi.Features.Wrappers.Player apiPlayer, out Player player) => (player = Get(apiPlayer)) is not null;

        /// <summary>
        /// Try-get player by <see cref="Collider"/>.
        /// </summary>
        /// <param name="collider">The <see cref="Collider"/>.</param>
        /// <param name="player">The player found or <see langword="null"/> if not found.</param>
        /// <returns>A boolean indicating whether a player was found.</returns>
        public static bool TryGet(Collider collider, out Player player) => (player = Get(collider)) is not null;

        /// <summary>
        /// Gets an <see cref="IEnumerable{Player}"/> containing all players processed based on the arguments specified.
        /// </summary>
        /// <param name="args">The array segment of strings representing the input arguments to be processed.</param>
        /// <param name="startIndex">The starting index within the array segment.</param>
        /// <param name="newargs">Contains the updated arguments after processing.</param>
        /// <param name="keepEmptyEntries">Determines whether empty entries should be kept in the result.</param>
        /// <returns>An <see cref="IEnumerable{Player}"/> representing the processed players.</returns>
        public static IEnumerable<Player> GetProcessedData(ArraySegment<string> args, int startIndex, out string[] newargs, bool keepEmptyEntries = false) => RAUtils.ProcessPlayerIdOrNamesList(args, startIndex, out newargs, keepEmptyEntries).Select(hub => Get(hub));

        /// <summary>
        /// Gets an <see cref="IEnumerable{Player}"/> containing all players processed based on the arguments specified.
        /// </summary>
        /// <param name="args">The array segment of strings representing the input arguments to be processed.</param>
        /// <param name="startIndex">The starting index within the array segment.</param>
        /// <returns>An <see cref="IEnumerable{Player}"/> representing the processed players.</returns>
        public static IEnumerable<Player> GetProcessedData(ArraySegment<string> args, int startIndex = 0) => GetProcessedData(args, startIndex, out string[] _);

        /// <summary>
        /// Adds a player's UserId to the list of reserved slots.
        /// </summary>
        /// <param name="userId">The UserId of the player to add.</param>
        /// <param name="isPermanent"> Whether to add a <see langword="userId"/> permanently. It will write a <see langword="userId"/> to UserIDReservedSlots.txt file.</param>
        /// <returns><see langword="true"/> if the slot was successfully added, or <see langword="false"/> if the provided UserId already has a reserved slot.</returns>
        /// <seealso cref="GiveReservedSlot(bool)"/>
        public static bool AddReservedSlot(string userId, bool isPermanent)
        {
            if (isPermanent)
            {
                if (LabApi.Features.Wrappers.ReservedSlots.HasReservedSlot(userId))
                    return false;

                LabApi.Features.Wrappers.ReservedSlots.Add(userId);
                return true;
            }

            return ReservedSlot.Users.Add(userId);
        }

        /// <summary>
        /// Adds a player's UserId to the whitelist.
        /// </summary>
        /// <param name="userId">The UserId of the player to add.</param>
        /// <param name="isPermanent"> Whether to add a <see langword="userId"/> permanently. It will write a <see langword="userId"/> to UserIDWhitelist.txt file.</param>
        /// <returns><see langword="true"/> if the record was successfully added, or <see langword="false"/> if the provided UserId already is in whitelist.</returns>
        /// <seealso cref="GrantWhitelist(bool)"/>
        public static bool AddToWhitelist(string userId, bool isPermanent)
        {
            if (isPermanent)
            {
                if (WhiteList.IsOnWhitelist(userId))
                    return false;

                LabApi.Features.Wrappers.Whitelist.Add(userId);
                return true;
            }

            return WhiteList.Users.Add(userId);
        }

        /// <summary>
        /// Reloads the reserved slot list, clearing all reserved slot changes made with add/remove methods and reverting to the reserved slots files.
        /// </summary>
        public static void ReloadReservedSlots() => ReservedSlot.Reload();

        /// <summary>
        /// Reloads the whitelist, clearing all whitelist changes made with add/remove methods and reverting to the whitelist files.
        /// </summary>
        public static void ReloadWhitelist() => WhiteList.Reload();

        /// <summary>
        /// Adds a player's UserId to the list of reserved slots.
        /// </summary>
        /// <param name="isPermanent"> Whether to add a player's UserId permanently. It will write a player's UserId to UserIDReservedSlots.txt file.</param>
        /// <returns><see langword="true"/> if the slot was successfully added, or <see langword="false"/> if the provided UserId already has a reserved slot.</returns>
        /// <seealso cref="AddReservedSlot(string, bool)"/>
        public bool GiveReservedSlot(bool isPermanent) => AddReservedSlot(UserId, isPermanent);

        /// <summary>
        /// Adds a player's UserId to the whitelist.
        /// </summary>
        /// <param name="isPermanent"> Whether to add a player's UserId permanently. It will write a player's UserId to UserIDWhitelist.txt file.</param>
        /// <returns><see langword="true"/> if the record was successfully added, or <see langword="false"/> if the provided UserId already is in whitelist.</returns>
        /// <seealso cref="AddToWhitelist(string, bool)"/>
        public bool GrantWhitelist(bool isPermanent) => AddToWhitelist(UserId, isPermanent);

        /// <summary>
        /// Tries to add <see cref="RoleTypeId"/> to FriendlyFire rules.
        /// </summary>
        /// <param name="roleToAdd"> Role to add. </param>
        /// <param name="ffMult"> Friendly fire multiplier. </param>
        public void SetFriendlyFire(RoleTypeId roleToAdd, float ffMult)
        {
            if (FriendlyFireMultiplier.ContainsKey(roleToAdd))
                FriendlyFireMultiplier[roleToAdd] = ffMult;
            else
                FriendlyFireMultiplier.Add(roleToAdd, ffMult);
        }

        /// <summary>
        /// Wrapper to call <see cref="SetFriendlyFire(RoleTypeId, float)"/>.
        /// </summary>
        /// <param name="roleFF"> Role with FF to add even if it exists. </param>
        public void SetFriendlyFire(KeyValuePair<RoleTypeId, float> roleFF) => SetFriendlyFire(roleFF.Key, roleFF.Value);

        /// <summary>
        /// Tries to add <see cref="RoleTypeId"/> to FriendlyFire rules.
        /// </summary>
        /// <param name="roleToAdd"> Role to add. </param>
        /// <param name="ffMult"> Friendly fire multiplier. </param>
        /// <returns> Whether the item was able to be added. </returns>
        public bool TryAddFriendlyFire(RoleTypeId roleToAdd, float ffMult)
        {
            if (FriendlyFireMultiplier.ContainsKey(roleToAdd))
                return false;

            FriendlyFireMultiplier.Add(roleToAdd, ffMult);
            return true;
        }

        /// <summary>
        /// Tries to add <see cref="RoleTypeId"/> to FriendlyFire rules.
        /// </summary>
        /// <param name="pairedRoleFF"> Role FF multiplier to add. </param>
        /// <returns> Whether the item was able to be added. </returns>
        public bool TryAddFriendlyFire(KeyValuePair<RoleTypeId, float> pairedRoleFF) => TryAddFriendlyFire(pairedRoleFF.Key, pairedRoleFF.Value);

        /// <summary>
        /// Tries to add <see cref="RoleTypeId"/> to FriendlyFire rules.
        /// </summary>
        /// <param name="ffRules"> Roles to add with friendly fire values. </param>
        /// <param name="overwrite"> Whether to overwrite current values if they exist. </param>
        /// <returns> Whether the item was able to be added. </returns>
        public bool TryAddFriendlyFire(Dictionary<RoleTypeId, float> ffRules, bool overwrite = false)
        {
            Dictionary<RoleTypeId, float> temporaryFriendlyFireRules = DictionaryPool<RoleTypeId, float>.Pool.Get();
            foreach (KeyValuePair<RoleTypeId, float> roleFf in ffRules)
            {
                if (overwrite)
                {
                    SetFriendlyFire(roleFf);
                }
                else
                {
                    if (!FriendlyFireMultiplier.ContainsKey(roleFf.Key))
                        temporaryFriendlyFireRules.Add(roleFf.Key, roleFf.Value);
                    else
                        return false; // Contained Key but overwrite set to false so we do not add any.
                }
            }

            if (!overwrite)
            {
                foreach (KeyValuePair<RoleTypeId, float> roleFF in temporaryFriendlyFireRules)
                    TryAddFriendlyFire(roleFF);
            }

            DictionaryPool<RoleTypeId, float>.Pool.Return(temporaryFriendlyFireRules);
            return true;
        }

        /// <summary>
        /// Tries to add <see cref="RoleTypeId"/> to FriendlyFire rules.
        /// </summary>
        /// <param name="roleTypeId"> Role associated for CustomFF. </param>
        /// <param name="roleToAdd"> Role to add. </param>
        /// <param name="ffMult"> Friendly fire multiplier. </param>
        public void SetCustomRoleFriendlyFire(string roleTypeId, RoleTypeId roleToAdd, float ffMult)
        {
            if (CustomRoleFriendlyFireMultiplier.TryGetValue(roleTypeId, out Dictionary<RoleTypeId, float> currentPairedData))
            {
                if (!currentPairedData.ContainsKey(roleToAdd))
                {
                    currentPairedData.Add(roleToAdd, ffMult);
                    return;
                }

                currentPairedData[roleToAdd] = ffMult;
                return;
            }

            CustomRoleFriendlyFireMultiplier.Add(roleTypeId, new() { { roleToAdd, ffMult } });
        }

        /// <summary>
        /// Wrapper to call <see cref="SetCustomRoleFriendlyFire(string, RoleTypeId, float)"/>.
        /// </summary>
        /// <param name="roleTypeId"> Role associated for CustomFF. </param>
        /// <param name="roleFf"> Role with FF to add even if it exists. </param>
        public void SetCustomRoleFriendlyFire(string roleTypeId, KeyValuePair<RoleTypeId, float> roleFf) => SetCustomRoleFriendlyFire(roleTypeId, roleFf.Key, roleFf.Value);

        /// <summary>
        /// Tries to add <see cref="RoleTypeId"/> to FriendlyFire rules for CustomRole.
        /// </summary>
        /// <param name="roleTypeId"> Role associated for CustomFF. </param>
        /// <param name="roleFf"> Role to add and FF multiplier. </param>
        /// <returns> Whether the item was able to be added. </returns>
        public bool TryAddCustomRoleFriendlyFire(string roleTypeId, KeyValuePair<RoleTypeId, float> roleFf) => TryAddCustomRoleFriendlyFire(roleTypeId, roleFf.Key, roleFf.Value);

        /// <summary>
        /// Tries to add <see cref="RoleTypeId"/> to FriendlyFire rules for CustomRole.
        /// </summary>
        /// <param name="roleTypeId"> Role associated for CustomFF. </param>
        /// <param name="roleToAdd"> Role to add. </param>
        /// <param name="ffMult"> Friendly fire multiplier. </param>
        /// <returns> Whether the item was able to be added. </returns>
        public bool TryAddCustomRoleFriendlyFire(string roleTypeId, RoleTypeId roleToAdd, float ffMult)
        {
            if (CustomRoleFriendlyFireMultiplier.TryGetValue(roleTypeId, out Dictionary<RoleTypeId, float> currentPairedData))
            {
                if (currentPairedData.ContainsKey(roleToAdd))
                    return false;

                currentPairedData.Add(roleToAdd, ffMult);
            }
            else
            {
                SetCustomRoleFriendlyFire(roleTypeId, roleToAdd, ffMult);
            }

            return true;
        }

        /// <summary>
        /// Tries to add <see cref="RoleTypeId"/> to FriendlyFire rules.
        /// </summary>
        /// <param name="customRoleName"> Role associated for CustomFF. </param>
        /// <param name="ffRules"> Roles to add with friendly fire values. </param>
        /// <param name="overwrite"> Whether to overwrite current values if they exist - does NOT delete previous entries if they are not in provided rules. </param>
        /// <returns> Whether the item was able to be added. </returns>
        public bool TryAddCustomRoleFriendlyFire(string customRoleName, Dictionary<RoleTypeId, float> ffRules, bool overwrite = false)
        {
            Dictionary<RoleTypeId, float> temporaryFriendlyFireRules = DictionaryPool<RoleTypeId, float>.Pool.Get();

            if (CustomRoleFriendlyFireMultiplier.TryGetValue(customRoleName, out Dictionary<RoleTypeId, float> pairedRoleFF))
            {
                foreach (KeyValuePair<RoleTypeId, float> roleFF in ffRules)
                {
                    if (overwrite)
                    {
                        SetCustomRoleFriendlyFire(customRoleName, roleFF);
                    }
                    else
                    {
                        if (!pairedRoleFF.ContainsKey(roleFF.Key))
                            temporaryFriendlyFireRules.Add(roleFF.Key, roleFF.Value);
                        else
                            return false; // Contained Key but overwrite set to false so we do not add any.
                    }
                }

                if (!overwrite)
                {
                    foreach (KeyValuePair<RoleTypeId, float> roleFf in temporaryFriendlyFireRules)
                        TryAddCustomRoleFriendlyFire(customRoleName, roleFf);
                }
            }
            else
            {
                foreach (KeyValuePair<RoleTypeId, float> roleFf in ffRules)
                    SetCustomRoleFriendlyFire(customRoleName, roleFf);
            }

            DictionaryPool<RoleTypeId, float>.Pool.Return(temporaryFriendlyFireRules);
            return true;
        }

        /// <summary>
        /// Adds the Custom role to the <see cref="CustomRoleFriendlyFireMultiplier"/> if they did not already exist.
        /// </summary>
        /// <param name="customRoleFriendlyFireMultiplier"> Custom role with FF role rules. </param>
        public void TryAddCustomRoleFriendlyFire(Dictionary<string, Dictionary<RoleTypeId, float>> customRoleFriendlyFireMultiplier)
        {
            foreach (KeyValuePair<string, Dictionary<RoleTypeId, float>> newRolesWithFf in customRoleFriendlyFireMultiplier)
                TryAddCustomRoleFriendlyFire(newRolesWithFf.Key, newRolesWithFf.Value);
        }

        /// <summary>
        /// Sets the <see cref="CustomRoleFriendlyFireMultiplier"/>.
        /// </summary>
        /// <param name="customRoleFriendlyFireMultiplier"> New rules for CustomeRoleFriendlyFireMultiplier to set to. </param>
        public void TrySetCustomRoleFriendlyFire(Dictionary<string, Dictionary<RoleTypeId, float>> customRoleFriendlyFireMultiplier)
            => CustomRoleFriendlyFireMultiplier = customRoleFriendlyFireMultiplier;

        /// <summary>
        /// Sets the <see cref="CustomRoleFriendlyFireMultiplier"/>.
        /// </summary>
        /// <param name="roleTypeId"> Role to associate FF rules to. </param>
        /// <param name="customRoleFriendlyFireMultiplier"> New rules for CustomeRoleFriendlyFireMultiplier to set to. </param>
        public void TrySetCustomRoleFriendlyFire(string roleTypeId, Dictionary<RoleTypeId, float> customRoleFriendlyFireMultiplier) =>
            CustomRoleFriendlyFireMultiplier[roleTypeId] = customRoleFriendlyFireMultiplier;

        /// <summary>
        /// Tries to remove <see cref="RoleTypeId"/> from FriendlyFire rules.
        /// </summary>
        /// <param name="role"> Role to add. </param>
        /// <returns> Whether the item was able to be added. </returns>
        public bool TryRemoveFriendlyFire(RoleTypeId role) => FriendlyFireMultiplier.Remove(role);

        /// <summary>
        /// Tries to remove <see cref="RoleTypeId"/> from FriendlyFire rules.
        /// </summary>
        /// <param name="role"> Role to add. </param>
        /// <returns> Whether the item was able to be added. </returns>
        public bool TryRemoveCustomeRoleFriendlyFire(string role) => CustomRoleFriendlyFireMultiplier.Remove(role);

        /// <summary>
        /// Forces the player to reload their current weapon.
        /// </summary>
        /// <returns><see langword="true"/> if firearm was successfully reloaded. Otherwise, <see langword="false"/>.</returns>
        public bool ReloadWeapon()
        {
            if (CurrentItem is Firearm firearm)
            {
                // TODO not finish
                /*
                bool result = firearm.Base.Ammo.ServerTryReload();
                Connection.Send(new RequestMessage(firearm.Serial, RequestType.Reload));
                return result;
                */
            }

            return false;
        }

        /// <summary>
        /// Tries to get an item from a player's inventory.
        /// </summary>
        /// <param name="serial">The unique identifier of the item.</param>
        /// <param name="item">The <see cref="Item"/> found. <see langword="null"/> if it doesn't exist.</param>
        /// <returns><see langword="true"/> if the item is found, <see langword="false"/> otherwise.</returns>
        public bool TryGetItem(ushort serial, out Item item)
        {
            item = Inventory.UserInventory.Items.TryGetValue(serial, out ItemBase itemBase) ? Item.Get(itemBase) : null;

            return item != null;
        }

        /// <summary>
        /// Sets the player's rank.
        /// </summary>
        /// <param name="name">The rank name to be set.</param>
        /// <param name="group">The group to be set.</param>
        public void SetRank(string name, UserGroup group)
        {
            if (ServerStatic.PermissionsHandler.Groups.TryGetValue(name, out UserGroup userGroup))
            {
                userGroup.BadgeColor = group.BadgeColor;
                userGroup.BadgeText = name;
                userGroup.HiddenByDefault = !group.Cover;
                userGroup.Cover = group.Cover;

                ReferenceHub.serverRoles.SetGroup(userGroup, false, false);
            }
            else
            {
                ServerStatic.PermissionsHandler.Groups.Add(name, group);

                ReferenceHub.serverRoles.SetGroup(group, false, false);
            }

            if (ServerStatic.PermissionsHandler.Members.ContainsKey(UserId))
                ServerStatic.PermissionsHandler.Members[UserId] = name;
            else
                ServerStatic.PermissionsHandler.Members.Add(UserId, name);
        }

        /// <summary>
        /// Handcuff the player as administrator.
        /// </summary>
        public void Handcuff()
        {
            ReferenceHub.inventory.SetDisarmedStatus(null);

            DisarmedPlayers.Entries.Add(new DisarmedPlayers.DisarmedEntry(referenceHub.networkIdentity.netId, 0U));
            new DisarmedPlayersListMessage(DisarmedPlayers.Entries).SendToAuthenticated(0);
        }

        /// <summary>
        /// Handcuff the player.
        /// </summary>
        /// <param name="cuffer">The cuffer player.</param>
        public void Handcuff(Player cuffer)
        {
            if (cuffer is not null && !IsCuffed && (cuffer.Position - Position).sqrMagnitude <= DisarmingHandlers.ServerDisarmingDistanceSqrt)
                Cuffer = cuffer;
        }

        /// <summary>
        /// Removes the player's handcuffs.
        /// </summary>
        public void RemoveHandcuffs()
        {
            Inventory.SetDisarmedStatus(null);
            new DisarmedPlayersListMessage(DisarmedPlayers.Entries).SendToAuthenticated();
        }

        /// <summary>
        /// Broadcasts the given <see cref="Features.Broadcast"/> to the player.
        /// </summary>
        /// <param name="broadcast">The <see cref="Features.Broadcast"/> to be broadcasted.</param>
        /// <param name="shouldClearPrevious">Clears all player's broadcasts before sending the new one.</param>
        public void Broadcast(Broadcast broadcast, bool shouldClearPrevious = false)
        {
            if (broadcast.Show)
                Broadcast(broadcast.Duration, broadcast.Content, broadcast.Type, shouldClearPrevious);
        }

        /// <summary>
        /// Drops an item from the player's inventory.
        /// </summary>
        /// <param name="item">The <see cref="Item"/> to be dropped.</param>
        /// <param name="isThrown">Is the item Thrown?.</param>
        public void DropItem(Item item, bool isThrown = false)
        {
            if (item is null)
                return;
            Inventory.UserCode_CmdDropItem__UInt16__Boolean(item.Serial, isThrown);
        }

        /// <summary>
        /// Drops an item from the player's inventory.
        /// </summary>
        /// <param name="item">The <see cref="Item"/> to be dropped.</param>
        /// <returns>dropped <see cref="Pickup"/>.</returns>
        public Pickup DropItem(Item item) => item is not null ? Pickup.Get(Inventory.ServerDropItem(item.Serial)) : null;

        /// <summary>
        /// Drops the held item. Will not do anything if the player is not holding an item.
        /// </summary>
        /// <param name="isThrown">Is the item Thrown?.</param>
        public void DropHeldItem(bool isThrown = false) => DropItem(CurrentItem, isThrown);

        /// <summary>
        /// Drops the held item. Will not do anything if the player is not holding an item.
        /// </summary>
        /// <seealso cref="CurrentItem"/>
        /// <returns>Dropped item's <see cref="Pickup"/>.</returns>
        public Pickup DropHeldItem()
        {
            if (CurrentItem is null)
                return null;

            return DropItem(CurrentItem);
        }

        /// <summary>
        /// Indicates whether the player has an item.
        /// </summary>
        /// <param name="item">The item to search for.</param>
        /// <returns><see langword="true"/>, if the player has it; otherwise, <see langword="false"/>.</returns>
        public bool HasItem(Item item) => Items.Contains(item);

        /// <summary>
        /// Indicates whether the player has an item type.
        /// </summary>
        /// <param name="type">The type to search for.</param>
        /// <returns><see langword="true"/>, if the player has it; otherwise, <see langword="false"/>.</returns>
        public bool HasItem(ItemType type) => Items.Any(tempItem => tempItem.Type == type);

        /// <summary>
        /// Counts how many items of a certain <see cref="ItemType"/> a player has.
        /// </summary>
        /// <param name="item">The item to search for.</param>
        /// <returns>How many items of that <see cref="ItemType"/> the player has.</returns>
        /// <remarks>For counting ammo, see <see cref="GetAmmo(AmmoType)"/>.</remarks>
        /// <seealso cref="GetAmmo(AmmoType)"/>
        /// <seealso cref="CountItem(ItemCategory)"/>
        public int CountItem(ItemType item) => Items.Count(tempItem => tempItem.Type == item);

        /// <summary>
        /// Counts how many items of a certain <see cref="ProjectileType"/> a player has.
        /// </summary>
        /// <param name="grenadeType">The ProjectileType to search for.</param>
        /// <returns>How many items of that <see cref="ProjectileType"/> the player has.</returns>
        /// <seealso cref="CountItem(ItemType)"/>
        public int CountItem(ProjectileType grenadeType) => Inventory.UserInventory.Items.Count(tempItem => tempItem.Value.ItemTypeId == grenadeType.GetItemType());

        /// <summary>
        /// Counts how many items of a certain <see cref="ItemCategory"/> a player has.
        /// </summary>
        /// <param name="category">The category to search for.</param>
        /// <returns>How many items of that <see cref="ItemCategory"/> the player has.</returns>
        /// <seealso cref="CountItem(ItemType)"/>
        public int CountItem(ItemCategory category) => category switch
        {
            ItemCategory.Ammo => Inventory.UserInventory.ReserveAmmo.Count(ammo => ammo.Value > 0),
            _ => Inventory.UserInventory.Items.Count(tempItem => tempItem.Value.Category == category),
        };

        /// <summary>
        /// Removes an <see cref="Item"/> from the player's inventory.
        /// </summary>
        /// <param name="item">The <see cref="Item"/> to remove.</param>
        /// <param name="destroy">Whether to destroy the item.</param>
        /// <returns>A value indicating whether the <see cref="Item"/> was removed.</returns>
        public bool RemoveItem(Item item, bool destroy = true)
        {
            if (!ItemsValue.Contains(item))
                return false;

            if (!Inventory.UserInventory.Items.ContainsKey(item.Serial))
            {
                ItemsValue.Remove(item);
                return false;
            }

            if (destroy)
            {
                Inventory.ServerRemoveItem(item.Serial, null);
            }
            else
            {
                item.ChangeOwner(this, Server.Host);

                if (CurrentItem == item)
                {
                    Inventory.CurInstance = null;
                }

                if (item.Serial == Inventory.CurItem.SerialNumber)
                    Inventory.NetworkCurItem = ItemIdentifier.None;

                ItemsValue.Remove(item);
                Inventory.UserInventory.Items.Remove(item.Serial);

                Inventory.SendItemsNextFrame = true;
            }

            return true;
        }

        /// <summary>
        /// Removes an <see cref="Item"/> from the player's inventory.
        /// </summary>
        /// <param name="serial">The <see cref="Item"/> serial to remove.</param>
        /// <param name="destroy">Whether to destroy the item.</param>
        /// <returns>A value indicating whether the <see cref="Item"/> was removed.</returns>
        public bool RemoveItem(ushort serial, bool destroy = true)
        {
            if (Items.SingleOrDefault(item => item.Serial == serial) is not Item item)
                return false;
            return RemoveItem(item, destroy);
        }

        /// <summary>
        /// Removes all <see cref="Item"/>'s that satisfy the condition from the player's inventory.
        /// </summary>
        /// <param name="predicate">The condition to satisfy.</param>
        /// <param name="destroy">Whether to destroy the items.</param>
        /// <returns>Count of a successfully removed <see cref="Item"/>'s.</returns>
        public int RemoveItem(Func<Item, bool> predicate, bool destroy = true)
        {
            List<Item> enumeratedItems = ListPool<Item>.Pool.Get(ItemsValue);
            int count = 0;

            foreach (Item item in enumeratedItems)
            {
                if (predicate(item) && RemoveItem(item, destroy))
                    ++count;
            }

            ListPool<Item>.Pool.Return(enumeratedItems);
            return count;
        }

        /// <summary>
        /// Removes the held <see cref="ItemBase"/> from the player's inventory.
        /// </summary>
        /// <param name="destroy">Whether to destroy the item.</param>
        /// <returns>Returns a value indicating whether the <see cref="ItemBase"/> was removed.</returns>
        public bool RemoveHeldItem(bool destroy = true) => RemoveItem(CurrentItem, destroy);

        /// <summary>
        /// Sends a console message to the player's console.
        /// </summary>
        /// <param name="message">The message to be sent.</param>
        /// <param name="color">The message color.</param>
        public void SendConsoleMessage(string message, string color) => referenceHub.gameConsoleTransmission.SendToClient(message, color);

        /// <summary>
        /// Disconnects the player.
        /// </summary>
        /// <param name="reason">The disconnection reason.</param>
        public void Disconnect(string reason = null) =>
            ServerConsole.Disconnect(GameObject, string.IsNullOrEmpty(reason) ? string.Empty : reason);

        /// <summary>
        /// Resets the <see cref="Player"/>'s stamina.
        /// </summary>
        public void ResetStamina() => Stamina = StaminaStat.MaxValue;

        /// <summary>
        /// Sets the scale of a player on the server side.
        /// </summary>
        /// <param name="scale">The scale to set.</param>
        public void SetScale(Vector3 scale)
        {
            try
            {
                if (Role is not FpcRole fpcRole)
                {
                    Log.Error($"{nameof(SetScale)} error: {nameof(Role)} {Role} is not a {nameof(FpcRole)}, cannot set scale.");
                    return;
                }

                fpcRole.FirstPersonController.FpcModule.Motor.ScaleController.Scale = scale;
            }
            catch (Exception exception)
            {
                Log.Error($"{nameof(SetScale)} error: {exception}");
            }
        }

        /// <summary>
        /// Sets the scale of a player on the server side.
        /// </summary>
        /// <param name="scale">The scale to set.</param>
        /// <param name="viewers">Who should see the updated scale.</param>
        public void SetScale(Vector3 scale, IEnumerable<Player> viewers) => SetScale(scale, viewers.Select(p => p.ReferenceHub));

        /// <summary>
        /// Sets the scale of a player on the server side.
        /// </summary>
        /// <param name="scale">The scale to set.</param>
        /// <param name="viewers">Who should see the updated scale.</param>
        public void SetScale(Vector3 scale, IEnumerable<ReferenceHub> viewers)
        {
            try
            {
                if (Role is not FpcRole fpcRole)
                {
                    Log.Error($"{nameof(SetScale)} error: {nameof(Role)} {Role} is not a {nameof(FpcRole)}, cannot set scale.");
                    return;
                }

                if (fpcRole.FirstPersonController.FpcModule.Motor.ScaleController.Scale == scale)
                    return;

                fpcRole.FirstPersonController.FpcModule.Motor.ScaleController._scale = scale;
                ReferenceHub.transform.localScale = scale;
                new SyncedScaleMessages.ScaleMessage(scale, ReferenceHub).SendToHubsConditionally(h => viewers.Contains(h));
            }
            catch (Exception exception)
            {
                Log.Error($"{nameof(SetScale)} error: {exception}");
            }
        }

        /// <summary>
        /// Sets the scale of the player for other players.
        /// </summary>
        /// <param name="fakeScale">The scale to set to.</param>
        /// <param name="viewers">Who should see the fake scale.</param>
        public void SetFakeScale(Vector3 fakeScale, IEnumerable<Player> viewers) => SetFakeScale(fakeScale, viewers.Select(p => p.ReferenceHub));

        /// <summary>
        /// Sets the scale of the player for other players.
        /// </summary>
        /// <param name="fakeScale">The scale to set to.</param>
        /// <param name="viewers">Who should see the fake scale.</param>
        public void SetFakeScale(Vector3 fakeScale, IEnumerable<ReferenceHub> viewers)
        {
            try
            {
                if (Role is not FpcRole fpcRole)
                {
                    Log.Error($"{nameof(SetFakeScale)} error: {nameof(Role)} {Role} is not a {nameof(FpcRole)}, cannot set fake scale.");
                    return;
                }

                new SyncedScaleMessages.ScaleMessage(fakeScale, ReferenceHub).SendToHubsConditionally(h => viewers.Contains(h));
            }
            catch (Exception exception)
            {
                Log.Error($"{nameof(SetFakeScale)} error: {exception}");
            }
        }

        /// <summary>
        /// Gets a user's SCP preference.
        /// </summary>
        /// <param name="roleType">The SCP RoleType.</param>
        /// <returns>A value from <c>-5</c> to <c>5</c>, representing a player's preference to play as the provided SCP. Will return <c>0</c> for invalid SCPs.</returns>
        public int GetScpPreference(RoleTypeId roleType)
        {
            if (ScpPreferences.Preferences.TryGetValue(roleType, out int value))
                return value;

            return 0;
        }

        /// <summary>
        /// Hurts the player.
        /// </summary>
        /// <param name="damageHandlerBase">The <see cref="DamageHandlerBase"/> used to deal damage.</param>
        public void Hurt(DamageHandlerBase damageHandlerBase) => ReferenceHub.playerStats.DealDamage(damageHandlerBase);

        /// <summary>
        /// Hurts the player.
        /// </summary>
        /// <param name="attacker">The <see cref="Player"/> attacking player.</param>
        /// <param name="amount">The <see langword="float"/> amount of damage to deal.</param>
        /// <param name="damageType">The <see cref="DamageType"/> of the damage dealt.</param>
        /// <param name="cassieAnnouncement">The <see cref="CassieAnnouncement"/> cassie announcement to make if the damage kills the player.</param>
        public void Hurt(Player attacker, float amount, DamageType damageType = DamageType.Unknown, CassieAnnouncement cassieAnnouncement = null) =>
            Hurt(new GenericDamageHandler(this, attacker, amount, damageType, cassieAnnouncement));

        /// <summary>
        /// Hurts the player.
        /// </summary>
        /// <param name="attacker">The <see cref="Player"/> attacking player.</param>
        /// <param name="amount">The <see langword="float"/> amount of damage to deal.</param>
        /// <param name="damageType">The <see cref="DamageType"/> of the damage dealt.</param>
        /// <param name="cassieAnnouncement">The <see cref="CassieAnnouncement"/> cassie announcement to make if the damage kills the player.</param>
        /// <param name="deathText"> The <see langword="string"/> death text to appear on <see cref="Player"/> screen. </param>
        public void Hurt(Player attacker, float amount, DamageType damageType = DamageType.Unknown, CassieAnnouncement cassieAnnouncement = null, string deathText = null) =>
            Hurt(new GenericDamageHandler(this, attacker, amount, damageType, cassieAnnouncement, deathText));

        /// <summary>
        /// Hurts the player.
        /// </summary>
        /// <param name="attacker">The <see cref="Player"/> attacking player.</param>
        /// <param name="damage">The <see langword="float"/> amount of damage to deal.</param>
        /// <param name="force">The throw force.</param>
        /// <param name="armorPenetration">The armor penetration amount.</param>
        public void Hurt(Player attacker, float damage, Vector3 force = default, int armorPenetration = 0) =>
            Hurt(new ExplosionDamageHandler(attacker.Footprint, force, damage, armorPenetration, ExplosionType.Grenade));

        /// <summary>
        /// Hurts the player.
        /// </summary>
        /// <param name="amount">The <see langword="float"/> amount of damage to deal.</param>
        /// <param name="damageType">The <see cref="DamageType"/> of the damage dealt.</param>
        /// <param name="cassieAnnouncement">The <see langword="string"/> cassie announcement to make if the damage kills the player.</param>
        public void Hurt(float amount, DamageType damageType = DamageType.Unknown, string cassieAnnouncement = "") =>
            Hurt(new CustomReasonDamageHandler(DamageTypeExtensions.TranslationConversion.FirstOrDefault(k => k.Value == damageType).Key.LogLabel, amount, cassieAnnouncement));

        /// <summary>
        /// Hurts the player.
        /// </summary>
        /// <param name="damage">The amount of damage to deal.</param>
        /// <param name="damageReason"> The reason for the damage being dealt.</param>
        /// <param name="cassieAnnouncement">The cassie announcement to make.</param>
        public void Hurt(float damage, string damageReason, string cassieAnnouncement = "") =>
            Hurt(new CustomReasonDamageHandler(damageReason, damage, cassieAnnouncement));

        /// <summary>
        /// Heals the player.
        /// </summary>
        /// <param name="amount">The amount of health to heal.</param>
        /// <param name="overrideMaxHealth">Whether healing should exceed their max health.</param>
        public void Heal(float amount, bool overrideMaxHealth = false)
        {
            if (!overrideMaxHealth)
                ReferenceHub.playerStats.GetModule<HealthStat>().ServerHeal(amount);
            else
                Health += amount;
        }

        /// <summary>
        /// Forces the player to use an item.
        /// </summary>
        /// <param name="usableItem">The ItemType to be used.</param>
        /// <returns><see langword="true"/> if item was used successfully. Otherwise, <see langword="false"/>.</returns>
        public bool UseItem(ItemType usableItem) => UseItem(Item.Create(usableItem));

        /// <summary>
        /// Forces the player to use an item.
        /// </summary>
        /// <param name="usable">The item to be used.</param>
        public void UseItem(Usable usable) => usable?.Use(this);

        /// <summary>
        /// Forces the player to use an item.
        /// </summary>
        /// <param name="item">The item to be used.</param>
        /// <returns><see langword="true"/> if item was used successfully. Otherwise, <see langword="false"/>.</returns>
        public bool UseItem(Item item)
        {
            if (item is not Usable usableItem)
                return false;

            UseItem(usableItem);
            return true;
        }

        /// <summary>
        /// Kills the player.
        /// </summary>
        /// <param name="damageHandlerBase">The <see cref="DamageHandlerBase"/>.</param>
        public void Kill(DamageHandlerBase damageHandlerBase) => ReferenceHub.playerStats.KillPlayer(damageHandlerBase);

        /// <summary>
        /// Kills the player.
        /// </summary>
        /// <param name="damageType">The <see cref="DamageType"/> the player has been killed.</param>
        /// <param name="cassieAnnouncement">The cassie announcement to make upon death.</param>
        public void Kill(DamageType damageType, string cassieAnnouncement = "")
        {
            if ((Role.Side != Side.Scp) && !string.IsNullOrEmpty(cassieAnnouncement))
                Cassie.Message(cassieAnnouncement);

            ReferenceHub.playerStats.KillPlayer(new CustomReasonDamageHandler(DamageTypeExtensions.TranslationConversion.FirstOrDefault(k => k.Value == damageType).Key.LogLabel, -1, cassieAnnouncement));
        }

        /// <summary>
        /// Kills the player.
        /// </summary>
        /// <param name="deathReason">The reason the player has been killed.</param>
        /// <param name="cassieAnnouncement">The cassie announcement to make upon death.</param>
        public void Kill(string deathReason, string cassieAnnouncement = "")
        {
            if ((Role.Side != Side.Scp) && !string.IsNullOrEmpty(cassieAnnouncement))
                Cassie.Message(cassieAnnouncement);

            ReferenceHub.playerStats.KillPlayer(new CustomReasonDamageHandler(deathReason, -1, cassieAnnouncement));
        }

        /// <summary>
        /// Kills the player and vaporizes the body.
        /// </summary>
        /// <param name="attacker">The <see cref="Player"/> attacking player.</param>
        /// <param name="cassieAnnouncement">The cassie announcement to make upon death.</param>
        public void Vaporize(Player attacker = null, string cassieAnnouncement = "")
        {
            if ((Role.Side != Side.Scp) && !string.IsNullOrEmpty(cassieAnnouncement))
                Cassie.Message(cassieAnnouncement);

            Kill(new DisruptorDamageHandler(new DisruptorShotEvent(Item.Create(ItemType.ParticleDisruptor, attacker).Base as InventorySystem.Items.Firearms.Firearm, DisruptorActionModule.FiringState.FiringSingle), Vector3.up, -1));
        }

        /// <summary>
        /// Bans the player.
        /// </summary>
        /// <param name="duration">The ban duration, in seconds.</param>
        /// <param name="reason">The ban reason.</param>
        /// <param name="issuer">The ban issuer.</param>
        public void Ban(int duration, string reason, Player issuer = null)
            => BanPlayer.BanUser(ReferenceHub, issuer is null || issuer.ReferenceHub == null ? Server.Host.ReferenceHub : issuer.ReferenceHub, reason, duration);

        /// <summary>
        /// Bans the player.
        /// </summary>
        /// <param name="duration">The length of time to ban.</param>
        /// <param name="reason">The ban reason.</param>
        /// <param name="issuer">The ban issuer.</param>
        public void Ban(TimeSpan duration, string reason, Player issuer = null) => Ban((int)duration.TotalSeconds, reason, issuer);

        /// <summary>
        /// Kicks the player.
        /// </summary>
        /// <param name="reason">The kick reason.</param>
        /// <param name="issuer">The kick issuer.</param>
        public void Kick(string reason, Player issuer = null) => Ban(0, reason, issuer);

        /// <summary>
        /// Persistently mutes the player. For temporary mutes, see <see cref="IsMuted"/> and <see cref="IsIntercomMuted"/>.
        /// </summary>
        /// <param name="isIntercom">Whether this mute is for the intercom only.</param>
        public void Mute(bool isIntercom = false) => VoiceChatMutes.IssueLocalMute(UserId, isIntercom);

        /// <summary>
        /// Revokes a persistent mute. For temporary mutes, see <see cref="IsMuted"/> and <see cref="IsIntercomMuted"/>.
        /// </summary>
        /// <param name="isIntercom">Whether this un-mute is for the intercom only.</param>
        public void UnMute(bool isIntercom = false) => VoiceChatMutes.RevokeLocalMute(UserId, isIntercom);

        /// <summary>
        /// Blink the player's tag.
        /// </summary>
        /// <returns>Used to wait.</returns>
        public IEnumerator<float> BlinkTag()
        {
            yield return Timing.WaitForOneFrame;

            BadgeHidden = !BadgeHidden;

            yield return Timing.WaitForOneFrame;

            BadgeHidden = !BadgeHidden;
        }

        /// <summary>
        /// Sends a message to the player's Remote Admin console.
        /// </summary>
        /// <param name="message">The message to be sent.</param>
        /// <param name="success">Indicates whether the message should be highlighted as success.</param>
        /// <param name="pluginName">The plugin name.</param>
        public void RemoteAdminMessage(string message, bool success = true, string pluginName = null)
        {
            Sender.RaReply((pluginName ?? Assembly.GetCallingAssembly().GetName().Name) + "#" + message, success, true, string.Empty);
        }

        /// <summary>
        /// Sends a message to the player's Remote Admin Chat.
        /// </summary>
        /// <param name="message">The message to be sent.</param>
        /// <param name="channel">Indicates whether the message should be highlighted as success.</param>
        /// <returns><see langword="true"/> if message was send; otherwise, <see langword="false"/>.</returns>
        public bool SendStaffMessage(string message, EncryptedChannelManager.EncryptedChannel channel = EncryptedChannelManager.EncryptedChannel.AdminChat)
        {
            return ReferenceHub.encryptedChannelManager.TrySendMessageToClient(NetId + "!" + message, channel);
        }

        /// <summary>
        /// Sends a message to the player's Remote Admin Chat.
        /// </summary>
        /// <param name="message">The message to be sent.</param>
        /// <param name="channel">Indicates whether the message should be highlighted as success.</param>
        /// <returns><see langword="true"/> if message was send; otherwise, <see langword="false"/>.</returns>
        public bool SendStaffPing(string message, EncryptedChannelManager.EncryptedChannel channel = EncryptedChannelManager.EncryptedChannel.AdminChat)
        {
            return ReferenceHub.encryptedChannelManager.TrySendMessageToClient("0!" + message, channel);
        }

        /// <summary>
        /// Shows a broadcast to the player. Doesn't get logged to the console and can be monospaced.
        /// </summary>
        /// <param name="duration">The broadcast duration.</param>
        /// <param name="message">The message to be broadcasted.</param>
        /// <param name="type">The broadcast type.</param>
        /// <param name="shouldClearPrevious">Clears all player's broadcasts before sending the new one.</param>
        public void Broadcast(ushort duration, string message, global::Broadcast.BroadcastFlags type = global::Broadcast.BroadcastFlags.Normal, bool shouldClearPrevious = false)
        {
            if (shouldClearPrevious)
                ClearBroadcasts();

            Server.Broadcast.TargetAddElement(Connection, message, duration, type);
        }

        /// <summary>
        /// Clears the player's brodcast. Doesn't get logged to the console.
        /// </summary>
        public void ClearBroadcasts() => Server.Broadcast.TargetClearElements(Connection);

        /// <summary>
        /// Adds the amount of a specified <see cref="AmmoType">ammo type</see> to the player's inventory.
        /// </summary>
        /// <param name="ammoType">The <see cref="AmmoType"/> to be added.</param>
        /// <param name="amount">The amount of ammo to be added.</param>
        public void AddAmmo(AmmoType ammoType, ushort amount) =>
            Inventory.ServerAddAmmo(ammoType.GetItemType(), amount);

        /// <summary>
        /// Adds the amount of a specified ammo to player's inventory.
        /// </summary>
        /// <param name="ammo">A dictionary of ItemType and ushort of ammo and amount.</param>
        public void AddAmmo(Dictionary<ItemType, ushort> ammo)
        {
            foreach (KeyValuePair<ItemType, ushort> kvp in ammo)
                AddAmmo(kvp.Key.GetAmmoType(), kvp.Value);
        }

        /// <summary>
        /// Adds the amount of a specified <see cref="AmmoType">ammo type</see> to player's inventory.
        /// </summary>
        /// <param name="ammoBag">A dictionary of <see cref="AmmoType">ammo types</see> that will be added.</param>
        public void AddAmmo(Dictionary<AmmoType, ushort> ammoBag)
        {
            foreach (KeyValuePair<AmmoType, ushort> kvp in ammoBag)
                AddAmmo(kvp.Key, kvp.Value);
        }

        /// <summary>
        /// Adds the amount of a weapon's <see cref="AmmoType">ammo type</see> to the player's inventory.
        /// </summary>
        /// <param name="weaponType">The <see cref="ItemType"/> of the weapon.</param>
        /// <param name="amount">The amount of ammo to be added.</param>
        public void AddAmmo(FirearmType weaponType, ushort amount) => AddAmmo(weaponType.GetWeaponAmmoType(), amount);

        /// <summary>
        /// Sets the amount of a specified <see cref="AmmoType">ammo type</see> to the player's inventory.
        /// </summary>
        /// <param name="ammoType">The <see cref="AmmoType"/> to be set.</param>
        /// <param name="amount">The amount of ammo to be set.</param>
        public void SetAmmo(AmmoType ammoType, ushort amount)
        {
            ItemType itemType = ammoType.GetItemType();
            if (itemType is not ItemType.None)
                Inventory.ServerSetAmmo(itemType, amount);
        }

        /// <summary>
        /// Sets the amount of a specified <see cref="AmmoType">ammo type</see> to player's inventory.
        /// </summary>
        /// <param name="ammoBag">A dictionary of <see cref="AmmoType">ammo types</see> that will be added.</param>
        public void SetAmmo(Dictionary<AmmoType, ushort> ammoBag)
        {
            foreach (KeyValuePair<AmmoType, ushort> kvp in ammoBag)
                SetAmmo(kvp.Key, kvp.Value);
        }

        /// <summary>
        /// Gets the ammo count of a specified <see cref="AmmoType">ammo type</see> in a player's inventory.
        /// </summary>
        /// <param name="ammoType">The <see cref="AmmoType"/> to be searched for in the player's inventory.</param>
        /// <returns>The specified <see cref="AmmoType">ammo</see> count.</returns>
        public ushort GetAmmo(AmmoType ammoType) => Inventory.GetCurAmmo(ammoType.GetItemType());

        /// <summary>
        /// Drops a specific <see cref="AmmoType"/> out of the player's inventory.
        /// </summary>
        /// <param name="ammoType">The <see cref="AmmoType"/> that will be dropped.</param>
        /// <param name="amount">The amount of ammo that will be dropped.</param>
        /// <param name="checkMinimals">Whether ammo limits will be taken into consideration.</param>
        /// <returns><see langword="true"/> if ammo was dropped; otherwise, <see langword="false"/>.</returns>
        public bool DropAmmo(AmmoType ammoType, ushort amount, bool checkMinimals = false) =>
            Inventory.ServerDropAmmo(ammoType.GetItemType(), amount, checkMinimals).Any();

        /// <summary>
        /// Gets the maximum amount of ammo the player can hold, given the ammo <see cref="AmmoType"/>.
        /// </summary>
        /// <param name="type">The <see cref="AmmoType"/> of the ammo to check.</param>
        /// <param name="ignoreArmor">If the method should ignore the armor the player is wearing.</param>
        /// <returns>The maximum amount of ammo this player can carry.</returns>
        public ushort GetAmmoLimit(AmmoType type, bool ignoreArmor = false)
        {
            if (ignoreArmor)
            {
                if (CustomAmmoLimits.TryGetValue(type, out ushort limit))
                    return limit;

                ItemType itemType = type.GetItemType();
                return ServerConfigSynchronizer.Singleton.AmmoLimitsSync.FirstOrDefault(x => x.AmmoType == itemType).Limit;
            }

            return InventorySystem.Configs.InventoryLimits.GetAmmoLimit(type.GetItemType(), referenceHub);
        }

        /// <summary>
        /// Gets the maximum amount of ammo the player can hold, given the ammo <see cref="AmmoType"/>.
        /// This limit will scale with the armor the player is wearing.
        /// For armor ammo limits, see <see cref="Armor.AmmoLimits"/>.
        /// </summary>
        /// <param name="ammoType">The <see cref="AmmoType"/> of the ammo to check.</param>
        /// <param name="limit">The <see cref="ushort"/> number that will define the new limit.</param>
        public void SetAmmoLimit(AmmoType ammoType, ushort limit)
        {
            CustomAmmoLimits[ammoType] = limit;

            ItemType itemType = ammoType.GetItemType();
            int index = ServerConfigSynchronizer.Singleton.AmmoLimitsSync.FindIndex(x => x.AmmoType == itemType);
            MirrorExtensions.SendFakeSyncObject(this, ServerConfigSynchronizer.Singleton.netIdentity, typeof(ServerConfigSynchronizer), writer =>
            {
                writer.WriteULong(2ul);
                writer.WriteUInt(1);
                writer.WriteByte((byte)SyncList<ServerConfigSynchronizer.AmmoLimit>.Operation.OP_SET);
                writer.WriteInt(index);
                writer.WriteAmmoLimit(new() { Limit = limit, AmmoType = itemType, });
            });
        }

        /// <summary>
        /// Reset a custom <see cref="AmmoType"/> limit.
        /// </summary>
        /// <param name="ammoType">The <see cref="AmmoType"/> of the ammo to reset.</param>
        public void ResetAmmoLimit(AmmoType ammoType)
        {
            if (!HasCustomAmmoLimit(ammoType))
            {
                Log.Error($"{nameof(Player)}.{nameof(ResetAmmoLimit)}(AmmoType): AmmoType.{ammoType} does not have a custom limit.");
                return;
            }

            CustomAmmoLimits.Remove(ammoType);

            ItemType itemType = ammoType.GetItemType();
            int index = ServerConfigSynchronizer.Singleton.AmmoLimitsSync.FindIndex(x => x.AmmoType == itemType);
            MirrorExtensions.SendFakeSyncObject(this, ServerConfigSynchronizer.Singleton.netIdentity, typeof(ServerConfigSynchronizer), writer =>
            {
                writer.WriteULong(2ul);
                writer.WriteUInt(1);
                writer.WriteByte((byte)SyncList<ServerConfigSynchronizer.AmmoLimit>.Operation.OP_SET);
                writer.WriteInt(index);
                writer.WriteAmmoLimit(ServerConfigSynchronizer.Singleton.AmmoLimitsSync[index]);
            });
        }

        /// <summary>
        /// Check if the player has a custom limit for a specific <see cref="AmmoType"/>.
        /// </summary>
        /// <param name="ammoType">The <see cref="AmmoType"/> to check.</param>
        /// <returns>If the player has a custom limit for the specific <see cref="AmmoType"/>.</returns>
        public bool HasCustomAmmoLimit(AmmoType ammoType) => CustomAmmoLimits.ContainsKey(ammoType);

        /// <summary>
        /// Gets the maximum amount of an <see cref="ItemCategory"/> the player can hold, based on the armor the player is wearing, as well as server configuration.
        /// </summary>
        /// <param name="category">The <see cref="ItemCategory"/> to check.</param>
        /// <param name="ignoreArmor">If the method should ignore the armor the player is wearing.</param>
        /// <returns>The maximum amount of items in the category that the player can hold.</returns>
        public sbyte GetCategoryLimit(ItemCategory category, bool ignoreArmor = false)
        {
            int index = InventorySystem.Configs.InventoryLimits.StandardCategoryLimits.Where(x => x.Value >= 0).OrderBy(x => x.Key).ToList().FindIndex(x => x.Key == category);

            if (ignoreArmor && index != -1)
            {
                if (CustomCategoryLimits.TryGetValue(category, out sbyte customLimit))
                    return customLimit;

                return ServerConfigSynchronizer.Singleton.CategoryLimits[index];
            }

            sbyte limit = InventorySystem.Configs.InventoryLimits.GetCategoryLimit(category, referenceHub);

            return limit == -1 ? (sbyte)1 : limit;
        }

        /// <summary>
        /// Set the maximum amount of an <see cref="ItemCategory"/> the player can hold. Only works with <see cref="ItemCategory.Keycard"/>, <see cref="ItemCategory.Medical"/>, <see cref="ItemCategory.Firearm"/>, <see cref="ItemCategory.Grenade"/> and <see cref="ItemCategory.SCPItem"/>.
        /// This limit will scale with the armor the player is wearing.
        /// For armor category limits, see <see cref="Armor.CategoryLimits"/>.
        /// </summary>
        /// <param name="category">The <see cref="ItemCategory"/> to check.</param>
        /// <param name="limit">The <see cref="int"/> number that will define the new limit.</param>
        public void SetCategoryLimit(ItemCategory category, sbyte limit)
        {
            int index = InventorySystem.Configs.InventoryLimits.StandardCategoryLimits.Where(x => x.Value >= 0).OrderBy(x => x.Key).ToList().FindIndex(x => x.Key == category);

            if (index == -1)
            {
                Log.Error($"{nameof(Player)}.{nameof(SetCategoryLimit)}(ItemCategory, sbyte): Cannot set category limit for ItemCategory.{category}.");
                return;
            }

            CustomCategoryLimits[category] = limit;

            MirrorExtensions.SendFakeSyncObject(this, ServerConfigSynchronizer.Singleton.netIdentity, typeof(ServerConfigSynchronizer), writer =>
            {
                writer.WriteULong(1ul);
                writer.WriteUInt(1);
                writer.WriteByte((byte)SyncList<sbyte>.Operation.OP_SET);
                writer.WriteInt(index);
                writer.WriteSByte(limit);
            });
        }

        /// <summary>
        /// Reset a custom <see cref="ItemCategory"/> limit. Only works with <see cref="ItemCategory.Keycard"/>, <see cref="ItemCategory.Medical"/>, <see cref="ItemCategory.Firearm"/>, <see cref="ItemCategory.Grenade"/> and <see cref="ItemCategory.SCPItem"/>.
        /// </summary>
        /// <param name="category">The <see cref="ItemCategory"/> of the category to reset.</param>
        public void ResetCategoryLimit(ItemCategory category)
        {
            int index = InventorySystem.Configs.InventoryLimits.StandardCategoryLimits.Where(x => x.Value >= 0).OrderBy(x => x.Key).ToList().FindIndex(x => x.Key == category);

            if (index == -1)
            {
                Log.Error($"{nameof(Player)}.{nameof(ResetCategoryLimit)}(ItemCategory, sbyte): Cannot reset category limit for ItemCategory.{category}.");
                return;
            }

            if (!HasCustomCategoryLimit(category))
            {
                Log.Error($"{nameof(Player)}.{nameof(ResetCategoryLimit)}(ItemCategory): ItemCategory.{category} does not have a custom limit.");
                return;
            }

            CustomCategoryLimits.Remove(category);

            MirrorExtensions.SendFakeSyncObject(this, ServerConfigSynchronizer.Singleton.netIdentity, typeof(ServerConfigSynchronizer), writer =>
            {
                writer.WriteULong(1ul);
                writer.WriteUInt(1);
                writer.WriteByte((byte)SyncList<sbyte>.Operation.OP_SET);
                writer.WriteInt(index);
                writer.WriteSByte(ServerConfigSynchronizer.Singleton.CategoryLimits[index]);
            });
        }

        /// <summary>
        /// Check if the player has a custom limit for a specific <see cref="ItemCategory"/>.
        /// </summary>
        /// <param name="category">The <see cref="ItemCategory"/> to check.</param>
        /// <returns>If the player has a custom limit for the specific <see cref="ItemCategory"/>.</returns>
        public bool HasCustomCategoryLimit(ItemCategory category) => CustomCategoryLimits.ContainsKey(category);

        /// <summary>
        /// Grants the player their current role's loadout.
        /// </summary>
        public void GrantLoadout() => GrantLoadout(Role.Type);

        /// <summary>
        /// Grants a player a role's loadout.
        /// </summary>
        /// <param name="roleType">The role loadout to give.</param>
        public void GrantLoadout(RoleTypeId roleType)
        {
            InventoryRoleInfo info = roleType.GetInventory();

            AddItem(info.Items);
            AddAmmo(info.Ammo);
        }

        /// <summary>
        /// Adds an item of the specified type with default durability(ammo/charge) and no mods to the player's inventory.
        /// </summary>
        /// <param name="itemType">The item to be added.</param>
        /// <returns>The <see cref="Item"/> given to the player.</returns>
        public Item AddItem(ItemType itemType)
        {
            if (itemType.GetFirearmType() is not FirearmType.None)
            {
                return AddItem(itemType.GetFirearmType(), null);
            }

            Item item = Item.Create(itemType);

            AddItem(item);

            return item;
        }

        /// <summary>
        /// Adds an firearm of the specified type with default durability(ammo/charge) and no mods to the player's inventory.
        /// </summary>
        /// <param name="firearmType">The firearm to be added.</param>
        /// <param name="identifiers">The attachments to be added to the item.</param>
        /// <returns>The <see cref="Item"/> given to the player.</returns>
        public Item AddItem(FirearmType firearmType, IEnumerable<AttachmentIdentifier> identifiers)
        {
            Item item = Item.Create(firearmType.GetItemType());

            if (item is Firearm firearm)
            {
                if (identifiers is not null)
                    firearm.AddAttachment(identifiers);
                else if (Preferences is not null && Preferences.TryGetValue(firearmType, out AttachmentIdentifier[] attachments))
                    firearm.Base.ApplyAttachmentsCode(attachments.GetAttachmentsCode(), true);

                // TODO Not finish
                /*
                FirearmStatusFlags flags = FirearmStatusFlags.MagazineInserted;

                if (firearm.Attachments.Any(a => a.Name == AttachmentName.Flashlight))
                    flags |= FirearmStatusFlags.FlashlightEnabled;

                firearm.Base.Status = new FirearmStatus(firearm.MaxAmmo, flags, firearm.Base.GetCurrentAttachmentsCode());
                */
            }

            AddItem(item);

            return item;
        }

        /// <summary>
        /// Adds the amount of items of the specified type with default durability(ammo/charge) and no mods to the player's inventory.
        /// </summary>
        /// <param name="itemType">The item to be added.</param>
        /// <param name="amount">The amount of items to be added.</param>
        /// <returns>An <see cref="IEnumerable{Item}"/> containing the items given.</returns>
        public IEnumerable<Item> AddItem(ItemType itemType, int amount)
        {
            List<Item> items = new(amount > 0 ? amount : 0);
            if (amount > 0)
            {
                for (int i = 0; i < amount; i++)
                    items.Add(AddItem(itemType));
            }

            return items;
        }

        /// <summary>
        /// Adds the amount of firearms of the specified type with default durability(ammo/charge) and no mods to the player's inventory.
        /// </summary>
        /// <param name="firearmType">The item to be added.</param>
        /// <param name="amount">The amount of items to be added.</param>
        /// <param name="identifiers">The attachments to be added to the item.</param>
        /// <returns>An <see cref="IEnumerable{Item}"/> containing the items given.</returns>
        public IEnumerable<Item> AddItem(FirearmType firearmType, int amount, IEnumerable<AttachmentIdentifier> identifiers)
        {
            List<Item> items = new(amount > 0 ? amount : 0);

            if (amount > 0)
            {
                IEnumerable<AttachmentIdentifier> attachmentIdentifiers = identifiers.ToList();

                for (int i = 0; i < amount; i++)
                    items.Add(AddItem(firearmType, attachmentIdentifiers));
            }

            return items;
        }

        /// <summary>
        /// Adds the list of items of the specified type with default durability(ammo/charge) and no mods to the player's inventory.
        /// </summary>
        /// <param name="items">The list of items to be added.</param>
        /// <returns>An <see cref="IEnumerable{Item}"/> containing the items given.</returns>
        public IEnumerable<Item> AddItem(IEnumerable<ItemType> items)
        {
            List<ItemType> enumeratedItems = ListPool<ItemType>.Pool.Get(items);
            List<Item> returnedItems = new(enumeratedItems.Count);

            foreach (ItemType type in enumeratedItems)
                returnedItems.Add(AddItem(type));

            ListPool<ItemType>.Pool.Return(enumeratedItems);
            return returnedItems;
        }

        /// <summary>
        /// Adds the list of items of the specified type with default durability(ammo/charge) and no mods to the player's inventory.
        /// </summary>
        /// <param name="items">The <see cref="Dictionary{TKey, TValue}"/> of <see cref="ItemType"/> and <see cref="IEnumerable{T}"/> of <see cref="AttachmentIdentifier"/> to be added.</param>
        /// <returns>An <see cref="IEnumerable{Item}"/> containing the items given.</returns>
        public IEnumerable<Item> AddItem(Dictionary<FirearmType, IEnumerable<AttachmentIdentifier>> items)
        {
            List<Item> returnedItems = new(items.Count);

            foreach (KeyValuePair<FirearmType, IEnumerable<AttachmentIdentifier>> item in items)
                returnedItems.Add(AddItem(item.Key, item.Value));

            return returnedItems;
        }

        /// <summary>
        /// Adds an item to the player's inventory.
        /// </summary>
        /// <param name="item">The item to be added.</param>
        public void AddItem(Item item)
        {
            try
            {
                AddItem(item.Base, item);
            }
            catch (Exception e)
            {
                Log.Error($"{nameof(Player)}.{nameof(AddItem)}(Item): {e}");
            }
        }

        /// <summary>
        /// Adds an item to the player's inventory.
        /// </summary>
        /// <param name="item">The item to be added.</param>
        /// <param name="identifiers">The attachments to be added to the item.</param>
        public void AddItem(Firearm item, IEnumerable<AttachmentIdentifier> identifiers)
        {
            try
            {
                if (identifiers is not null)
                    item.AddAttachment(identifiers);

                AddItem(item.Base, item);
            }
            catch (Exception exception)
            {
                Log.Error($"{nameof(Player)}.{nameof(AddItem)}(Item): {exception}");
            }
        }

        /// <summary>
        /// Adds an item to the player's inventory.
        /// </summary>
        /// <param name="pickup">The <see cref="Pickup"/> of the item to be added.</param>
        /// <param name="addReason">The reason the item was added.</param>
        /// <returns>The <see cref="Item"/> that was added.</returns>
        public Item AddItem(Pickup pickup, ItemAddReason addReason = ItemAddReason.AdminCommand) => Item.Get(Inventory.ServerAddItem(pickup.Type, addReason, pickup.Serial, pickup.Base));

        /// <summary>
        /// Adds an item to the player's inventory.
        /// </summary>
        /// <param name="pickup">The <see cref="FirearmPickup"/> of the item to be added.</param>
        /// <param name="identifiers">The attachments to be added to <see cref="Pickup"/> of the item.</param>
        /// <returns>The <see cref="Item"/> that was added.</returns>
        public Item AddItem(FirearmPickup pickup, IEnumerable<AttachmentIdentifier> identifiers)
        {
            Firearm firearm = Item.Get<Firearm>(Inventory.ServerAddItem(pickup.Type, ItemAddReason.AdminCommand, pickup.Serial, pickup.Base));

            if (identifiers is not null)
                firearm.AddAttachment(identifiers);

            return firearm;
        }

        /// <summary>
        /// Adds an item to the player's inventory.
        /// </summary>
        /// <param name="itemBase">The item to be added.</param>
        /// <param name="item">The <see cref="Item"/> object of the item.</param>
        /// <param name="addReason">The reason the item was added.</param>
        /// <returns>The <see cref="Item"/> that was added.</returns>
        public Item AddItem(ItemBase itemBase, Item item = null, ItemAddReason addReason = ItemAddReason.AdminCommand)
        {
            try
            {
                item ??= Item.Get(itemBase);
                item.AddReason = addReason;

                Inventory.UserInventory.Items[item.Serial] = itemBase;

                item.ChangeOwner(item.Owner, this);

                if (itemBase is IAcquisitionConfirmationTrigger acquisitionConfirmationTrigger)
                {
                    acquisitionConfirmationTrigger.AcquisitionAlreadyReceived = false;
                }

                typeof(InventoryExtensions).InvokeStaticEvent(nameof(InventoryExtensions.OnItemAdded), new object[] { ReferenceHub, itemBase, null });

                Inventory.SendItemsNextFrame = true;
                return item;
            }
            catch (Exception exception)
            {
                Log.Error($"{nameof(Player)}.{nameof(AddItem)}(ItemBase, [Item]): {exception}");
            }

            return null;
        }

        /// <summary>
        /// Adds the list of items to the player's inventory.
        /// </summary>
        /// <param name="items">The list of items to be added.</param>
        public void AddItem(IEnumerable<Item> items)
        {
            foreach (Item item in items)
                AddItem(item);
        }

        /// <summary>
        /// Adds the list of items to the player's inventory.
        /// </summary>
        /// <param name="firearms">The <see cref="Dictionary{TKey, TValue}"/> of <see cref="Firearm"/> and <see cref="IEnumerable{T}"/> of <see cref="AttachmentIdentifier"/> to be added.</param>
        public void AddItem(Dictionary<Firearm, IEnumerable<AttachmentIdentifier>> firearms)
        {
            if (firearms.Count > 0)
            {
                foreach (KeyValuePair<Firearm, IEnumerable<AttachmentIdentifier>> item in firearms)
                    AddItem(item.Key, item.Value);
            }
        }

        /// <summary>
        /// Gives the player a specific candy. Will give the player a bag if they do not already have one.
        /// </summary>
        /// <param name="candyType">The <see cref="CandyKindID"/> to give.</param>
        /// <returns><see langword="true"/> if a candy was given.</returns>
        public bool TryAddCandy(CandyKindID candyType)
        {
            if (Scp330Bag.TryGetBag(ReferenceHub, out Scp330Bag bag))
            {
                bool flag = bag.TryAddSpecific(candyType);

                if (flag)
                    bag.ServerRefreshBag();

                return flag;
            }

            if (Items.Count > 7)
                return false;

            Scp330 scp330 = (Scp330)AddItem(ItemType.SCP330);

            Timing.CallDelayed(0.02f, () =>
            {
                scp330.Base.Candies.Clear();
                scp330.AddCandy(candyType);
            });

            return true;
        }

        /// <summary>
        /// Removes specific candy from the players <see cref="Scp330"/>.
        /// </summary>
        /// <param name="candyType">The <see cref="CandyKindID"/> to remove.</param>
        /// <param name="removeAll">Remove all candy of that type.</param>
        /// <returns><see langword="true"/> if a candy was removed.</returns>
        public bool TryRemoveCandу(CandyKindID candyType, bool removeAll = false)
        {
            foreach (Item item in Items)
            {
                if (item is not Scp330 bag)
                    continue;

                if (bag.RemoveCandy(candyType, removeAll) > 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Resets the player's inventory to the provided list of items, clearing any items it already possess.
        /// </summary>
        /// <param name="newItems">The new items that have to be added to the inventory.</param>
        public void ResetInventory(IEnumerable<ItemType> newItems)
        {
            ClearItems();

            foreach (ItemType item in newItems)
                AddItem(item);
        }

        /// <summary>
        /// Resets the player's inventory to the provided list of items, clearing any items it already possess.
        /// </summary>
        /// <param name="newItems">The new items that have to be added to the inventory.</param>
        public void ResetInventory(IEnumerable<Item> newItems)
        {
            ClearItems();

            foreach (Item item in newItems)
                AddItem(item);
        }

        /// <summary>
        /// Clears the player's inventory, including all ammo and items.
        /// </summary>
        /// <param name="destroy">Whether to fully destroy the old items.</param>
        /// <seealso cref="ResetInventory(IEnumerable{Item})"/>
        /// <seealso cref="ResetInventory(IEnumerable{ItemType})"/>
        /// <seealso cref="DropItems()"/>
        public void ClearInventory(bool destroy = true)
        {
            ClearAmmo();
            ClearItems(destroy);
        }

        /// <summary>
        /// Clears the player's items.
        /// </summary>
        /// <param name="destroy">Whether to fully destroy the old items.</param>
        /// <seealso cref="ResetInventory(IEnumerable{Item})"/>
        /// <seealso cref="ResetInventory(IEnumerable{ItemType})"/>
        /// <seealso cref="DropItems()"/>
        public void ClearItems(bool destroy = true)
        {
            while (Items.Count > 0)
                RemoveItem(Items.ElementAt(0), destroy);
        }

        /// <summary>
        /// Clears all ammo in the inventory.
        /// </summary>
        /// <seealso cref="ResetInventory(IEnumerable{Item})"/>
        /// <seealso cref="SetAmmo(AmmoType, ushort)"/>
        /// <seealso cref="DropItems()"/>
        public void ClearAmmo()
        {
            ReferenceHub.inventory.UserInventory.ReserveAmmo.Clear();
            ReferenceHub.inventory.SendAmmoNextFrame = true;
        }

        /// <summary>
        /// Drops all items in the player's inventory, including all ammo and items.
        /// </summary>
        /// <seealso cref="ClearInventory(bool)"/>
        public void DropItems() => Inventory.ServerDropEverything();

        /// <summary>
        /// Forces the player to throw a grenade.
        /// </summary>
        /// <param name="type">The <see cref="ProjectileType"/> to be thrown.</param>
        /// <param name="fullForce">Whether to throw with full or half force.</param>
        /// <returns>The <see cref="Throwable"/> item that was spawned.</returns>
        public Throwable ThrowGrenade(ProjectileType type, bool fullForce = true)
        {
            Throwable throwable = type switch
            {
                ProjectileType.Flashbang => new FlashGrenade(),
                ProjectileType.Scp2176 => new Scp2176(),
                _ => new ExplosiveGrenade(type.GetItemType()),
            };

            ThrowItem(throwable, fullForce);
            return throwable;
        }

        /// <summary>
        /// Forcefully throws a <paramref name="throwable"/> item.
        /// </summary>
        /// <param name="throwable">The <see cref="Throwable"/> to be thrown.</param>
        /// <param name="fullForce">Whether to throw with full or half force.</param>
        public void ThrowItem(Throwable throwable, bool fullForce = true)
        {
            throwable.Base.Owner = ReferenceHub;
            throwable.Throw(fullForce);
        }

        /// <summary>
        /// Shows a hint to the player.
        /// </summary>
        /// <param name="message">The message to be shown.</param>
        /// <param name="duration">The duration the text will be on screen.</param>
        public void ShowHint(string message, float duration = 3f)
        {
            ShowHint(message, new HintParameter[] { new StringHintParameter(message) }, null, duration);
        }

        /// <summary>
        /// Shows a hint to the player with the specified message, hint effects, and duration.
        /// </summary>
        /// <param name="message">The message to be shown as a hint.</param>
        /// <param name="hintEffects">The array of hint effects to apply.</param>
        /// <param name="duration">The duration the hint will be displayed, in seconds.</param>
        public void ShowHint(string message, HintEffect[] hintEffects, float duration = 3f)
        {
            ShowHint(message, new HintParameter[] { new StringHintParameter(message) }, hintEffects, duration);
        }

        /// <summary>
        /// Shows a hint to the player with the specified message, hint parameters, hint effects, and duration.
        /// </summary>
        /// <param name="message">The message to be shown as a hint.</param>
        /// <param name="hintParameters">The array of hint parameters to use.</param>
        /// <param name="hintEffects">The array of hint effects to apply.</param>
        /// <param name="duration">The duration the hint will be displayed, in seconds.</param>
        public void ShowHint(string message, HintParameter[] hintParameters, HintEffect[] hintEffects, float duration = 3f)
        {
            message ??= string.Empty;
            HintDisplay.Show(new TextHint(
                message,
                (!hintParameters.IsEmpty()) ? hintParameters : new HintParameter[] { new StringHintParameter(message) },
                hintEffects,
                duration));
        }

        /// <summary>
        /// Show a hint to the player.
        /// </summary>
        /// <param name="hint">The hint to be shown.</param>
        public void ShowHint(Hint hint)
        {
            if (hint.Show)
                ShowHint(hint.Content, hint.Duration);
        }

        /// <summary>
        /// Messages the given <see cref="Features.Message"/> to the player.
        /// </summary>
        /// <param name="message">The <see cref="Features.Message"/> to be messaged.</param>
        /// <param name="shouldClearPrevious">Clears all player's messages before sending the new one.</param>
        public void Message(Message message, bool shouldClearPrevious = false)
        {
            if (message.Show)
                Message(message.Duration, message.Content, message.Type, shouldClearPrevious);
        }

        /// <summary>
        /// Shows a message to the player.
        /// </summary>
        /// <param name="duration">The message duration.</param>
        /// <param name="message">The message to be messaged.</param>
        /// <param name="type">The message type.</param>
        /// <param name="shouldClearPrevious">Clears all player's messages before sending the new one.</param>
        public void Message(ushort duration, string message, MessageType type = MessageType.Broadcast, bool shouldClearPrevious = false)
        {
            if (type == MessageType.Broadcast)
            {
                Broadcast(duration, message, shouldClearPrevious: shouldClearPrevious);
            }
            else
            {
                ShowHint(message, duration);
            }
        }

        /// <summary>
        /// Sends a HitMarker to the player.
        /// </summary>
        /// <param name="size">The size of the hitmarker, ranging from <c>0</c> to <c><see cref="Hitmarker.MaxSize"/></c>).</param>
        public void ShowHitMarker(float size = 1f) =>
            Hitmarker.SendHitmarkerDirectly(ReferenceHub, size);

        /// <summary>
        /// Safely gets an <see cref="object"/> from <see cref="SessionVariables"/>, then casts it to <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The returned object type.</typeparam>
        /// <param name="key">The key of the object to get.</param>
        /// <param name="result">When this method returns, contains the value associated with the specified key, if the key is found; otherwise, the default value for the type of the value parameter is used.</param>
        /// <returns><see langword="true"/> if the SessionVariables contains an element with the specified key; otherwise, <see langword="false"/>.</returns>
        public bool TryGetSessionVariable<T>(string key, out T result)
        {
            if (SessionVariables.TryGetValue(key, out object value) && value is T type)
            {
                result = type;
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>
        /// Plays the Hume Shield break sound effect from the player.
        /// </summary>
        /// <remarks>This will only function if the player's <see cref="FpcRole.IsHumeShieldedRole"/> is <see langword="true"/>.</remarks>
        public void PlayShieldBreakSound()
        => new PlayerRoles.PlayableScps.HumeShield.DynamicHumeShieldController.ShieldBreakMessage() { Target = ReferenceHub }.SendToAuthenticated();

        /// <summary>
        /// Gets a <see cref="StatBase"/> module from the player's <see cref="PlayerStats"/> component.
        /// </summary>
        /// <typeparam name="T">The returned object type.</typeparam>
        /// <returns>The <typeparamref name="T"/> module that was requested.</returns>
        public T GetModule<T>()
            where T : StatBase
            => ReferenceHub.playerStats.GetModule<T>();

        /// <summary>
        /// Gets a <see cref="bool"/> describing whether the given <see cref="StatusEffectBase">status effect</see> is currently enabled.
        /// </summary>
        /// <typeparam name="T">The <see cref="StatusEffectBase"/> to check.</typeparam>
        /// <returns>A <see cref="bool"/> determining whether the player effect is active.</returns>
        public bool IsEffectActive<T>()
            where T : StatusEffectBase
        {
            if (ReferenceHub.playerEffectsController._effectsByType.TryGetValue(typeof(T), out StatusEffectBase playerEffect))
                return playerEffect.IsEnabled;

            return false;
        }

        /// <summary>
        /// Disables all currently active <see cref="StatusEffectBase">status effects</see>.
        /// </summary>
        /// <seealso cref="DisableEffects(IEnumerable{EffectType})"/>
        public void DisableAllEffects()
        {
            foreach (StatusEffectBase effect in ReferenceHub.playerEffectsController.AllEffects)
                effect.IsEnabled = false;
        }

        /// <summary>
        /// Disables all currently active <see cref="StatusEffectBase">status effects</see>.
        /// </summary>
        /// <param name="category">A category to filter the disabled effects.</param>
        /// <seealso cref="DisableAllEffects()"/>
        public void DisableAllEffects(EffectCategory category)
        {
            if (category is EffectCategory.None)
                return;

            foreach (KeyValuePair<Type, StatusEffectBase> effect in ReferenceHub.playerEffectsController._effectsByType)
            {
                if (Enum.TryParse(effect.Key.Name, out EffectType effectType) && effectType.GetCategories().HasFlag(category))
                    effect.Value.IsEnabled = false;
            }
        }

        /// <summary>
        /// Disables a specific <see cref="StatusEffectBase">status effect</see> on the player.
        /// </summary>
        /// <typeparam name="T">The <see cref="StatusEffectBase"/> to disable.</typeparam>
        public void DisableEffect<T>()
            where T : StatusEffectBase => ReferenceHub.playerEffectsController.DisableEffect<T>();

        /// <summary>
        /// Disables a specific <see cref="EffectType">status effect</see> on the player.
        /// </summary>
        /// <param name="effect">The <see cref="EffectType"/> to disable.</param>
        public void DisableEffect(EffectType effect)
        {
            if (TryGetEffect(effect, out StatusEffectBase playerEffect))
                playerEffect.IsEnabled = false;
        }

        /// <summary>
        /// Disables a <see cref="IEnumerable{T}"/> of <see cref="EffectType"/> on the player.
        /// </summary>
        /// <param name="effects">The <see cref="IEnumerable{T}"/> of <see cref="EffectType"/> to disable.</param>
        public void DisableEffects(IEnumerable<EffectType> effects)
        {
            foreach (EffectType effect in effects)
                DisableEffect(effect);
        }

        /// <summary>
        /// Enables a <see cref="StatusEffectBase">status effect</see> on the player.
        /// </summary>
        /// <typeparam name="T">The <see cref="StatusEffectBase"/> to enable.</typeparam>
        /// <param name="duration">The amount of time the effect will be active for.</param>
        /// <param name="addDurationIfActive">If the effect is already active, setting to <see langword="true"/> will add this duration onto the effect.</param>
        /// <returns>A bool indicating whether the effect was valid and successfully enabled.</returns>
        public bool EnableEffect<T>(float duration = 0f, bool addDurationIfActive = false)
                    where T : StatusEffectBase => EnableEffect<T>(1, duration, addDurationIfActive);

        /// <summary>
        /// Enables a <see cref="StatusEffectBase">status effect</see> on the player.
        /// </summary>
        /// <typeparam name="T">The <see cref="StatusEffectBase"/> to enable.</typeparam>
        /// <param name="intensity">The intensity of the effect will be active for.</param>
        /// <param name="duration">The amount of time the effect will be active for.</param>
        /// <param name="addDurationIfActive">If the effect is already active, setting to <see langword="true"/> will add this duration onto the effect.</param>
        /// <returns>A bool indicating whether the effect was valid and successfully enabled.</returns>
        public bool EnableEffect<T>(byte intensity, float duration = 0f, bool addDurationIfActive = false)
            where T : StatusEffectBase => ReferenceHub.playerEffectsController.ChangeState<T>(intensity, duration, addDurationIfActive);

        /// <summary>
        /// Enables a <see cref="StatusEffectBase">status effect</see> on the player.
        /// </summary>
        /// <param name="statusEffect">The name of the <see cref="StatusEffectBase"/> to enable.</param>
        /// <param name="duration">The amount of time the effect will be active for.</param>
        /// <param name="addDurationIfActive">If the effect is already active, setting to <see langword="true"/> will add this duration onto the effect.</param>
        /// <returns>A bool indicating whether the effect was valid and successfully enabled.</returns>
        public bool EnableEffect(StatusEffectBase statusEffect, float duration = 0f, bool addDurationIfActive = false)
            => EnableEffect(statusEffect, 1, duration, addDurationIfActive);

        /// <summary>
        /// Enables a <see cref="StatusEffectBase">status effect</see> on the player.
        /// </summary>
        /// <param name="statusEffect">The name of the <see cref="StatusEffectBase"/> to enable.</param>
        /// <param name="intensity">The intensity of the effect will be active for.</param>
        /// <param name="duration">The amount of time the effect will be active for.</param>
        /// <param name="addDurationIfActive">If the effect is already active, setting to <see langword="true"/> will add this duration onto the effect.</param>
        /// <returns>A bool indicating whether the effect was valid and successfully enabled.</returns>
        public bool EnableEffect(StatusEffectBase statusEffect, byte intensity, float duration = 0f, bool addDurationIfActive = false)
        {
            if (statusEffect is null)
                return false;

            statusEffect.ServerSetState(intensity, duration, addDurationIfActive);

            return statusEffect is not null && statusEffect.IsEnabled;
        }

        /// <summary>
        /// Enables a <see cref="StatusEffectBase">status effect</see> on the player.
        /// </summary>
        /// <param name="effectName">The name of the <see cref="StatusEffectBase"/> to enable.</param>
        /// <param name="duration">The amount of time the effect will be active for.</param>
        /// <param name="addDurationIfActive">If the effect is already active, setting to <see langword="true"/> will add this duration onto the effect.</param>
        /// <returns>The <see cref="StatusEffectBase"/> instance of the activated effect.</returns>
        public StatusEffectBase EnableEffect(string effectName, float duration = 0f, bool addDurationIfActive = false)
            => EnableEffect(effectName, 1, duration, addDurationIfActive);

        /// <summary>
        /// Enables a <see cref="StatusEffectBase">status effect</see> on the player.
        /// </summary>
        /// <param name="effectName">The name of the <see cref="StatusEffectBase"/> to enable.</param>
        /// <param name="intensity">The intensity of the effect will be active for.</param>
        /// <param name="duration">The amount of time the effect will be active for.</param>
        /// <param name="addDurationIfActive">If the effect is already active, setting to <see langword="true"/> will add this duration onto the effect.</param>
        /// <returns>The <see cref="StatusEffectBase"/> instance of the activated effect.</returns>
        public StatusEffectBase EnableEffect(string effectName, byte intensity, float duration = 0f, bool addDurationIfActive = false)
            => ReferenceHub.playerEffectsController.ChangeState(effectName, intensity, duration, addDurationIfActive);

        /// <summary>
        /// Enables a <see cref="EffectType">status effect</see> on the player.
        /// </summary>
        /// <param name="type">The <see cref="EffectType"/> to enable.</param>
        /// <param name="duration">The amount of time the effect will be active for.</param>
        /// <param name="addDurationIfActive">If the effect is already active, setting to <see langword="true"/> will add this duration onto the effect.</param>
        public void EnableEffect(EffectType type, float duration = 0f, bool addDurationIfActive = false)
            => EnableEffect(type, 1, duration, addDurationIfActive);

        /// <summary>
        /// Enables a <see cref="EffectType">status effect</see> on the player.
        /// </summary>
        /// <param name="type">The <see cref="EffectType"/> to enable.</param>
        /// <param name="intensity">The intensity of the effect will be active for.</param>
        /// <param name="duration">The amount of time the effect will be active for.</param>
        /// <param name="addDurationIfActive">If the effect is already active, setting to <see langword="true"/> will add this duration onto the effect.</param>
        /// <returns>A bool indicating whether the effect was valid and successfully enabled.</returns>
        public bool EnableEffect(EffectType type, byte intensity, float duration = 0f, bool addDurationIfActive = false)
            => TryGetEffect(type, out StatusEffectBase statusEffect) && EnableEffect(statusEffect, intensity, duration, addDurationIfActive);

        /// <summary>
        /// Syncs the <see cref="Effect">status effect</see> on the player.
        /// </summary>
        /// <param name="effect">The <see cref="Effect"/> to sync.</param>
        public void SyncEffect(Effect effect)
        {
            if (effect.IsEnabled)
            {
                EnableEffect(effect.Type, effect.Duration, effect.AddDurationIfActive);

                if (effect.Intensity > 0)
                    ChangeEffectIntensity(effect.Type, effect.Intensity, effect.Duration);
            }
        }

        /// <summary>
        /// Enables a random <see cref="EffectType"/> on the player.
        /// </summary>
        /// <param name="category">An optional category to filter the applied effect. Set to <see cref="EffectCategory.None"/> for any effect.</param>
        /// <param name="duration">The amount of time the effect will be active for.</param>
        /// <param name="addDurationIfActive">If the effect is already active, setting to <see langword="true"/> will add this duration onto the effect.</param>
        /// <returns>A <see cref="EffectType"/> that was given to the player.</returns>
        public EffectType ApplyRandomEffect(EffectCategory category = EffectCategory.None, float duration = 0f, bool addDurationIfActive = false)
            => ApplyRandomEffect(category, 1, duration, addDurationIfActive);

        /// <summary>
        /// Enables a random <see cref="EffectType"/> on the player.
        /// </summary>
        /// <param name="category">An optional category to filter the applied effect. Set to <see cref="EffectCategory.None"/> for any effect.</param>
        /// <param name="intensity">The intensity of the effect will be active for.</param>
        /// <param name="duration">The amount of time the effect will be active for.</param>
        /// <param name="addDurationIfActive">If the effect is already active, setting to <see langword="true"/> will add this duration onto the effect.</param>
        /// <returns>A <see cref="EffectType"/> that was given to the player.</returns>
        public EffectType ApplyRandomEffect(EffectCategory category, byte intensity, float duration = 0f, bool addDurationIfActive = false)
        {
            Array effectTypes = Enum.GetValues(typeof(EffectType));
            IEnumerable<EffectType> validEffects = effectTypes.ToArray<EffectType>().Where(effect => effect.GetCategories().HasFlag(category));
            EffectType effectType = validEffects.GetRandomValue();

            EnableEffect(effectType, intensity, duration, addDurationIfActive);

            return effectType;
        }

        /// <summary>
        /// Enables a <see cref="IEnumerable{T}"/> of <see cref="EffectType"/> on the player.
        /// </summary>
        /// <param name="types">The <see cref="IEnumerable{T}"/> of <see cref="EffectType"/> to enable.</param>
        /// <param name="duration">The amount of time the effects will be active for.</param>
        /// <param name="addDurationIfActive">If an effect is already active, setting to <see langword="true"/> will add this duration onto the effect.</param>
        public void EnableEffects(IEnumerable<EffectType> types, float duration = 0f, bool addDurationIfActive = false)
        {
            foreach (EffectType type in types)
            {
                if (TryGetEffect(type, out StatusEffectBase statusEffect))
                    EnableEffect(statusEffect, duration, addDurationIfActive);
            }
        }

        /// <summary>
        /// Syncs a <see cref="IEnumerable{T}"/> of <see cref="Effect"/> on the player.
        /// </summary>
        /// <param name="effects">The <see cref="IEnumerable{T}"/> of <see cref="Effect"/> to enable.</param>
        public void SyncEffects(IEnumerable<Effect> effects)
        {
            foreach (Effect effect in effects)
                SyncEffect(effect);
        }

        /// <summary>
        /// Gets an effect of a player.
        /// </summary>
        /// <typeparam name="T">The <see cref="StatusEffectBase"/> to get.</typeparam>
        /// <returns>The <see cref="StatusEffectBase"/> found.</returns>
        public T GetEffect<T>()
            where T : StatusEffectBase => ReferenceHub.playerEffectsController.GetEffect<T>();

        /// <summary>
        /// Gets an instance of <see cref="StatusEffectBase"/> by <see cref="EffectType"/>.
        /// </summary>
        /// <param name="effectType">The <see cref="EffectType"/>.</param>
        /// <returns>The <see cref="StatusEffectBase"/>.</returns>
        public StatusEffectBase GetEffect(EffectType effectType)
        {
            if (!effectType.TryGetType(out Type type))
                return null;
            ReferenceHub.playerEffectsController._effectsByType.TryGetValue(type, out StatusEffectBase playerEffect);
            return playerEffect;
        }

        /// <summary>
        /// Tries to get an instance of <see cref="StatusEffectBase"/> by <see cref="EffectType"/>.
        /// </summary>
        /// <param name="type">The <see cref="EffectType"/>.</param>
        /// <param name="statusEffect">The <see cref="StatusEffectBase"/>.</param>
        /// <returns>A bool indicating whether the <paramref name="statusEffect"/> was successfully gotten.</returns>
        public bool TryGetEffect(EffectType type, out StatusEffectBase statusEffect)
        {
            statusEffect = GetEffect(type);

            return statusEffect is not null;
        }

        /// <summary>
        /// Tries to get an instance of <see cref="StatusEffectBase"/> by <see cref="EffectType"/>.
        /// </summary>
        /// <param name="statusEffect">The <see cref="StatusEffectBase"/>.</param>
        /// <typeparam name="T">The <see cref="StatusEffectBase"/> to get.</typeparam>
        /// <returns>A bool indicating whether the <paramref name="statusEffect"/> was successfully gotten.</returns>
        public bool TryGetEffect<T>(out T statusEffect)
            where T : StatusEffectBase
            => ReferenceHub.playerEffectsController.TryGetEffect(out statusEffect);

        /// <summary>
        /// Gets a <see cref="byte"/> indicating the intensity of the given <see cref="StatusEffectBase"></see>.
        /// </summary>
        /// <typeparam name="T">The <see cref="StatusEffectBase"/> to check.</typeparam>
        /// <exception cref="ArgumentException">Thrown if the given type is not a valid <see cref="StatusEffectBase"/>.</exception>
        /// <returns>The intensity of the effect.</returns>
        public byte GetEffectIntensity<T>()
            where T : StatusEffectBase
        {
            if (ReferenceHub.playerEffectsController._effectsByType.TryGetValue(typeof(T), out StatusEffectBase statusEffect))
                return statusEffect.Intensity;

            throw new ArgumentException("The given type is invalid.");
        }

        /// <summary>
        /// Changes the intensity of a <see cref="StatusEffectBase">status effect</see>.
        /// </summary>
        /// <typeparam name="T">The <see cref="StatusEffectBase"/> to change the intensity of.</typeparam>
        /// <param name="intensity">The intensity of the effect.</param>
        /// <param name="duration">The new duration to add to the effect.</param>
        public void ChangeEffectIntensity<T>(byte intensity, float duration = 0)
            where T : StatusEffectBase
        {
            if (ReferenceHub.playerEffectsController.TryGetEffect(out T statusEffect))
            {
                statusEffect.Intensity = intensity;
                statusEffect.ServerChangeDuration(duration, true);
            }
        }

        /// <summary>
        /// Changes the intensity of a <see cref="StatusEffectBase"/>.
        /// </summary>
        /// <param name="type">The <see cref="EffectType"/> to change.</param>
        /// <param name="intensity">The new intensity to use.</param>
        /// <param name="duration">The new duration to add to the effect.</param>
        public void ChangeEffectIntensity(EffectType type, byte intensity, float duration = 0)
        {
            if (TryGetEffect(type, out StatusEffectBase statusEffect))
            {
                statusEffect.Intensity = intensity;
                statusEffect.ServerChangeDuration(duration, false);
            }
        }

        /// <summary>
        /// Changes the intensity of a <see cref="StatusEffectBase">status effect</see>.
        /// </summary>
        /// <param name="effectName">The name of the <see cref="StatusEffectBase"/> to enable.</param>
        /// <param name="intensity">The intensity of the effect.</param>
        /// <param name="duration">The new length of the effect. Defaults to infinite length.</param>
        public void ChangeEffectIntensity(string effectName, byte intensity, float duration = 0)
        {
            if (Enum.TryParse(effectName, out EffectType type))
                ChangeEffectIntensity(type, intensity, duration);
        }

        /// <summary>
        /// Gets an instance of <see cref="DangerStackBase"/> by <see cref="DangerType"/> if the Scp1853 effect is enabled or null if it is not enabled.
        /// </summary>
        /// <param name="dangerType">The <see cref="DangerType"/>.</param>
        /// <returns>The <see cref="DangerStackBase"/>.</returns>
        public DangerStackBase GetDanger(DangerType dangerType) => Dangers.FirstOrDefault(danger => danger.TryGetDangerType(out DangerType type) && dangerType == type);

        /// <summary>
        /// Tries to get an instance of <see cref="StatusEffectBase"/> by <see cref="EffectType"/> (does not work if the Scp1853 effect is not enabled).
        /// </summary>
        /// <param name="type">The <see cref="EffectType"/>.</param>
        /// <param name="danger">The <see cref="StatusEffectBase"/>.</param>
        /// <returns>A bool indicating whether the <paramref name="danger"/> was successfully gotten.</returns>
        public bool TryGetDanger(DangerType type, out DangerStackBase danger) => (danger = GetDanger(type)) is not null;

        /// <summary>
        /// Opens the report window.
        /// </summary>
        /// <param name="text">The text to send.</param>
        public void OpenReportWindow(string text) => SendConsoleMessage($"[REPORTING] {text}", "white");

        /// <summary>
        /// Places a Tantrum (SCP-173's ability) under the player.
        /// </summary>
        /// <param name="isActive">Whether the tantrum will apply the <see cref="EffectType.Stained"/> effect.</param>
        /// <remarks>If <paramref name="isActive"/> is <see langword="true"/>, the tantrum is moved slightly up from its original position. Otherwise, the collision will not be detected and the slowness will not work.</remarks>
        /// <returns>The <see cref="TantrumHazard"/> instance..</returns>
        public TantrumHazard PlaceTantrum(bool isActive = true) => TantrumHazard.PlaceTantrum(Position, isActive);

        /// <summary>
        /// Gives a new <see cref="AhpStat">to the player</see>.
        /// </summary>
        /// <param name="amount">The amount to give the player.</param>
        /// <param name="limit">The maximum AHP for this stat.</param>
        /// <param name="decay">How much value is lost per second.</param>
        /// <param name="efficacy">Percent of incoming damage absorbed by this stat.</param>
        /// <param name="sustain">The number of seconds to delay the start of the decay.</param>
        /// <param name="persistant">Whether the process is removed when the value hits 0.</param>
        public void AddAhp(float amount, float limit = 75f, float decay = 1.2f, float efficacy = 0.7f, float sustain = 0f, bool persistant = false)
        {
            ReferenceHub.playerStats.GetModule<AhpStat>()
                .ServerAddProcess(amount, limit, decay, efficacy, sustain, persistant);
        }

        /// <summary>
        /// Adds a new <see cref="RegenerationProcess"/> to the player.
        /// </summary>
        /// <param name="starttime">The delay before regeneration starts (in seconds).</param>
        /// <param name="rate">Health points regenerated per second.</param>
        /// <param name="duration">Total duration of the regeneration (in seconds).</param>
        /// <param name="speedMultiplier">How fast the regeneration progresses (default is 1.0).</param>
        /// <param name="healthPointsMultiplier">Multiplier for HP amount being regenerated (default is 1.0).</param>
        public void AddRegeneration(float starttime = 0f, float rate = 1f, float duration = 1f, float speedMultiplier = 1f, float healthPointsMultiplier = 1f)
        {
            AnimationCurve regenCurve = AnimationCurve.Constant(starttime, duration, rate);
            UsableItemsController.GetHandler(ReferenceHub)
                .ActiveRegenerations.Add(new RegenerationProcess(regenCurve, speedMultiplier, healthPointsMultiplier));
        }

        /// <summary>
        /// Reconnects the player to the server. Can be used to redirect them to another server on a different port but same IP.
        /// </summary>
        /// <param name="newPort">New port.</param>
        /// <param name="delay">Player reconnection delay.</param>
        /// <param name="reconnect">Whether player should be reconnected.</param>
        /// <param name="roundRestartType">Type of round restart.</param>
        public void Reconnect(ushort newPort = 0, float delay = 5, bool reconnect = true, RoundRestartType roundRestartType = RoundRestartType.FullRestart)
        {
            if (newPort != 0)
                roundRestartType = newPort == Server.Port && roundRestartType is RoundRestartType.RedirectRestart ? RoundRestartType.FullRestart : RoundRestartType.RedirectRestart;

            Connection.Send(new RoundRestartMessage(roundRestartType, delay, newPort, reconnect, false));
        }

        /// <inheritdoc cref="MirrorExtensions.PlayGunSound(Player, Vector3, ItemType, byte, byte)"/>
        [Obsolete("Use PlayGunSound(Player, Vector3, FirearmType, byte, byte) instead.")]
        public void PlayGunSound(ItemType type, byte volume, byte audioClipId = 0)
            => PlayGunSound(type.GetFirearmType(), volume, audioClipId);

        /// <inheritdoc cref="MirrorExtensions.PlayGunSound(Player, Vector3, FirearmType, float, int)"/>
        public void PlayGunSound(FirearmType itemType, float pitch = 1, int clipIndex = 0) =>
            this.PlayGunSound(Position, itemType, pitch, clipIndex);

        /// <inheritdoc cref="Map.PlaceBlood(Vector3, Vector3)"/>
        public void PlaceBlood(Vector3 direction) => Map.PlaceBlood(Position, direction);

        /// <inheritdoc cref="Map.GetNearCameras(Vector3, float)"/>
        public IEnumerable<Camera> GetNearCameras(float toleration = 15f) => Map.GetNearCameras(Position, toleration);

        /// <summary>
        /// Teleports the player to the given <see cref="Vector3"/> coordinates.
        /// </summary>
        /// <param name="position">The <see cref="Vector3"/> coordinates to move the player to.</param>
        public void Teleport(Vector3 position) => Position = position;

        /// <summary>
        /// Teleports the player to the given object, with no offset.
        /// </summary>
        /// <param name="obj">The object to teleport to.</param>
        public void Teleport(object obj)
            => Teleport(obj, Vector3.zero);

        /// <summary>
        /// Teleports the player to the given object, offset by the defined offset value.
        /// </summary>
        /// <param name="obj">The object to teleport the player to.</param>
        /// <param name="offset">The offset to teleport.</param>
        public void Teleport(object obj, Vector3 offset)
        {
            switch (obj)
            {
                case TeslaGate teslaGate:
                    Teleport(
                        teslaGate.Position + offset + Vector3.up +
                        (teslaGate.Room.Transform.rotation == new Quaternion(0f, 0f, 0f, 1f)
                            ? new Vector3(3, 0, 0)
                            : new Vector3(0, 0, 3)));
                    break;
                case IPosition positionObject:
                    Teleport(positionObject.Position + Vector3.up + offset);
                    break;
                case DoorType doorType:
                    Teleport(Door.Get(doorType).Position + Vector3.up + offset);
                    break;
                case SpawnLocationType sp:
                    Teleport(sp.GetPosition() + offset);
                    break;
                case RoomType roomType:
                    Teleport(Room.Get(roomType).Position + Vector3.up + offset);
                    break;
                case Enums.CameraType cameraType:
                    Teleport(Camera.Get(cameraType).Position + offset);
                    break;
                case ElevatorType elevatorType:
                    Teleport(Lift.Get(elevatorType).Position + Vector3.up + offset);
                    break;
                case Scp914Controller scp914:
                    Teleport(scp914._knobTransform.position + Vector3.up + offset);
                    break;
                case Role role:
                    if (role.Owner is not null)
                        Teleport(role.Owner.Position + offset);
                    else
                        Log.Warn($"{nameof(Teleport)}: {Assembly.GetCallingAssembly().GetName().Name}: Invalid role teleport (role is missing Owner).");
                    break;
                case Locker locker:
                    Teleport(locker.transform.position + Vector3.up + offset);
                    break;
                case LockerChamber chamber:
                    Teleport(chamber.Spawnpoint.position + Vector3.up + offset);
                    break;
                case ElevatorChamber elevator:
                    Teleport(elevator.transform.position + Vector3.up + offset);
                    break;
                case Item item:
                    if (item.Owner is not null)
                        Teleport(item.Owner.Position + offset);
                    else
                        Log.Warn($"{nameof(Teleport)}: {Assembly.GetCallingAssembly().GetName().Name}: Invalid item teleport (item is missing Owner).");
                    break;

                // Unity
                case Vector3 v3: // I wouldn't be surprised if someone calls this method with a Vector3.
                    Teleport(v3 + offset);
                    break;
                case Component comp:
                    Teleport(comp.transform.position + Vector3.up + offset);
                    break;
                case GameObject go:
                    Teleport(go.transform.position + Vector3.up + offset);
                    break;

                default:
                    Log.Warn($"{nameof(Teleport)}: {Assembly.GetCallingAssembly().GetName().Name}: Invalid type declared: {obj.GetType()}");
                    break;
            }
        }

        /// <summary>
        /// Teleports player to a random object of a specific type.
        /// </summary>
        /// <param name="type">Object for teleport.</param>
        public void RandomTeleport(Type type)
        {
            object randomObject = type.Name switch
            {
                nameof(Camera) => Camera.List.GetRandomValue(),
                nameof(Door) => Door.Random(),
                nameof(Room) => Room.List.GetRandomValue(),
                nameof(TeslaGate) => TeslaGate.List.GetRandomValue(),
                nameof(Player) => Dictionary.Values.GetRandomValue(),
                nameof(Pickup) => Pickup.BaseToPickup.GetRandomValue().Value,
                nameof(Ragdoll) => Ragdoll.List.GetRandomValue(),
                nameof(Locker) => Lockers.Locker.Random().Base,
                nameof(Generator) => Generator.List.GetRandomValue(),
                nameof(Window) => Window.List.GetRandomValue(),
                nameof(Scp914) => Scp914.Scp914Controller,
                nameof(LockerChamber) => Lockers.Locker.Random().Chambers.GetRandomValue().Base,
                _ => null,
            };

            Teleport(randomObject);
        }

        /// <summary>
        /// Teleports the player to a random object.
        /// </summary>
        /// <param name="types">The list of object types to choose from.</param>
        public void RandomTeleport(IEnumerable<Type> types)
        {
            Type[] array = types as Type[] ?? types.ToArray();

            if (array.Length == 0)
                return;

            RandomTeleport(array.GetRandomValue());
        }

        /// <summary>
        /// Teleports player to a random object of a specific type.
        /// </summary>
        /// <typeparam name="T">Object for teleport.</typeparam>
        public void RandomTeleport<T>() => RandomTeleport(typeof(T));

        /// <inheritdoc/>
        public T AddComponent<T>(string name = "")
            where T : EActor
        {
            T component = EObject.CreateDefaultSubobject<T>(GameObject);

            if (component is null)
                return null;

            componentsInChildren.Add(component);
            return component;
        }

        /// <inheritdoc/>
        public EActor AddComponent(Type type, string name = "")
        {
            EActor component = EObject.CreateDefaultSubobject(type, GameObject).Cast<EActor>();

            if (component is null)
                return null;

            componentsInChildren.Add(component);
            return component;
        }

        /// <inheritdoc/>
        public T AddComponent<T>(Type type, string name = "")
            where T : EActor
        {
            T component = EObject.CreateDefaultSubobject<T>(type, GameObject);
            if (component is null)
                return null;

            componentsInChildren.Add(component);
            return component;
        }

        /// <inheritdoc/>
        public T GetComponent<T>()
            where T : EActor => componentsInChildren.FirstOrDefault(comp => typeof(T) == comp.GetType()).Cast<T>();

        /// <inheritdoc/>
        public T GetComponent<T>(Type type)
            where T : EActor => componentsInChildren.FirstOrDefault(comp => type == comp.GetType()).Cast<T>();

        /// <inheritdoc/>
        public EActor GetComponent(Type type) => componentsInChildren.FirstOrDefault(comp => type == comp.GetType());

        /// <inheritdoc/>
        public bool TryGetComponent<T>(out T component)
            where T : EActor
        {
            component = GetComponent<T>();

            return component is not null;
        }

        /// <inheritdoc/>
        public bool TryGetComponent(Type type, out EActor component)
        {
            component = GetComponent(type);

            return component is not null;
        }

        /// <inheritdoc/>
        public bool TryGetComponent<T>(Type type, out T component)
            where T : EActor
        {
            component = GetComponent<T>(type);

            return component is not null;
        }

        /// <inheritdoc/>
        public bool HasComponent<T>(bool depthInheritance = false) => depthInheritance
            ? componentsInChildren.Any(comp => typeof(T).IsSubclassOf(comp.GetType()))
            : componentsInChildren.Any(comp => typeof(T) == comp.GetType());

        /// <inheritdoc/>
        public bool HasComponent(Type type, bool depthInheritance = false) => depthInheritance
            ? componentsInChildren.Any(comp => type.IsSubclassOf(comp.GetType()))
            : componentsInChildren.Any(comp => type == comp.GetType());

        /// <summary>
        /// Get the time cooldown on this ItemType.
        /// </summary>
        /// <param name="itemType">The itemtypes to choose for getting cooldown.</param>
        /// <returns>Return the time in seconds of the cooldowns.</returns>
        public float GetCooldownItem(ItemType itemType)
            => UsableItemsController.GetHandler(ReferenceHub).PersonalCooldowns.TryGetValue(itemType, out float value) ? value : -1;

        /// <summary>
        /// Set the time cooldown on this ItemType.
        /// </summary>
        /// <param name="time">The times for the cooldown.</param>
        /// <param name="itemType">The itemtypes to choose for being cooldown.</param>
        public void SetCooldownItem(float time, ItemType itemType)
            => UsableItemsController.GetHandler(ReferenceHub).PersonalCooldowns[itemType] = Time.timeSinceLevelLoad + time;

        /// <summary>
        /// Explode the player.
        /// </summary>
        public void Explode() => ExplosionUtils.ServerExplode(ReferenceHub, ExplosionType.Grenade);

        /// <summary>
        /// Explode the player.
        /// </summary>
        /// <param name="projectileType">The projectile that will create the explosion.</param>
        /// <param name="attacker">The Player that will causing the explosion.</param>
        public void Explode(ProjectileType projectileType, Player attacker = null) => Map.Explode(Position, projectileType, attacker);

        /// <summary>
        /// Spawn projectile effect on the player.
        /// </summary>
        /// <param name="projectileType">The projectile that will create the effect.</param>
        public void ExplodeEffect(ProjectileType projectileType) => Map.ExplodeEffect(Position, projectileType);

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            Player player = obj as Player;
            return (object)player != null && ReferenceHub == player.ReferenceHub;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        /// <summary>
        /// Returns whether the two players are the same.
        /// </summary>
        /// <param name="player1">The first player instance.</param>
        /// <param name="player2">The second player instance.</param>
        /// <returns><see langword="true"/> if the values are equal.</returns>
#pragma warning disable SA1201
        public static bool operator ==(Player player1, Player player2) => player1?.Equals(player2) ?? player2 is null;

        /// <summary>
        /// Returns whether the two players are different.
        /// </summary>
        /// <param name="player1">The first player instance.</param>
        /// <param name="player2">The second player instance.</param>
        /// <returns><see langword="true"/> if the values are not equal.</returns>
        public static bool operator !=(Player player1, Player player2) => !(player1 == player2);
#pragma warning restore SA1201

        /// <summary>
        /// Converts the player in a human-readable format.
        /// </summary>
        /// <returns>A string containing Player-related data.</returns>
        public override string ToString() => $"{Id} ({Nickname}) [{UserId}] *{(Role is null ? "No role" : Role)}*";
    }
}
