using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Unity.Services.Multiplayer.Editor.MigrationPath
{
    internal static class Validator
    {
        private const string k_LobbyPackageIdentifier =
            "com.unity.services.lobby";

        private const string k_MatchmakerPackageIdentifier =
            "com.unity.services.matchmaker";

        private const string k_MultiplayPackageIdentifier =
            "com.unity.services.multiplay";

        private const string k_MultiplayerSDKPackageIdentifier =
            "com.unity.services.multiplayer";

        private const string k_RelayPackageIdentifier =
            "com.unity.services.relay";

        private const string k_MigrationDocumentationURL =
            "https://docs.unity.com/ugs/en-us/manual/mps-sdk/manual/migration-path";

        private const string k_UnityMultiplayerServices =
            "Unity Multiplayer Services";

        private static readonly string[] k_IncompatiblePackageNames =
        {
            k_LobbyPackageIdentifier, k_MatchmakerPackageIdentifier,
            k_MultiplayPackageIdentifier, k_RelayPackageIdentifier
        };

        [InitializeOnLoadMethod]
        private static void SubscribeToPackageManagerEvents()
        {
            Events.registeredPackages -= OnRegisteredPackages;
            Events.registeredPackages += OnRegisteredPackages;
        }

        private static string WarningMessage(
            in ReadOnlyCollection<PackageInfo> items)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(
                $"The following {(items.Count == 1 ? "package has" : "packages have")} been added:");
            foreach (var packageInfo in items)
            {
                stringBuilder.AppendLine(
                    $"\t- {packageInfo.displayName} ({packageInfo.name}) version {packageInfo.version}");
            }

            stringBuilder.AppendLine(
                $"However, {(items.Count == 1 ? "it is" : "they are")} incompatible with the " +
                "Unity Multiplayer Service SDK.");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine(
                $"Please remove the following {(items.Count == 1 ? "package" : "packages")}:");
            foreach (var packageInfo in items)
            {
                stringBuilder.AppendLine(
                    $"\t- {packageInfo.displayName} ({packageInfo.name}) version {packageInfo.version}");
            }

            stringBuilder.AppendLine(
                "If you wish to use the Unity Multiplayer Services SDK.");
            return stringBuilder.ToString();
        }

        private static string WarningMessageWithDependency(
            in ReadOnlyCollection<IGrouping<PackageInfo, DependencyInfo>>
            packages)
        {
            var stringBuilder = new StringBuilder();
            foreach (var group in packages)
            {
                stringBuilder.AppendLine(
                    $"The following {(packages.Count == 1 ? "package has an" : "packages have")} incompatible {(group.Count() > 1 ? "dependencies" : "dependency")} with the Multipalyer Services SDK:");
                stringBuilder.AppendLine(
                    $"\t- {group.Key.name} version {group.Key.version}");

                stringBuilder.AppendLine(
                    $"The incompatible {(group.Count() > 1 ? "dependencies are" : "dependency is")}:");
                foreach (var dependency in group)
                {
                    stringBuilder.AppendLine(
                        $"\t- {dependency.name} version {dependency.version}");
                }
            }

            stringBuilder.AppendLine();
            stringBuilder.AppendLine(
                $"Please remove the following {(packages.Count == 1 ? "package" : "packages")}:");
            foreach (var group in packages)
            {
                stringBuilder.AppendLine(
                    $"\t- {group.Key.displayName} ({group.Key.name}) version {group.Key.version}");
            }

            stringBuilder.AppendLine(
                "If you wish to use the Unity Multiplayer Services SDK.");
            return stringBuilder.ToString();
        }

        private static void OnRegisteredPackages(
            PackageRegistrationEventArgs eventArgs)
        {
            try
            {
                var isMultiplayerSDKPackageRemoved = eventArgs.removed
                    .Select(packageInfo => packageInfo.name)
                    .Contains(k_MultiplayerSDKPackageIdentifier);
                if (isMultiplayerSDKPackageRemoved)
                {
                    Events.registeredPackages -= OnRegisteredPackages;
                    return;
                }

                var compatibilityInfo = CheckCompatibility(eventArgs.added);

                if (compatibilityInfo.IsCompatible)
                {
                    return;
                }

                var message = compatibilityInfo.PackageWithDependencies.Any()
                    ? WarningMessageWithDependency(compatibilityInfo
                    .PackageWithDependencies)
                    : WarningMessage(compatibilityInfo
                    .PackageWithoutDependencies);
                Debug.LogWarning(message);

                var choice = EditorUtility.DisplayDialogComplex(
                    k_UnityMultiplayerServices,
                    message, "Ok", "Close", "Help");
                if (choice == 2)
                {
                    Application.OpenURL(k_MigrationDocumentationURL);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"Error processing package registration event: {e.Message}");
            }
        }

        internal static CompatibilityInfo CheckCompatibility(
            in ReadOnlyCollection<PackageInfo> added)
        {
            var incompatiblePackages = added
                .Where(packageInfo =>
                k_IncompatiblePackageNames.Contains(packageInfo.name))
                .ToList();

            var incompatibleDependentPackages = added.SelectMany(
                packageInfo =>
                    packageInfo.dependencies
                        .Where(dependency =>
                            k_IncompatiblePackageNames.Contains(dependency
                                .name))
                        .Select(dependency => new
                        {
                            Package = packageInfo,
                            Dependency = dependency
                        }))
                .GroupBy(tuple => tuple.Package, tuple => tuple.Dependency)
                .Where(group => group.Any())
                .ToList();

            return new CompatibilityInfo(
                incompatibleDependentPackages.AsReadOnly(),
                incompatiblePackages.AsReadOnly());
        }

        internal readonly struct CompatibilityInfo
        {
            public readonly
            ReadOnlyCollection<IGrouping<PackageInfo, DependencyInfo>>
            PackageWithDependencies;

            public readonly ReadOnlyCollection<PackageInfo>
            PackageWithoutDependencies;

            public bool IsCompatible =>
                !PackageWithDependencies.Any() &&
                !PackageWithoutDependencies.Any();

            public CompatibilityInfo(
                ReadOnlyCollection<IGrouping<PackageInfo, DependencyInfo>>
                packageWithDependencies,
                ReadOnlyCollection<PackageInfo> packageWithoutDependencies)
            {
                PackageWithDependencies = packageWithDependencies;
                PackageWithoutDependencies = packageWithoutDependencies;
            }
        }
    }
}
