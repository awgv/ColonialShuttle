using HarmonyLib;
using RimWorld;
using SmashTools;
using System.Collections.Generic;
using UnityEngine;
using Vehicles;
using Verse;

namespace ColonialShuttle
{
    internal static class Defaults
    {
        internal const string colonialShuttleDefName = "ColonialShuttle";

        /// <summary>
        /// It doesn’t affect the overall “there and back again” fuel consumption directly, i.e. 1.5
        /// is not a 50% increase, more like ~30%. It was initially 1.5f.
        /// </summary>
        internal const float fuelConsumptionRateMultiplier = 1.39f;

        /// <summary>
        /// Transport pods spend 4.54f per kg.
        /// </summary>
        internal const float fuelConsumptionRatePerKilogram = 4.54f * fuelConsumptionRateMultiplier;

        /// <summary>
        /// Dictionary keys are the keys of upgrade nodes. These are essentially percentages of
        /// `fuelConsumptionRatePerKilogram`, so make sure that no combination of upgrades allow
        /// `fuelConsumptionRatePerKilogram` to become <= zero, because it’s going to be reset to
        /// the default value.
        /// </summary>
        internal static Dictionary<string, float> fuelConsumptionRateMultipliersForUpgrades =
            new Dictionary<string, float>()
            {
                { "CargoRacksB", 0.25f },
                { "CargoRacksA", 0.5f },
                { "ThrustersC", -0.125f },
                { "ThrustersB", -0.25f },
                { "ThrustersA", -0.5f },
            };

        /// <summary>
        /// Transport pods spend 2.25 chemfuel per tile or 675f. Used when shuttles carry no cargo.
        /// </summary>
        internal const float minimumFuelConsumptionRate = 338f;
    }

    internal class ProjectileDataCached
    {
        public ThingDef ProjectileDef;
        public string TranslatedLabel;
        public ResearchProjectDef ResearchPrerequisiteDef;
        public ThingDef AmmunitionDef;
        public float ChargePerAmmoCount;

        /// <summary>
        /// Refering to the `spreadRadius` when firing in bursts only. The `spreadRadius` of single
        /// shots is 1.9f, like all other craftable launchers in the game.
        /// </summary>
        public float SpreadRadius;
    }

    [StaticConstructorOnStartup]
    public static class HarmonyPatcher
    {
        internal static float chargePerAmmoCountToSetAfterUnloading;

        internal static List<ProjectileDataCached> cachedDataOfPossibleProjectiles =
            new List<ProjectileDataCached>()
            {
                new ProjectileDataCached()
                {
                    ProjectileDef = DefDatabase<ThingDef>.GetNamed("Bullet_IncendiaryLauncher"),
                    TranslatedLabel = "ColonialShuttle_IncendiaryLauncher".Translate(),
                    ResearchPrerequisiteDef = DefDatabase<ResearchProjectDef>.GetNamed(
                        "Gunsmithing"
                    ),
                    AmmunitionDef = DefDatabase<ThingDef>.GetNamed("Chemfuel"),
                    ChargePerAmmoCount = 15,
                    SpreadRadius = 1.9f,
                },
                new ProjectileDataCached()
                {
                    ProjectileDef = DefDatabase<ThingDef>.GetNamed("Bullet_SmokeLauncher"),
                    TranslatedLabel = "ColonialShuttle_SmokeLauncher".Translate(),
                    ResearchPrerequisiteDef = DefDatabase<ResearchProjectDef>.GetNamed(
                        "Gunsmithing"
                    ),
                    AmmunitionDef = DefDatabase<ThingDef>.GetNamed("Chemfuel"),
                    ChargePerAmmoCount = 15,
                    SpreadRadius = 9.9f,
                },
                new ProjectileDataCached()
                {
                    ProjectileDef = DefDatabase<ThingDef>.GetNamed("Bullet_EMPLauncher"),
                    TranslatedLabel = "ColonialShuttle_EMPLauncher".Translate(),
                    ResearchPrerequisiteDef = DefDatabase<ResearchProjectDef>.GetNamed(
                        "MicroelectronicsBasics"
                    ),
                    AmmunitionDef = DefDatabase<ThingDef>.GetNamed("Steel"),
                    ChargePerAmmoCount = 25,
                    SpreadRadius = 2.9f,
                },
                new ProjectileDataCached()
                {
                    ProjectileDef = DefDatabase<ThingDef>.GetNamed("Bullet_ToxbombLauncher"),
                    TranslatedLabel = "ColonialShuttle_ToxLauncher".Translate(),
                    ResearchPrerequisiteDef = DefDatabase<ResearchProjectDef>.GetNamed("ToxGas"),
                    AmmunitionDef = DefDatabase<ThingDef>.GetNamed("Chemfuel"),
                    ChargePerAmmoCount = 25,
                    SpreadRadius = 9.9f,
                },
                new ProjectileDataCached()
                {
                    ProjectileDef = DefDatabase<ThingDef>.GetNamed(
                        "ColonialShuttle_Bullet_FirefoamLauncher"
                    ),
                    TranslatedLabel = "ColonialShuttle_FirefoamLauncher".Translate(),
                    ResearchPrerequisiteDef = DefDatabase<ResearchProjectDef>.GetNamed("Firefoam"),
                    AmmunitionDef = DefDatabase<ThingDef>.GetNamed("Chemfuel"),
                    ChargePerAmmoCount = 15,
                    SpreadRadius = 7.9f,
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

    // MARK: Dynamic fuel consumption rate:

    [HarmonyPatch(
        typeof(CompVehicleLauncher),
        nameof(CompVehicleLauncher.CanLaunchWithCargoCapacity)
    )]
    class CanLaunchWithCargoCapacityPatch
    {
        static bool Postfix(bool __result, ref string disableReason, CompVehicleLauncher __instance)
        {
            if (
                __instance.Vehicle.VehicleDef.defName == Defaults.colonialShuttleDefName
                && !(
                    SettingsCache.TryGetValue(
                        __instance.Vehicle.VehicleDef,
                        typeof(VehicleDef),
                        nameof(VehicleDef.vehicleMovementPermissions),
                        __instance.Vehicle.VehicleDef.vehicleMovementPermissions
                    ) > VehiclePermissions.NotAllowed
                )
            )
            {
                float capacity = __instance.Vehicle.GetStatValue(VehicleStatDefOf.CargoCapacity);
                if (MassUtility.InventoryMass(__instance.Vehicle) > capacity || capacity == 0)
                {
                    disableReason = "VF_CannotLaunchOverEncumbered".Translate(
                        __instance.Vehicle.LabelShort
                    );
                    return disableReason.NullOrEmpty();
                }
            }

            return __result;
        }
    }

    [HarmonyPatch(typeof(CompFueledTravel), "FuelEfficiency", MethodType.Getter)]
    class FuelEfficiencyPatch
    {
        static float Postfix(float __result, CompFueledTravel __instance)
        {
            if (__instance.Vehicle.VehicleDef.defName != Defaults.colonialShuttleDefName)
            {
                return __result;
            }

            float fuelConsumptionRatePerKilogramAfterUpgrades =
                Defaults.fuelConsumptionRatePerKilogram;

            foreach (var node in __instance.Vehicle.CompUpgradeTree.Props.def.nodes)
            {
                if (!__instance.Vehicle.CompUpgradeTree.NodeUnlocked(node))
                {
                    continue;
                }

                if (Defaults.fuelConsumptionRateMultipliersForUpgrades.ContainsKey(node.key))
                {
                    fuelConsumptionRatePerKilogramAfterUpgrades +=
                        Defaults.fuelConsumptionRatePerKilogram
                        * Defaults.fuelConsumptionRateMultipliersForUpgrades[node.key];
                }
            }

            if (fuelConsumptionRatePerKilogramAfterUpgrades <= 0)
            {
                fuelConsumptionRatePerKilogramAfterUpgrades =
                    Defaults.fuelConsumptionRatePerKilogram;
            }

            float totalWeightOfCargoInKilograms = MassUtility.InventoryMass(__instance.Vehicle);

            foreach (var pawn in __instance.Vehicle.AllPawnsAboard)
            {
                totalWeightOfCargoInKilograms += pawn.GetStatValue(StatDefOf.Mass);
            }

            float fuelConsumptionRate =
                totalWeightOfCargoInKilograms == 0
                    ? Defaults.minimumFuelConsumptionRate
                    : totalWeightOfCargoInKilograms * fuelConsumptionRatePerKilogramAfterUpgrades;

            return fuelConsumptionRate;
        }
    }

    // MARK: Ammo handling:

    [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.SubGizmo_ReloadFromInventory))]
    class SubGizmo_ReloadFromInventoryPatch
    {
        static bool Prefix(ref VehicleTurret.SubGizmo __result, VehicleTurret turret)
        {
            if (
                turret.vehicleDef.defName != Defaults.colonialShuttleDefName
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
                    List<FloatMenuOption> options = new List<FloatMenuOption>();

                    foreach (var projectile in HarmonyPatcher.cachedDataOfPossibleProjectiles)
                    {
                        if (!projectile.ResearchPrerequisiteDef.IsFinished)
                        {
                            continue;
                        }

                        string label = projectile.TranslatedLabel;

                        if (turret.turretDef.projectile == projectile.ProjectileDef)
                        {
                            label +=
                                " ("
                                + "ColonialShuttle_Indicator_Of_Selected_Grenade".Translate()
                                + ")";
                        }

                        options.Add(
                            new FloatMenuOption(
                                label,
                                delegate()
                                {
                                    if (
                                        turret.shellCount == turret.turretDef.magazineCapacity
                                        && turret.turretDef.projectile == projectile.ProjectileDef
                                    )
                                    {
                                        return;
                                    }

                                    turret.turretDef.projectile = projectile.ProjectileDef;
                                    turret.ReloadCannon();
                                }
                            )
                        );
                    }

                    if (options.Count == 0)
                    {
                        options.Add(
                            new FloatMenuOption(
                                "ColonialShuttle_No_Grenades_To_Choose_From".Translate(),
                                delegate() { }
                            )
                        );
                    }

                    Find.WindowStack.Add(new FloatMenu(options));
                },
                tooltip = "VF_ReloadVehicleTurret".Translate()
            };
            return false;
        }

        [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.ReloadCannon))]
        class ReloadCannonPatch
        {
            static bool Prefix(VehicleTurret __instance)
            {
                if (
                    __instance.vehicleDef.defName != Defaults.colonialShuttleDefName
                    || HarmonyPatcher.combatExtendedIsLoaded
                )
                {
                    return true;
                }

                __instance.shellCount = __instance.turretDef.magazineCapacity;
                __instance.ActivateTimer(true);
                return false;
            }
        }

        [HarmonyPatch(typeof(VehicleTurret), "HasAmmo", MethodType.Getter)]
        class HasAmmoPatch
        {
            static bool Prefix(ref bool __result, VehicleTurret __instance)
            {
                if (
                    __instance.vehicleDef.defName != Defaults.colonialShuttleDefName
                    || HarmonyPatcher.combatExtendedIsLoaded
                )
                {
                    return true;
                }

                __result = __instance.shellCount > 0;
                return false;
            }
        }

        [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.FireTurret))]
        class FireTurretPatch
        {
            static void Postfix(VehicleTurret __instance)
            {
                if (
                    __instance.vehicleDef.defName != Defaults.colonialShuttleDefName
                    || HarmonyPatcher.combatExtendedIsLoaded
                )
                {
                    return;
                }

                __instance.ConsumeShellChambered();
            }
        }
    }
}
