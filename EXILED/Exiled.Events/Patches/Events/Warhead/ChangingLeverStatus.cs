// -----------------------------------------------------------------------
// <copyright file="ChangingLeverStatus.cs" company="ExMod Team">
// Copyright (c) ExMod Team. All rights reserved.
// Licensed under the CC BY-SA 3.0 license.
// </copyright>
// -----------------------------------------------------------------------

namespace Exiled.Events.Patches.Events.Warhead
{
    using System.Collections.Generic;
    using System.Reflection;
    using System.Reflection.Emit;

    using API.Features;
    using API.Features.Pools;
    using Exiled.Events.Attributes;
    using Exiled.Events.EventArgs.Warhead;

    using HarmonyLib;

    using static HarmonyLib.AccessTools;

    using Warhead = Handlers.Warhead;

    /// <summary>
    /// Patches <see cref="AlphaWarheadNukesitePanel.ServerInteract" />.
    /// Adds the <see cref="Warhead.ChangingLeverStatus" /> event.
    /// </summary>
    [EventPatch(typeof(Warhead), nameof(Warhead.ChangingLeverStatus))]

    [HarmonyPatch(typeof(AlphaWarheadNukesitePanel), nameof(AlphaWarheadNukesitePanel.ServerInteract))]
    internal static class ChangingLeverStatus
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            List<CodeInstruction> newInstructions = ListPool<CodeInstruction>.Pool.Get(instructions);

            Label returnLabel = generator.DefineLabel();

            int index = newInstructions.FindIndex(x => x.operand == (object)PropertySetter(typeof(AlphaWarheadNukesitePanel), nameof(AlphaWarheadNukesitePanel.Networkenabled))) - 5;

            newInstructions.InsertRange(
                index,
                new[]
                {
                    // Player.Get(component)
                    new CodeInstruction(OpCodes.Ldarg_1).MoveLabelsFrom(newInstructions[index]),
                    new(OpCodes.Call, Method(typeof(Player), nameof(Player.Get), new[] { typeof(ReferenceHub) })),

                    // nukeside.Networkenabled
                    new(OpCodes.Ldarg_0),
                    new(OpCodes.Call, PropertyGetter(typeof(AlphaWarheadNukesitePanel), nameof(AlphaWarheadNukesitePanel.Networkenabled))),

                    // true
                    new(OpCodes.Ldc_I4_1),

                    // ChangingLeverStatusEventArgs ev = new(Player, bool, bool)
                    new(OpCodes.Newobj, GetDeclaredConstructors(typeof(ChangingLeverStatusEventArgs))[0]),
                    new(OpCodes.Dup),

                    // Warhead.OnChangingLeverStatus(ev)
                    new(OpCodes.Call, Method(typeof(Warhead), nameof(Warhead.OnChangingLeverStatus))),

                    // if (!ev.IsAllowed)
                    //    return;
                    new(OpCodes.Callvirt, PropertyGetter(typeof(ChangingLeverStatusEventArgs), nameof(ChangingLeverStatusEventArgs.IsAllowed))),
                    new(OpCodes.Brfalse_S, returnLabel),
                });

            newInstructions[newInstructions.Count - 1].WithLabels(returnLabel);

            for (int z = 0; z < newInstructions.Count; z++)
                yield return newInstructions[z];

            ListPool<CodeInstruction>.Pool.Return(newInstructions);
        }
    }
}