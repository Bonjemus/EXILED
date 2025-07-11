// -----------------------------------------------------------------------
// <copyright file="UpgradingPlayer.cs" company="ExMod Team">
// Copyright (c) ExMod Team. All rights reserved.
// Licensed under the CC BY-SA 3.0 license.
// </copyright>
// -----------------------------------------------------------------------

namespace Exiled.Events.Patches.Events.Scp914
{
    using System.Collections.Generic;
    using System.Reflection;
    using System.Reflection.Emit;

    using API.Features;
    using API.Features.Pools;
    using Exiled.Events.Attributes;
    using Exiled.Events.EventArgs.Scp914;
    using global::Scp914;
    using HarmonyLib;
    using Mono.Cecil.Cil;
    using PlayerRoles.FirstPersonControl;
    using UnityEngine;

    using static HarmonyLib.AccessTools;

    using OpCode = System.Reflection.Emit.OpCode;
    using Scp914 = Handlers.Scp914;

    /// <summary>
    /// Patches <see cref="Scp914Upgrader.ProcessPlayer" />
    /// to add the <see cref="Scp914.UpgradingPlayer" /> and <see cref="Scp914.UpgradingInventoryItem" /> event.
    /// </summary>
    [EventPatch(typeof(Scp914), nameof(Scp914.UpgradingPlayer))]
    [EventPatch(typeof(Scp914), nameof(Scp914.UpgradingInventoryItem))]
    [HarmonyPatch(typeof(Scp914Upgrader), nameof(Scp914Upgrader.ProcessPlayer))]
    internal static class UpgradingPlayer
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            List<CodeInstruction> newInstructions = ListPool<CodeInstruction>.Pool.Get(instructions);

            Label returnLabel = generator.DefineLabel();

            LocalBuilder curSetting = generator.DeclareLocal(typeof(Scp914KnobSetting));
            LocalBuilder ev = generator.DeclareLocal(typeof(UpgradingPlayerEventArgs));

            int offset = 1;
            int index = newInstructions.FindIndex(instruction => instruction.opcode == OpCodes.Stloc_0) + offset;

            newInstructions.InsertRange(
                index,
                new[]
                {
                    // Player.Get(ply)
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new(OpCodes.Call, Method(typeof(Player), nameof(Player.Get), new[] { typeof(ReferenceHub) })),

                    // upgradeInventory
                    new(OpCodes.Ldarg_1),

                    // heldOnly
                    new(OpCodes.Ldarg_2),

                    // setting
                    new(OpCodes.Ldarg_3),

                    // outputPosition
                    new(OpCodes.Ldloc_0),

                    // UpgradingPlayerEventArgs ev = new(player, upgradeInventory, heldonly, setting, moveVector);
                    new(OpCodes.Newobj, GetDeclaredConstructors(typeof(UpgradingPlayerEventArgs))[0]),
                    new(OpCodes.Dup),
                    new(OpCodes.Dup),
                    new(OpCodes.Stloc_S, ev.LocalIndex),

                    // Handlers.Scp914.OnUpgradingPlayer(ev);
                    new(OpCodes.Call, Method(typeof(Scp914), nameof(Scp914.OnUpgradingPlayer))),

                    // if (!ev.IsAllowed)
                    //    return;
                    new(OpCodes.Callvirt, PropertyGetter(typeof(UpgradingPlayerEventArgs), nameof(UpgradingPlayerEventArgs.IsAllowed))),
                    new(OpCodes.Brfalse_S, returnLabel),

                    // load "ev" three times
                    new(OpCodes.Ldloc_S, ev.LocalIndex),
                    new(OpCodes.Dup),
                    new(OpCodes.Dup),
                    new(OpCodes.Dup),

                    // upgradeInventory = ev.UpgradeItems
                    new(OpCodes.Callvirt, PropertyGetter(typeof(UpgradingPlayerEventArgs), nameof(UpgradingPlayerEventArgs.UpgradeItems))),
                    new(OpCodes.Starg_S, 1),

                    // heldOnly = ev.HeldOnly
                    new(OpCodes.Callvirt, PropertyGetter(typeof(UpgradingPlayerEventArgs), nameof(UpgradingPlayerEventArgs.HeldOnly))),
                    new(OpCodes.Starg_S, 2),

                    // setting = ev.KnobSetting
                    new(OpCodes.Callvirt, PropertyGetter(typeof(UpgradingPlayerEventArgs), nameof(UpgradingPlayerEventArgs.KnobSetting))),
                    new(OpCodes.Starg_S, 3),

                    // outputPosition = ev.OutputPosition
                    new(OpCodes.Callvirt, PropertyGetter(typeof(UpgradingPlayerEventArgs), nameof(UpgradingPlayerEventArgs.OutputPosition))),
                    new(OpCodes.Stloc_0),

                    // curSetting = setting;
                    new(OpCodes.Ldarg_S, 3),
                    new(OpCodes.Stloc_S, curSetting.LocalIndex),
                });

            offset = 1;
            index = newInstructions.FindIndex(x => x.Calls(PropertyGetter(typeof(LabApi.Events.Arguments.Scp914Events.Scp914ProcessingInventoryItemEventArgs), nameof(LabApi.Events.Arguments.Scp914Events.Scp914ProcessingInventoryItemEventArgs.IsAllowed)))) + offset;
            Label continueLabel = (Label)newInstructions[index].operand;

            LocalBuilder ev2 = generator.DeclareLocal(typeof(UpgradingInventoryItemEventArgs));
            offset = 1;

            // index = newInstructions.FindIndex(x => x.opcode == OpCodes.Stloc_S && x.operand is LocalBuilder { LocalIndex: 10 }) + offset;
            ConstructorInfo lab_api_constructor = typeof(LabApi.Events.Arguments.Scp914Events.Scp914ProcessingInventoryItemEventArgs)
                .GetConstructor(new[]
                {
                    typeof(InventorySystem.Items.ItemBase),
                    typeof(Scp914KnobSetting),
                    typeof(ReferenceHub),
                });
            index = newInstructions.FindIndex(x => x.Is(OpCodes.Newobj, lab_api_constructor)) + offset;

            // ridtp lcz914
            // noclip
            // give tuxwonder7 47
            newInstructions.InsertRange(
                index,
                new CodeInstruction[]
                {
                    // setting = curSetting
                    new(OpCodes.Ldloc_S, curSetting.LocalIndex),
                    new(OpCodes.Starg_S, 3),

                    // Player.Get(ply)
                    new(OpCodes.Ldarg_0),
                    new(OpCodes.Call, Method(typeof(Player), nameof(Player.Get), new[] { typeof(ReferenceHub) })),

                    // itemBase
                    new(OpCodes.Ldloc_S, 7),

                    // setting
                    new(OpCodes.Ldarg_S, 3),

                    // true
                    new(OpCodes.Ldc_I4_1),

                    // UpgradingInventoryItemEventArgs ev = new(player, itemBase, setting)
                    new(OpCodes.Newobj, GetDeclaredConstructors(typeof(UpgradingInventoryItemEventArgs))[0]),
                    new(OpCodes.Dup),
                    new(OpCodes.Dup),
                    new(OpCodes.Stloc_S, ev2.LocalIndex),

                    // Handlers.Scp914.OnUpgradingInventoryItem(ev);
                    new(OpCodes.Call, Method(typeof(Scp914), nameof(Scp914.OnUpgradingInventoryItem))),

                    // if (!ev.IsAllowed)
                    //    return;
                    new(OpCodes.Callvirt, PropertyGetter(typeof(UpgradingInventoryItemEventArgs), nameof(UpgradingInventoryItemEventArgs.IsAllowed))),
                    new(OpCodes.Brfalse_S, continueLabel),

                    // setting = ev.KnobSetting
                    new(OpCodes.Ldloc_S, ev2.LocalIndex),
                    new(OpCodes.Callvirt, PropertyGetter(typeof(UpgradingInventoryItemEventArgs), nameof(UpgradingInventoryItemEventArgs.KnobSetting))),
                    new(OpCodes.Starg_S, 3),
                });

            newInstructions[newInstructions.Count - 1].labels.Add(returnLabel);

            for (int z = 0; z < newInstructions.Count; z++)
                yield return newInstructions[z];

            ListPool<CodeInstruction>.Pool.Return(newInstructions);
        }
    }
}