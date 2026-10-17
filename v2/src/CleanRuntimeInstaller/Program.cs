using System;
using System.IO;

namespace Aion2TR.V2.RuntimeInstaller;

public class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("AION2TR V2 — SAFE RUNTIME TEST INSTALLER");
        Console.WriteLine("==================================================");

        string command = args.Length > 0 ? args[0].ToLowerInvariant() : "preflight";
        string? customGameRoot = null;
        string? customBackupDir = null;
        string artifactChoice = "step8";

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].Equals("--game-root", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                customGameRoot = args[++i];
            }
            else if (args[i].Equals("--backup-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                customBackupDir = args[++i];
            }
            else if (args[i].Equals("--artifact", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                artifactChoice = args[++i].ToLowerInvariant();
            }
        }

        ArtifactDescriptor descriptor = artifactChoice == "step3" ? ArtifactDescriptor.Step3Exact : ArtifactDescriptor.Step8Full;

        var engine = new RuntimeInstallerEngine(artifact: descriptor);
        Console.WriteLine($"Repo Root: {engine.RepoRoot}");
        Console.WriteLine($"Active Artifact: {descriptor.ArtifactId} ({descriptor.DisplayName})");
        Console.WriteLine($"Candidate DAT: {engine.DatArtifactPath}");
        Console.WriteLine($"Stub PAK: {engine.StubPakPath}");

        switch (command)
        {
            case "preflight":
                return RunPreflightCommand(engine, customGameRoot);

            case "status":
                return RunStatusCommand(engine, customGameRoot);

            case "install":
                return RunInstallCommand(engine, customGameRoot);

            case "upgrade":
                return RunUpgradeCommand(engine, customGameRoot);

            case "repair":
                return RunRepairCommand(engine, customGameRoot);

            case "rollback":
                return RunRollbackCommand(engine, customGameRoot, customBackupDir);

            case "dry-run":
                return RunDryRunCommand(engine);

            default:
                Console.WriteLine($"Unknown command: '{command}'");
                Console.WriteLine("Available commands: preflight, status, install, upgrade, repair, rollback, dry-run");
                return 1;
        }
    }

    private static int RunPreflightCommand(RuntimeInstallerEngine engine, string? gameRoot)
    {
        Console.WriteLine("\n[COMMAND: PREFLIGHT (READ-ONLY)]");
        var report = engine.RunPreflight(gameRoot);

        Console.WriteLine($"Game Root: {report.GameRoot}");
        Console.WriteLine($"Is Game Running: {report.IsGameRunning}");
        Console.WriteLine($"Localization PAK Found: {report.OfficialPakFound} (Size: {report.OfficialPakSize:N0} bytes, SHA-256: {report.OfficialPakHash})");
        Console.WriteLine($"Is Official PAK Verified: {report.IsPakOfficialVerified}");
        Console.WriteLine($"Is Stub PAK Verified: {report.IsPakStubVerified}");
        Console.WriteLine($"SIG Found: {report.SigFound} (Size: {report.SigSize:N0} bytes)");
        Console.WriteLine($"UCAS Found: {report.UcasFound} (Size: {report.UcasSize:N0} bytes)");
        Console.WriteLine($"UTOC Found: {report.UtocFound} (Size: {report.UtocSize:N0} bytes)");
        Console.WriteLine($"Existing Loose DAT: {report.ExistingLooseDatFound} (Size: {report.ExistingLooseDatSize:N0} bytes)");
        Console.WriteLine($"Candidate DAT Verified: {report.DatHashValid} (Hash: {report.DatHash})");
        Console.WriteLine($"Stub PAK Verified: {report.StubHashValid} (Hash: {report.StubHash})");
        Console.WriteLine($"Free Disk Space: {(report.FreeDiskSpaceBytes >= 0 ? $"{report.FreeDiskSpaceBytes / 1024 / 1024:N0} MB" : "Unknown")}");
        Console.WriteLine($"Can Install: {report.CanInstall}");

        if (report.Issues.Count > 0)
        {
            Console.WriteLine("\nIssues / Warnings:");
            foreach (var issue in report.Issues)
            {
                Console.WriteLine($"  - {issue}");
            }
        }

        Console.WriteLine($"\nPreflight Result: {(report.CanInstall ? "READY FOR INSTALL" : "NOT READY")}");
        return report.CanInstall ? 0 : 1;
    }

    private static int RunStatusCommand(RuntimeInstallerEngine engine, string? gameRoot)
    {
        Console.WriteLine("\n[COMMAND: STATUS (READ-ONLY)]");
        var status = engine.GetStatus(gameRoot);

        Console.WriteLine($"Game Root: {status.GameRoot}");
        Console.WriteLine($"Status Code: {status.Code}");
        Console.WriteLine($"Message: {status.Message}");
        Console.WriteLine($"Active Artifact: {status.ActiveArtifactId ?? "None"}");
        Console.WriteLine($"Loose DAT Present: {status.LooseDatPresent} (Hash: {status.LooseDatHash ?? "N/A"})");
        Console.WriteLine($"PAK Present: {status.PakPresent} (Is Stub: {status.IsPakStub}, Is Official: {status.IsOfficialPak}, Hash: {status.PakHash ?? "N/A"})");
        Console.WriteLine($"Companions Present (SIG: {status.SigPresent}, UCAS: {status.UcasPresent}, UTOC: {status.UtocPresent})");

        return status.Code == RuntimeStatusCode.INSTALLED_VALID || status.Code == RuntimeStatusCode.NOT_INSTALLED ? 0 : 2;
    }

    private static int RunInstallCommand(RuntimeInstallerEngine engine, string? gameRoot)
    {
        Console.WriteLine("\n[COMMAND: INSTALL]");
        var preflight = engine.RunPreflight(gameRoot);
        if (!preflight.CanInstall)
        {
            Console.WriteLine("Preflight failed! Cannot install.");
            foreach (var issue in preflight.Issues)
            {
                Console.WriteLine($"  - {issue}");
            }
            return 1;
        }

        try
        {
            Console.WriteLine($"Executing install transaction for {engine.Artifact.ArtifactId} with automatic rollback safeguard...");
            var state = engine.ExecuteInstall(preflight);
            Console.WriteLine("INSTALL COMPLETED SUCCESSFULLY!");
            Console.WriteLine($"Backup saved to: {state.BackupDirectory}");
            Console.WriteLine($"State recorded to: {engine.StateFilePath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"INSTALL FAILED: {ex.Message}");
            return 1;
        }
    }

    private static int RunUpgradeCommand(RuntimeInstallerEngine engine, string? gameRoot)
    {
        Console.WriteLine("\n[COMMAND: UPGRADE]");
        try
        {
            Console.WriteLine($"Executing upgrade transaction to {engine.Artifact.ArtifactId} with automatic rollback safeguard...");
            var state = engine.ExecuteUpgrade(gameRoot);
            Console.WriteLine("UPGRADE COMPLETED SUCCESSFULLY!");
            Console.WriteLine($"Backup snapshot saved to: {state.BackupDirectory}");
            Console.WriteLine($"State recorded to: {engine.StateFilePath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UPGRADE FAILED: {ex.Message}");
            return 1;
        }
    }

    private static int RunRepairCommand(RuntimeInstallerEngine engine, string? gameRoot)
    {
        Console.WriteLine("\n[COMMAND: REPAIR]");
        try
        {
            Console.WriteLine("Executing automated repair transaction to restore clean official game baseline...");
            bool ok = engine.ExecuteRepair(gameRoot);
            Console.WriteLine($"REPAIR COMPLETED: {ok}");
            var status = engine.GetStatus(gameRoot);
            Console.WriteLine($"Post-Repair Status: {status.Code} - {status.Message}");
            return ok && status.Code == RuntimeStatusCode.NOT_INSTALLED ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"REPAIR FAILED: {ex.Message}");
            return 1;
        }
    }

    private static int RunRollbackCommand(RuntimeInstallerEngine engine, string? gameRoot, string? backupDir)
    {
        Console.WriteLine("\n[COMMAND: ROLLBACK]");
        try
        {
            bool ok = engine.ExecuteRollback(gameRoot, backupDir);
            Console.WriteLine($"ROLLBACK COMPLETED: {ok}");
            var status = engine.GetStatus(gameRoot);
            Console.WriteLine($"Post-Rollback Status: {status.Code} - {status.Message}");
            return ok && status.Code == RuntimeStatusCode.NOT_INSTALLED ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ROLLBACK FAILED: {ex.Message}");
            return 1;
        }
    }

    private static int RunDryRunCommand(RuntimeInstallerEngine engine)
    {
        Console.WriteLine("\n[COMMAND: DRY-RUN SIMULATED LIFECYCLE]");
        string tempDir = Path.Combine(Path.GetTempPath(), "Aion2_DryRun_" + Guid.NewGuid().ToString("N"));
        try
        {
            Console.WriteLine($"Creating mock game environment at: {tempDir}");
            Directory.CreateDirectory(tempDir);

            string paksDir = Path.Combine(tempDir, "Aion2", "Content", "Paks", "L10N", "Text", "en-US");
            string l10nDir = Path.Combine(tempDir, "Aion2", "Content", "L10N", "Text", "en-US");
            Directory.CreateDirectory(paksDir);
            Directory.CreateDirectory(l10nDir);

            string pakFile = Path.Combine(paksDir, "pakchunk502000-Windows_0_P.pak");
            string sigFile = Path.Combine(paksDir, "pakchunk502000-Windows_0_P.sig");
            string ucasFile = Path.Combine(paksDir, "pakchunk502000-Windows_0_P.ucas");
            string utocFile = Path.Combine(paksDir, "pakchunk502000-Windows_0_P.utoc");

            // Create official dummy files matching official hashes
            File.WriteAllBytes(pakFile, [1, 2, 3, 4, 5]);
            File.WriteAllBytes(sigFile, [10, 11]);
            File.WriteAllBytes(ucasFile, [20, 21]);
            File.WriteAllBytes(utocFile, [30, 31]);

            var customDescriptor = new ArtifactDescriptor
            {
                ArtifactId = "TEST_ARTIFACT",
                DisplayName = "Test Simulated Artifact",
                ExpectedOfficialPakHash = RuntimeInstallerEngine.ComputeSha256Hex(pakFile),
                ExpectedOfficialPakSize = 5,
                ExpectedOfficialSigHash = RuntimeInstallerEngine.ComputeSha256Hex(sigFile),
                ExpectedOfficialUcasHash = RuntimeInstallerEngine.ComputeSha256Hex(ucasFile),
                ExpectedOfficialUtocHash = RuntimeInstallerEngine.ComputeSha256Hex(utocFile),
                ExpectedDatHash = engine.Artifact.ExpectedDatHash,
                ExpectedDatSize = engine.Artifact.ExpectedDatSize,
                ExpectedStubHash = engine.Artifact.ExpectedStubHash,
                ExpectedStubSize = engine.Artifact.ExpectedStubSize
            };

            var dryEngine = new RuntimeInstallerEngine(
                processDetector: new MockProcessDetector(false),
                artifact: customDescriptor,
                customRepoRoot: engine.RepoRoot,
                customDatPath: engine.DatArtifactPath,
                customStubPakPath: engine.StubPakPath,
                customBackupRoot: Path.Combine(tempDir, "backups"),
                customStateFilePath: Path.Combine(tempDir, "install_state.json")
            );

            // 1. Status before
            var statusBefore = dryEngine.GetStatus(tempDir);
            Console.WriteLine($"Status Before: {statusBefore.Code} (Expected: NOT_INSTALLED)");

            // 2. Preflight
            var pre = dryEngine.RunPreflight(tempDir);
            Console.WriteLine($"Preflight: CanInstall={pre.CanInstall}");

            // 3. Install
            var installState = dryEngine.ExecuteInstall(pre);
            Console.WriteLine($"Install: Status={installState.Status}, BackupDir={installState.BackupDirectory}");

            // 4. Status after install
            var statusAfterInstall = dryEngine.GetStatus(tempDir);
            Console.WriteLine($"Status After Install: {statusAfterInstall.Code} (Expected: INSTALLED_VALID)");

            // 5. Upgrade (same or updated)
            var upgradeState = dryEngine.ExecuteUpgrade(tempDir);
            Console.WriteLine($"Upgrade: Status={upgradeState.Status}");

            // 6. Rollback
            bool rollbackOk = dryEngine.ExecuteRollback(tempDir, installState.BackupDirectory);
            Console.WriteLine($"Rollback: Success={rollbackOk}");

            // 7. Status after rollback
            var statusAfterRollback = dryEngine.GetStatus(tempDir);
            Console.WriteLine($"Status After Rollback: {statusAfterRollback.Code} (Expected: NOT_INSTALLED)");

            // 8. Test Repair on simulated mixed state
            // Corrupt environment into mixed state (stub pak + companion files)
            File.Copy(engine.StubPakPath, pakFile, overwrite: true);
            File.WriteAllBytes(sigFile, [10, 11]);
            File.WriteAllBytes(Path.Combine(l10nDir, "L10NString.dat"), [99, 98, 97]);

            var mixedStatus = dryEngine.GetStatus(tempDir);
            Console.WriteLine($"Simulated Corrupted State: {mixedStatus.Code} (Expected: MIXED_STATE)");

            bool repairOk = dryEngine.ExecuteRepair(tempDir);
            Console.WriteLine($"Repair: Success={repairOk}");

            var statusAfterRepair = dryEngine.GetStatus(tempDir);
            Console.WriteLine($"Status After Repair: {statusAfterRepair.Code} (Expected: NOT_INSTALLED)");

            bool dryRunPassed = statusBefore.Code == RuntimeStatusCode.NOT_INSTALLED &&
                                pre.CanInstall &&
                                statusAfterInstall.Code == RuntimeStatusCode.INSTALLED_VALID &&
                                rollbackOk &&
                                statusAfterRollback.Code == RuntimeStatusCode.NOT_INSTALLED &&
                                mixedStatus.Code == RuntimeStatusCode.MIXED_STATE &&
                                repairOk &&
                                statusAfterRepair.Code == RuntimeStatusCode.NOT_INSTALLED;

            Console.WriteLine($"\nDRY-RUN RESULT: {(dryRunPassed ? "PASSED" : "FAILED")}");
            return dryRunPassed ? 0 : 1;
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }
}

public class MockProcessDetector : IProcessDetector
{
    private readonly bool _isRunning;
    public MockProcessDetector(bool isRunning) => _isRunning = isRunning;
    public bool IsGameRunning() => _isRunning;
}
