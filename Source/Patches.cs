using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using SmashTools;
using Vehicles;
using Verse;

namespace ColonialShuttle
{
    internal static class Defaults
    {
        internal const string colonialShuttleDefName = "ColonialShuttle";

        internal const float fuelConsumptionRateMultiplier = 1.5f;

        /// <summary>
        /// Transport pods spend 4.54f per kg.
        /// </summary>
        internal const float fuelConsumptionRatePerKilogram = 4.54f * fuelConsumptionRateMultiplier;

        /// <summary>
        /// Dictionary keys are the keys of upgrade nodes. These are essentially
        /// percentages of `fuelConsumptionRatePerKilogram`, so make sure that
        /// no combination of upgrades allow `fuelConsumptionRatePerKilogram` to
        /// become <= zero, because it’s going to be reset to the default value.
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
    }

    [StaticConstructorOnStartup]
    public static class HarmonyPatcher
    {
        static HarmonyPatcher()
        {
            new Harmony("awgv.ColonialShuttle").PatchAll();
        }
    }

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
                totalWeightOfCargoInKilograms * fuelConsumptionRatePerKilogramAfterUpgrades;

            return fuelConsumptionRate;
        }
    }
}
