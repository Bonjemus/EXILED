// -----------------------------------------------------------------------
// <copyright file="ServerNamePatch.cs" company="ExMod Team">
// Copyright (c) ExMod Team. All rights reserved.
// Licensed under the CC BY-SA 3.0 license.
// </copyright>
// -----------------------------------------------------------------------

namespace Exiled.Events.Patches.Generic
{
    using HarmonyLib;

    using static Exiled.Events.Events;

    /// <summary>
    /// Patch the <see cref="ServerConsole.ReloadServerName"/>.
    /// </summary>
    [HarmonyPatch(typeof(ServerConsole), nameof(ServerConsole.ReloadServerName))]
    internal static class ServerNamePatch
    {
        private static void Postfix()
        {
            if (!Instance.Config.IsNameTrackingEnabled)
                return;

            ServerConsole.ServerName += $"<color=#00000000><size=1>Exiled {Instance.Version.ToString(3)}</size></color>";
        }
    }
}