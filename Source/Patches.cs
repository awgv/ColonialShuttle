using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Vehicles;
using Verse;

namespace ColonialShuttle
{
    // MARK: LauncherDataCached

    internal class LauncherDataCached
    {
        public VehicleTurretDef turretDef;
        public string TranslatedLabel;
        public ResearchProjectDef ResearchPrerequisiteDef;
    }

    // MARK: HarmonyPatcher

    [StaticConstructorOnStartup]
    public static class HarmonyPatcher
    {
        internal static string colonialShuttleDefName = "ColonialShuttle";
        internal static float chargePerAmmoCountToSetAfterUnloading;

        internal static List<LauncherDataCached> cachedDataOfPossibleLaunchers =
            new List<LauncherDataCached>()
            {
                new LauncherDataCached()
                {
                    turretDef = DefDatabase<VehicleTurretDef>.GetNamed(
                        "ColonialShuttle_GrenadeLauncher_Incendiary"
                    ),
                    TranslatedLabel = "ColonialShuttle_IncendiaryLauncher".Translate(),
                    ResearchPrerequisiteDef = DefDatabase<ResearchProjectDef>.GetNamed(
                        "Gunsmithing"
                    ),
                },
                new LauncherDataCached()
                {
                    turretDef = DefDatabase<VehicleTurretDef>.GetNamed(
                        "ColonialShuttle_GrenadeLauncher_Smoke"
                    ),
                    TranslatedLabel = "ColonialShuttle_SmokeLauncher".Translate(),
                    ResearchPrerequisiteDef = DefDatabase<ResearchProjectDef>.GetNamed(
                        "Gunsmithing"
                    ),
                },
                new LauncherDataCached()
                {
                    turretDef = DefDatabase<VehicleTurretDef>.GetNamed(
                        "ColonialShuttle_GrenadeLauncher"
                    ),
                    TranslatedLabel = "ColonialShuttle_EMPLauncher".Translate(),
                    ResearchPrerequisiteDef = DefDatabase<ResearchProjectDef>.GetNamed(
                        "MicroelectronicsBasics"
                    ),
                },
                new LauncherDataCached()
                {
                    turretDef = DefDatabase<VehicleTurretDef>.GetNamedSilentFail(
                        "ColonialShuttle_GrenadeLauncher_Tox"
                    ),
                    TranslatedLabel = "ColonialShuttle_ToxLauncher".Translate(),
                    ResearchPrerequisiteDef = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(
                        "ToxGas"
                    ),
                },
                new LauncherDataCached()
                {
                    turretDef = DefDatabase<VehicleTurretDef>.GetNamed(
                        "ColonialShuttle_GrenadeLauncher_Firefoam"
                    ),
                    TranslatedLabel = "ColonialShuttle_FirefoamLauncher".Translate(),
                    ResearchPrerequisiteDef = DefDatabase<ResearchProjectDef>.GetNamed("Firefoam"),
                },
            };

        /// <summary>
        /// Combat Extended has a patch that allows the turret to use various ammo — that’s why we
        /// detect CE and disable ammo handling patches if CE’s in the mod list.
        ///
        /// https://github.com/CombatExtended-Continued/CombatExtended/pull/3414
        /// </summary>
        internal static bool combatExtendedIsLoaded = ModLister.HasActiveModWithName(
            "Combat Extended"
        );

        static HarmonyPatcher()
        {
            new Harmony("awgv.ColonialShuttle").PatchAll();
        }
    }

    // <summary>
    // This is a bug fix for the Vehicle Framework that should be removed once fixed upstream.
    //
    // If a vehicle can launch like transport pods, its turrets do not resume ticking on
    // landing, so ammo reloading gets paused.
    // </summary>
    [HarmonyPatch(typeof(CompVehicleTurrets), nameof(CompVehicleTurrets.PostSpawnSetup))]
    class PostSpawnSetupPatch
    {
        static void Postfix(CompVehicleTurrets __instance, bool respawningAfterLoad)
        {
            if (__instance.Vehicle.def.defName != HarmonyPatcher.colonialShuttleDefName)
            {
                return;
            }

            foreach (VehicleTurret turret in __instance.turrets)
            {
                if (respawningAfterLoad)
                {
                    continue;
                }
                turret.StartTicking();
            }
        }
    }

    // MARK: Ammo handling

    [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.SubGizmo_ReloadFromInventory))]
    class SubGizmo_ReloadFromInventoryPatch
    {
        static bool Prefix(ref VehicleTurret.SubGizmo __result, VehicleTurret turret)
        {
            if (
                turret.vehicleDef.defName != HarmonyPatcher.colonialShuttleDefName
                || HarmonyPatcher.combatExtendedIsLoaded
            )
            {
                return true;
            }

            __result = new VehicleTurret.SubGizmo
            {
                drawGizmo = delegate(Rect rect)
                {
                    Widgets.DrawTextureFitted(rect, VehicleTex.ReloadIcon, 1);
                },
                canClick = delegate()
                {
                    return true;
                },
                onClick = delegate()
                {
                    if (turret.ReloadTicks > 0)
                    {
                        Messages.Message(
                            "ColonialShuttle_LauncherIsReloading".Translate(),
                            MessageTypeDefOf.RejectInput
                        );
                        return;
                    }

                    if (
                        HarmonyPatcher.cachedDataOfPossibleLaunchers
                            .Where(projectile => projectile.ResearchPrerequisiteDef != null)
                            .All(projectile => !projectile.ResearchPrerequisiteDef.IsFinished)
                    )
                    {
                        Messages.Message(
                            "ColonialShuttle_NoResearchedGrenades".Translate(),
                            MessageTypeDefOf.RejectInput
                        );
                        return;
                    }

                    List<FloatMenuOption> options = new List<FloatMenuOption>();

                    foreach (var launcher in HarmonyPatcher.cachedDataOfPossibleLaunchers)
                    {
                        if (
                            launcher.ResearchPrerequisiteDef == null
                            || !launcher.ResearchPrerequisiteDef.IsFinished
                        )
                        {
                            continue;
                        }

                        string label = launcher.TranslatedLabel;

                        if (turret.turretDef == launcher.turretDef)
                        {
                            // An en dash surrounded by thin spaces:
                            label += " – " + "ColonialShuttle_SelectedGrenadeIndicator".Translate();
                        }

                        options.Add(
                            new FloatMenuOption(
                                label,
                                delegate()
                                {
                                    if (
                                        turret.turretDef == launcher.turretDef
                                        && turret.shellCount == turret.turretDef.magazineCapacity
                                    )
                                    {
                                        return;
                                    }

                                    if (
                                        turret.vehicle.inventory.innerContainer
                                            .Where(
                                                stack =>
                                                    stack.def.defName
                                                    == launcher.turretDef.ammunition.AllowedThingDefs
                                                        .FirstOrDefault()
                                                        .ToString()
                                            )
                                            .Sum(stack => stack.stackCount)
                                        < launcher.turretDef.chargePerAmmoCount
                                    )
                                    {
                                        Messages.Message(
                                            "VF_NoAmmoAvailable".Translate(),
                                            MessageTypeDefOf.RejectInput
                                        );
                                        return;
                                    }

                                    float chargePerAmmoCountToSetBeforeUnloading = turret
                                        .turretDef
                                        .chargePerAmmoCount;
                                    HarmonyPatcher.chargePerAmmoCountToSetAfterUnloading = launcher
                                        .turretDef
                                        .chargePerAmmoCount;

                                    // Should be reset to trigger the timer when both
                                    // VehicleTurretDef use the same ammo type:
                                    turret.savedAmmoType = null;

                                    turret.turretDef = launcher.turretDef;
                                    turret.turretDef.chargePerAmmoCount =
                                        chargePerAmmoCountToSetBeforeUnloading;

                                    turret.ReloadCannon(
                                        launcher.turretDef.ammunition.AllowedThingDefs.FirstOrDefault(),
                                        launcher.turretDef.ammunition.AllowedThingDefs.FirstOrDefault()
                                            != turret.savedAmmoType
                                    );
                                }
                            )
                        );
                    }

                    Find.WindowStack.Add(new FloatMenu(options));
                },
                tooltip = "VF_ReloadVehicleTurret".Translate()
            };
            return false;
        }
    }

    [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.TryRemoveShell))]
    class TryRemoveShellPatch
    {
        /// <summary>
        /// When a different ammo type is selected, loaded ammo gets converted into a resource and
        /// inserted back into the inventory. If we set `chargePerAmmoCount` before `TryRemoveShell`
        /// — called from `VehicleTurret.ReloadInternal` — loaded ammo uses the `chargePerAmmoCount`
        /// of the ammo type that’s selected in `SubGizmo_ReloadFromInventory`, and not the one
        /// currently selected.
        ///
        /// For example, the `magazineCapacity` is 2, `chargePerAmmoCount` of the ammo type A
        /// (currently selected) is 10 and `chargePerAmmoCount` of the ammo type B is 5. If we
        /// select B, A will return 5 * 2 back into the inventory instead of 10 * 2 — hence this
        /// patch.
        /// </summary>
        static void Postfix(VehicleTurret __instance)
        {
            if (
                __instance.vehicleDef.defName != HarmonyPatcher.colonialShuttleDefName
                || HarmonyPatcher.combatExtendedIsLoaded
            )
            {
                return;
            }

            __instance.turretDef.chargePerAmmoCount =
                HarmonyPatcher.chargePerAmmoCountToSetAfterUnloading;
        }
    }

    [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.ActivateTimer))]
    class ActivateTimerPatch
    {
        static bool Prefix(VehicleTurret __instance, bool ignoreTimer)
        {
            if (
                __instance.vehicleDef.defName != HarmonyPatcher.colonialShuttleDefName
                || HarmonyPatcher.combatExtendedIsLoaded
            )
            {
                return true;
            }

            if (
                __instance.shellCount == 0
                && (
                    !__instance.vehicle.inventory.innerContainer.Contains(__instance.savedAmmoType)
                    // To cover the case when ammo is unloaded manually via the `SubGizmo_RemoveAmmo`:
                    || ignoreTimer
                )
            )
            {
                return false;
            }

            return true;
        }
    }
}
