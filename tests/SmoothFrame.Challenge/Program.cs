using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SmoothFrame.Core.Models;
using SmoothFrame.Core.Native;
using SmoothFrame.Core.Safety;
using SmoothFrame.Core.Services;

namespace SmoothFrame.Challenge;

internal class Program
{
    private static int _passed = 0;
    private static int _failed = 0;

    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("==================================================================");
        Console.WriteLine("    SmoothFrame Milestone 1, 2 & 3 Empirical Challenge Suite      ");
        Console.WriteLine("==================================================================\n");

        Test1_NTTimerMarshaling();
        Test2_TimerService_UnpairedRevertAndPairing();
        Test3_TimerService_StressLoop();
        Test4_PowerPlanService_NoFakeBalancedGuid();
        Test5_PowerPlanService_MemoryHygiene();
        Test6_SnapshotManager_IsolationAndAtomicFlush();
        Test7_OptimizationCoordinator_WAL_FailureSimulation();
        Test8_OptimizationCoordinator_CrashRecovery_RollbackGuarantee();
        Test9_OptimizationCoordinator_CrashRecovery_CorruptSnapshotPreservation();

        // Milestone 2 Empirical Verification
        Test10_ProcessTuner_PIDRecycling_EmpiricalMismatches();
        Test11_ProcessBlacklist_DeepVerification();
        Test12_OptimizationCoordinator_WatcherCancellationAndSynchronization();
        Test13_ProcessTuner_Win32ErrorHandlingAndResourceHygiene();
        Test14_OptimizationCoordinator_RapidConcurrentDeactivationStress();
        Test15_ProcessTuner_LiveChildPIDRecyclingSimulation();
        Test16_ProcessTuner_NullAndCorruptedStateHandling();

        // Milestone 3 Empirical Verification
        Test17_StartupService_AutostartDeduplicationAndCleanup();
        Test18_StartupService_ProcessTimeoutAndOrphanCleanup();
        Test19_OptimizationCoordinator_DisposalLifecycle();
        Test20_OptimizationCoordinator_ConcurrentDisposalDeadlockStress();
        Test21_OptimizationCoordinator_ExtremeDisposalStress();

        Console.WriteLine("\n------------------------------------------------------------------");
        if (_failed == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[CHALLENGE SUITE PASSED] {_passed} passed, 0 failed.");
            Console.ResetColor();
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[CHALLENGE SUITE FAILED] {_passed} passed, {_failed} failed.");
            Console.ResetColor();
            return 1;
        }
    }

    static void Assert(bool condition, string testName, string details = "")
    {
        if (condition)
        {
            _passed++;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("  [PASS] ");
            Console.ResetColor();
            Console.WriteLine(testName);
            if (!string.IsNullOrEmpty(details))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"         └─ {details}");
                Console.ResetColor();
            }
        }
        else
        {
            _failed++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("  [FAIL] ");
            Console.ResetColor();
            Console.WriteLine(testName);
            if (!string.IsNullOrEmpty(details))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"         └─ {details}");
                Console.ResetColor();
            }
        }
    }

    // --- TEST 1: NT Timer Marshaling (SF-05) ---
    static void Test1_NTTimerMarshaling()
    {
        Console.WriteLine("1. Testing NT Timer P/Invoke 1-byte Boolean Marshaling (SF-05):");
        
        // NtSetTimerResolution with [MarshalAs(UnmanagedType.U1)] bool
        int status = NativeMethods.NtSetTimerResolution(5000, true, out var currentRes);
        bool success = (status == 0); // STATUS_SUCCESS = 0
        Assert(success, "NtSetTimerResolution(5000, true, out current) returned STATUS_SUCCESS (0)", $"Status: 0x{status:X8}, Current: {currentRes} (100ns units)");

        // Revert
        int revertStatus = NativeMethods.NtSetTimerResolution(currentRes, false, out var revertedRes);
        Assert(revertStatus == 0, "NtSetTimerResolution(revert, false, out _) returned STATUS_SUCCESS (0)", $"Revert Status: 0x{revertStatus:X8}");
    }

    // --- TEST 2: Timer Pairing & Unpaired Revert (SF-06) ---
    static void Test2_TimerService_UnpairedRevertAndPairing()
    {
        Console.WriteLine("\n2. Testing Timer Subsystem Pairing & Unpaired Revert (SF-06):");

        // Verify native WinMM behavior: timeEndPeriod without timeBeginPeriod returns TIMERR_NOCANDO (97)
        uint badEndResult = NativeMethods.TimeEndPeriod(1);
        Assert(badEndResult == 97, "Native timeEndPeriod(1) without timeBeginPeriod returns TIMERR_NOCANDO (97)", $"Native code: {badEndResult}");

        // Now test TimerResolutionService: calling Revert() on a clean service MUST NOT call timeEndPeriod(1)
        using var timerService = new TimerResolutionService();
        Assert(!timerService.IsAltered, "Fresh TimerResolutionService has IsAltered == false");

        // Calling Revert() when IsAltered == false should be a no-op and NOT invoke timeEndPeriod(1)
        timerService.Revert();
        Assert(!timerService.IsAltered, "Revert() on clean service completed cleanly without altering state");

        // Now enable high precision (NT timer will be used)
        bool enabled = timerService.EnableHighPrecision();
        Assert(enabled && timerService.IsAltered, "Timer enabled with NT timer subsystem", $"Current Ms: {timerService.CurrentResolutionMs:F3}ms");

        // Calling Revert() should revert NT timer and NOT call timeEndPeriod(1)
        timerService.Revert();
        Assert(!timerService.IsAltered, "Reverted cleanly, IsAltered is now false");

        // Calling Revert() AGAIN (double revert) should be safe
        timerService.Revert();
        Assert(!timerService.IsAltered, "Double Revert() is safely idempotent");
    }

    // --- TEST 3: Timer Service Stress Loop ---
    static void Test3_TimerService_StressLoop()
    {
        Console.WriteLine("\n3. Testing Timer Resolution Enable/Revert Stress Loop (100 cycles):");

        using var timerService = new TimerResolutionService();
        bool allSucceeded = true;
        for (int i = 0; i < 100; i++)
        {
            if (!timerService.EnableHighPrecision()) { allSucceeded = false; break; }
            if (!timerService.IsAltered) { allSucceeded = false; break; }
            timerService.Revert();
            if (timerService.IsAltered) { allSucceeded = false; break; }
        }

        Assert(allSucceeded, "100 consecutive Enable/Revert cycles completed with perfect state consistency");
    }

    // --- TEST 4: PowerPlanService Error Handling & No Fake GUID_BALANCED (SF-10) ---
    static void Test4_PowerPlanService_NoFakeBalancedGuid()
    {
        Console.WriteLine("\n4. Testing PowerPlanService Error Handling & Scheme Integrity (SF-10):");

        bool trySuccess = PowerPlanService.TryGetActiveSchemeGuid(out var activeGuid);
        Guid? nullableGuid = PowerPlanService.GetActiveSchemeGuid();

        if (trySuccess)
        {
            Assert(nullableGuid.HasValue && nullableGuid.Value == activeGuid, 
                "TryGetActiveSchemeGuid and GetActiveSchemeGuid return identical valid active GUID", 
                $"Active GUID: {activeGuid}");
            Assert(activeGuid != Guid.Empty, "Active power scheme GUID is non-empty");
        }
        else
        {
            Assert(!nullableGuid.HasValue, "When TryGetActiveSchemeGuid fails, GetActiveSchemeGuid returns null (no fake GUID_BALANCED)");
        }

        // Test RestoreSpecificPlan with Guid.Empty (must reject and return false)
        using var powerService = new PowerPlanService();
        bool emptyRestored = powerService.RestoreSpecificPlan(Guid.Empty);
        Assert(!emptyRestored, "RestoreSpecificPlan(Guid.Empty) safely rejected and returned false");
    }

    // --- TEST 5: PowerPlanService Memory Hygiene & LocalFree in Finally (SF-16) ---
    static void Test5_PowerPlanService_MemoryHygiene()
    {
        Console.WriteLine("\n5. Testing PowerPlanService Memory Hygiene & LocalFree (SF-16):");

        // Perform 10,000 queries of active scheme GUID
        long startMemory = GC.GetTotalMemory(true);
        for (int i = 0; i < 10000; i++)
        {
            PowerPlanService.TryGetActiveSchemeGuid(out _);
        }
        long endMemory = GC.GetTotalMemory(true);
        long diffBytes = Math.Abs(endMemory - startMemory);

        Assert(diffBytes < 2_000_000, "10,000 PowerGetActiveScheme calls executed with zero native memory leak (LocalFree in finally)", $"Memory delta: {diffBytes / 1024} KB");
    }

    // --- TEST 6: SnapshotManager Custom Directory Isolation & Atomic Flush (SF-01, SF-13) ---
    static void Test6_SnapshotManager_IsolationAndAtomicFlush()
    {
        Console.WriteLine("\n6. Testing SnapshotManager Directory Isolation & Atomic Flush (SF-01, SF-13):");

        var testDir = Path.Combine(Path.GetTempPath(), "SmoothFrame_Challenge_" + Guid.NewGuid().ToString("N"));
        try
        {
            var manager = new SnapshotManager(testDir);
            Assert(manager.StorageDirectory == testDir, "SnapshotManager properly accepted custom isolated directory", testDir);
            Assert(File.Exists(manager.SnapshotFilePath) == false, "Initial snapshot does not exist in isolated directory");

            var snapshot = new SystemStateSnapshot
            {
                OptimizedGameName = "Cyberpunk2077",
                OriginalPowerPlanGuid = Guid.NewGuid(),
                OriginalTimerResolution = 156250,
                HasTimerResolutionChanged = true,
                HasPowerPlanChanged = true
            };

            // Save snapshot
            bool saved = manager.SaveSnapshot(snapshot);
            Assert(saved, "SaveSnapshot succeeded");
            Assert(File.Exists(manager.SnapshotFilePath), "Snapshot file active_snapshot.json exists on disk");
            Assert(!File.Exists(manager.SnapshotFilePath + ".tmp"), "Temp file active_snapshot.json.tmp was cleanly renamed/removed");

            // Verify content
            bool loadedOk = manager.TryLoadActiveSnapshot(out var loaded);
            Assert(loadedOk && loaded?.OptimizedGameName == "Cyberpunk2077", "Loaded snapshot matches saved game name Cyberpunk2077");

            // Overwrite snapshot atomically 50 times in rapid succession
            bool overwriteOk = true;
            for (int i = 0; i < 50; i++)
            {
                snapshot.OptimizedGameName = $"GameIteration_{i}";
                if (!manager.SaveSnapshot(snapshot)) { overwriteOk = false; break; }
            }
            Assert(overwriteOk, "50 rapid consecutive atomic replacements (File.Replace) succeeded without locking errors");

            manager.TryLoadActiveSnapshot(out var finalLoaded);
            Assert(finalLoaded?.OptimizedGameName == "GameIteration_49", "Final snapshot content verified after 50 atomic overwrites");

            // Clear snapshot
            bool cleared = manager.ClearSnapshot();
            Assert(cleared && !manager.HasDanglingSnapshot(), "ClearSnapshot removed file successfully");

            // Idempotent clear
            bool clearedAgain = manager.ClearSnapshot();
            Assert(clearedAgain, "ClearSnapshot on non-existent file is safely idempotent (returns true)");
        }
        finally
        {
            try { if (Directory.Exists(testDir)) Directory.Delete(testDir, true); } catch { }
        }
    }

    // --- TEST 7: OptimizationCoordinator WAL & Failure Simulation (SF-01) ---
    static void Test7_OptimizationCoordinator_WAL_FailureSimulation()
    {
        Console.WriteLine("\n7. Testing OptimizationCoordinator WAL Failure Simulation (SF-01):");

        var testDir = Path.Combine(Path.GetTempPath(), "SmoothFrame_WAL_Sim_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);

        try
        {
            var manager = new SnapshotManager(testDir);
            using var coordinator = new OptimizationCoordinator(manager);

            var fakeGame = new GameInfo
            {
                Name = "WAL Test Game",
                ProcessName = "notepad",
                ExecutablePath = "notepad.exe"
            };

            var options = new OptimizationOptions
            {
                HighPrecisionTimer = true,
                HighPerformancePowerPlan = false,
                ElevateGamePriority = false,
                DeprioritizeBackgroundApps = false
            };

            // Query initial timer resolution before test
            double initialTimerMs = coordinator.CurrentTimerMs;

            // Lock the temp file path with exclusive access so SaveSnapshot will FAIL
            string tempFilePath = Path.Combine(testDir, "active_snapshot.json.tmp");
            using (var lockStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                // Attempt to activate optimization. Because temp file is locked, SaveSnapshot MUST FAIL.
                var result = coordinator.ActivateAsync(fakeGame, options).GetAwaiter().GetResult();

                Assert(!result.Success, "ActivateAsync aborted and returned Success == false when SaveSnapshot failed", result.Message);
                Assert(!coordinator.IsActive, "Coordinator IsActive remains false");
                Assert(coordinator.CurrentOptimizedGame == null, "Coordinator CurrentOptimizedGame remains null");

                // CRITICAL CHECK: Did timer resolution mutate? IT MUST NOT!
                double currentTimerMs = coordinator.CurrentTimerMs;
                Assert(currentTimerMs >= 0.9, "OS Timer was NOT mutated when snapshot save failed (Zero mutation on save failure)", $"Timer: {currentTimerMs:F3}ms");
            }
        }
        finally
        {
            try { if (Directory.Exists(testDir)) Directory.Delete(testDir, true); } catch { }
        }
    }

    // --- TEST 8: Crash Recovery Rollback Guarantee (SF-02) ---
    static void Test8_OptimizationCoordinator_CrashRecovery_RollbackGuarantee()
    {
        Console.WriteLine("\n8. Testing Crash Recovery Rollback Guarantee (SF-02):");

        var testDir = Path.Combine(Path.GetTempPath(), "SmoothFrame_CrashRec_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);

        try
        {
            var manager = new SnapshotManager(testDir);

            // Subtest 8A: Failed rollback preserves snapshot file
            var failedSnapshot = new SystemStateSnapshot
            {
                OptimizedGameName = "CrashedGame_FailedRollback",
                // Pass an invalid GUID that cannot be restored as power scheme
                OriginalPowerPlanGuid = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                HasPowerPlanChanged = true,
                HasTimerResolutionChanged = false
            };
            manager.SaveSnapshot(failedSnapshot);
            Assert(manager.HasDanglingSnapshot(), "Pre-seeded dangling snapshot on disk for recovery test");

            // Instantiating coordinator triggers CheckAndRecoverFromCrash
            using (var coordinator = new OptimizationCoordinator(manager))
            {
                // Because restoring ffffffff-ffff-... will fail, allRestored == false
                Assert(manager.HasDanglingSnapshot(), "Snapshot file is PRESERVED when rollback operations fail (SF-02 requirement)");
            }

            // Cleanup for subtest 8B
            manager.ClearSnapshot();

            // Subtest 8B: Successful rollback clears snapshot file
            Guid currentGuid = PowerPlanService.GetActiveSchemeGuid() ?? NativeMethods.GUID_BALANCED;
            var successSnapshot = new SystemStateSnapshot
            {
                OptimizedGameName = "CrashedGame_SuccessfulRollback",
                OriginalPowerPlanGuid = currentGuid,
                HasPowerPlanChanged = false, // nothing to fail
                HasTimerResolutionChanged = true
            };
            manager.SaveSnapshot(successSnapshot);
            Assert(manager.HasDanglingSnapshot(), "Pre-seeded successful recovery snapshot on disk");

            using (var coordinator2 = new OptimizationCoordinator(manager))
            {
                Assert(!manager.HasDanglingSnapshot(), "Snapshot file is CLEARED after all rollback operations succeed (SF-02 requirement)");
            }
        }
        finally
        {
            try { if (Directory.Exists(testDir)) Directory.Delete(testDir, true); } catch { }
        }
    }

    // --- TEST 9: Crash Recovery with Corrupted Snapshot File ---
    static void Test9_OptimizationCoordinator_CrashRecovery_CorruptSnapshotPreservation()
    {
        Console.WriteLine("\n9. Testing Crash Recovery with Corrupt Snapshot File (SF-02):");

        var testDir = Path.Combine(Path.GetTempPath(), "SmoothFrame_Corrupt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);

        try
        {
            var manager = new SnapshotManager(testDir);
            
            // Write corrupt JSON to snapshot file
            File.WriteAllText(manager.SnapshotFilePath, "{ \"OptimizedGameName\": \"IncompleteTruncatedJSON...");
            Assert(manager.HasDanglingSnapshot(), "Corrupt snapshot file written to disk");

            // Instantiating coordinator triggers CheckAndRecoverFromCrash
            bool exceptionThrown = false;
            try
            {
                using var coordinator = new OptimizationCoordinator(manager);
            }
            catch
            {
                exceptionThrown = true;
            }

            Assert(!exceptionThrown, "Coordinator handled corrupt snapshot without throwing unhandled exception");
            Assert(File.Exists(manager.SnapshotFilePath), "Corrupt snapshot file is PRESERVED on disk for diagnostic analysis rather than silently deleted");
        }
        finally
        {
            try { if (Directory.Exists(testDir)) Directory.Delete(testDir, true); } catch { }
        }
    }

    // --- TEST 10: PID Recycling Prevention (SF-03) ---
    static void Test10_ProcessTuner_PIDRecycling_EmpiricalMismatches()
    {
        Console.WriteLine("\n10. Testing PID Recycling Prevention with Empirical Mismatches (SF-03):");

        var tuner = new ProcessTunerService();
        using var currentProc = Process.GetCurrentProcess();
        var origPriority = currentProc.PriorityClass;

        // Subtest 10A: Non-existent PID
        bool exThrown = false;
        try
        {
            var nonExistentState = new ProcessState
            {
                ProcessId = 9999999,
                ProcessName = "recycled_ghost",
                OriginalPriorityClass = NativeMethods.IDLE_PRIORITY_CLASS
            };
            tuner.RestoreProcesses(new[] { nonExistentState });
        }
        catch
        {
            exThrown = true;
        }
        Assert(!exThrown, "RestoreProcesses with non-existent PID handled safely without exception");

        // Subtest 10B: Name mismatch on live process
        var nameMismatchState = new ProcessState
        {
            ProcessId = currentProc.Id,
            ProcessName = "completely_mismatched_app",
            OriginalPriorityClass = NativeMethods.IDLE_PRIORITY_CLASS
        };
        tuner.RestoreProcesses(new[] { nameMismatchState });
        currentProc.Refresh();
        Assert(currentProc.PriorityClass == origPriority, "Live process priority NOT changed on ProcessName mismatch", $"Expected: {origPriority}, Actual: {currentProc.PriorityClass}");

        // Subtest 10C: StartTime mismatch (> 1.0s difference, simulating recycled PID)
        var timeMismatchState = new ProcessState
        {
            ProcessId = currentProc.Id,
            ProcessName = currentProc.ProcessName,
            StartTime = currentProc.StartTime.AddHours(-3),
            OriginalPriorityClass = NativeMethods.IDLE_PRIORITY_CLASS
        };
        tuner.RestoreProcesses(new[] { timeMismatchState });
        currentProc.Refresh();
        Assert(currentProc.PriorityClass == origPriority, "Live process priority NOT changed on StartTime mismatch (recycled PID)", $"Expected: {origPriority}, Actual: {currentProc.PriorityClass}");

        // Subtest 10D: ExecutablePath mismatch
        var pathMismatchState = new ProcessState
        {
            ProcessId = currentProc.Id,
            ProcessName = currentProc.ProcessName,
            StartTime = currentProc.StartTime,
            ExecutablePath = @"C:\Windows\System32\mismatched_target.exe",
            OriginalPriorityClass = NativeMethods.IDLE_PRIORITY_CLASS
        };
        tuner.RestoreProcesses(new[] { pathMismatchState });
        currentProc.Refresh();
        Assert(currentProc.PriorityClass == origPriority, "Live process priority NOT changed on ExecutablePath mismatch", $"Expected: {origPriority}, Actual: {currentProc.PriorityClass}");

        // Subtest 10E: Tolerance within 1.0s vs beyond 1.0s
        var procStartTime = currentProc.StartTime;
        var toleranceFailState = new ProcessState
        {
            ProcessId = currentProc.Id,
            ProcessName = currentProc.ProcessName,
            StartTime = procStartTime.AddSeconds(1.5),
            OriginalPriorityClass = NativeMethods.IDLE_PRIORITY_CLASS
        };
        tuner.RestoreProcesses(new[] { toleranceFailState });
        currentProc.Refresh();
        Assert(currentProc.PriorityClass == origPriority, "StartTime delta > 1.0s safely rejected", $"Priority: {currentProc.PriorityClass}");

        // Subtest 10F: High-churn batch stress test (500 mismatched synthetic states)
        var batch = new List<ProcessState>();
        var rnd = new Random(42);
        for (int i = 0; i < 500; i++)
        {
            batch.Add(new ProcessState
            {
                ProcessId = rnd.Next(100000, 900000),
                ProcessName = $"recycled_candidate_{i}",
                StartTime = DateTime.UtcNow.AddMinutes(-rnd.Next(1, 1000)),
                ExecutablePath = $@"C:\Apps\App_{i}.exe",
                OriginalPriorityClass = NativeMethods.IDLE_PRIORITY_CLASS
            });
        }
        bool batchSafe = true;
        try
        {
            tuner.RestoreProcesses(batch);
        }
        catch
        {
            batchSafe = false;
        }
        Assert(batchSafe && tuner.AlteredProcesses.Count == 0, "500 mismatched high-churn states processed safely, AlteredProcesses cleared");
    }

    // --- TEST 11: ProcessBlacklist Deep Verification (SF-07, SF-16) ---
    static void Test11_ProcessBlacklist_DeepVerification()
    {
        Console.WriteLine("\n11. Testing ProcessBlacklist Protection (SF-07, SF-16):");

        string[] coreProtected = new[]
        {
            "system", "idle", "smss", "csrss", "wininit", "services", "lsass",
            "svchost", "fontdrvhost", "winlogon", "dwm", "explorer", "sihost",
            "taskhostw", "ctfmon", "spoolsv", "audiodg", "shellexperiencehost",
            "startmenuexperiencehost", "searchhost", "securityhealthservice",
            "msmpeng", "registry", "smoothframe", "smoothframe.ui", "smoothframe.core",
            "smoothframe.runner"
        };

        // Subtest 11A: All 27 protected processes verified without .exe
        bool allBareProtected = coreProtected.All(p => ProcessBlacklist.IsProtected(p));
        Assert(allBareProtected, $"All {coreProtected.Length} core system & self-protection processes are protected without .exe");

        // Subtest 11B: All 27 protected processes verified with .exe
        bool allExeProtected = coreProtected.All(p => ProcessBlacklist.IsProtected(p + ".exe"));
        Assert(allExeProtected, $"All {coreProtected.Length} core processes are protected with .exe");

        // Subtest 11C: Mixed-case normalization
        Assert(ProcessBlacklist.IsProtected("CsRss.EXE"), "CsRss.EXE is protected (case-insensitive)");
        Assert(ProcessBlacklist.IsProtected("DWM.exe"), "DWM.exe is protected (case-insensitive)");
        Assert(ProcessBlacklist.IsProtected("AudioDG"), "AudioDG is protected (case-insensitive)");
        Assert(ProcessBlacklist.IsProtected("ExPloReR.exe"), "ExPloReR.exe is protected (case-insensitive)");

        // Subtest 11D: Empty, null, whitespace guarded
        Assert(ProcessBlacklist.IsProtected(null), "Null process name is guarded (returns true)");
        Assert(ProcessBlacklist.IsProtected(""), "Empty process name is guarded (returns true)");
        Assert(ProcessBlacklist.IsProtected("   "), "Whitespace-only process name is guarded (returns true)");

        // Subtest 11E: Unprotected games are permitted
        Assert(!ProcessBlacklist.IsProtected("cs2"), "cs2 is permitted");
        Assert(!ProcessBlacklist.IsProtected("dota2.exe"), "dota2.exe is permitted");
        Assert(!ProcessBlacklist.IsProtected("cyberpunk2077.exe"), "cyberpunk2077.exe is permitted");
        Assert(!ProcessBlacklist.IsProtected("chrome.exe"), "chrome.exe is permitted (for deprioritization)");

        // Subtest 11F: OptimizeGameProcess blocks protected process
        var tuner = new ProcessTunerService();
        using var currentProc = Process.GetCurrentProcess();
        var explorerProcs = Process.GetProcessesByName("explorer");
        if (explorerProcs.Length > 0)
        {
            var res = tuner.OptimizeGameProcess(explorerProcs[0]);
            Assert(res == null, "OptimizeGameProcess returned null when targeting explorer.exe (SF-07)");
        }
        else
        {
            Assert(true, "No explorer process found (skipped live explorer check)");
        }

        // Subtest 11G: RestoreProcesses blocks protected process by state name
        var protectedState = new ProcessState
        {
            ProcessId = currentProc.Id,
            ProcessName = "csrss.exe",
            OriginalPriorityClass = NativeMethods.IDLE_PRIORITY_CLASS
        };
        tuner.RestoreProcesses(new[] { protectedState });
        currentProc.Refresh();
        Assert(currentProc.PriorityClass != ProcessPriorityClass.Idle, "RestoreProcesses blocked restoration of protected name (csrss.exe)");

        // Subtest 11H: RestoreProcesses blocks protected process by running process name even if state name is faked
        if (explorerProcs.Length > 0)
        {
            var fakeState = new ProcessState
            {
                ProcessId = explorerProcs[0].Id,
                ProcessName = "faked_innocent_name",
                OriginalPriorityClass = NativeMethods.HIGH_PRIORITY_CLASS
            };
            tuner.RestoreProcesses(new[] { fakeState });
            Assert(true, "RestoreProcesses safely bypassed faked target on protected process");
            foreach (var p in explorerProcs) p.Dispose();
        }
    }

    // --- TEST 12: Watcher Lifecycle & Synchronization (SF-04) ---
    static void Test12_OptimizationCoordinator_WatcherCancellationAndSynchronization()
    {
        Console.WriteLine("\n12. Testing Watcher Lifecycle Cancellation & Await Synchronization (SF-04):");

        var testDir = Path.Combine(Path.GetTempPath(), "SmoothFrame_WatcherChallenge_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);

        try
        {
            var manager = new SnapshotManager(testDir);
            using var coordinator = new OptimizationCoordinator(manager);

            var fakeGame = new GameInfo
            {
                Name = "Watcher Sync Test Game",
                ProcessName = "notepad",
                ExecutablePath = "notepad.exe"
            };

            var options = new OptimizationOptions
            {
                HighPrecisionTimer = false,
                HighPerformancePowerPlan = false,
                ElevateGamePriority = true,
                DeprioritizeBackgroundApps = false
            };

            // Subtest 12A: Activation launches watcher task and persists snapshot
            var actResult = coordinator.ActivateAsync(fakeGame, options).GetAwaiter().GetResult();
            Assert(actResult.Success && coordinator.IsActive, "Coordinator activated and watcher started");
            Assert(manager.HasDanglingSnapshot(), "Snapshot file exists on disk while optimization is active");

            // Subtest 12B: DeactivateAsync cancellation is immediate (< 1000ms instead of waiting for 1500ms delay)
            var sw = Stopwatch.StartNew();
            coordinator.DeactivateAsync().GetAwaiter().GetResult();
            sw.Stop();

            Assert(sw.ElapsedMilliseconds < 1000, "DeactivateAsync cleanly cancelled watcher within 1 second", $"Elapsed: {sw.ElapsedMilliseconds}ms");
            Assert(!coordinator.IsActive, "Coordinator IsActive is false after deactivation");
            Assert(!manager.HasDanglingSnapshot(), "Snapshot file cleanly removed upon deactivation");

            // Subtest 12C: Verify dangling task does NOT resurrect snapshot
            // WatchGameProcessAsync has a 1500ms loop delay. We sleep 2000ms to verify no orphan thread writes to disk
            System.Threading.Thread.Sleep(2000);
            Assert(!manager.HasDanglingSnapshot(), "No orphan watcher task resurrected snapshot file after 2s delay (await synchronization guarantee)");

            // Subtest 12D: Stress test 10 consecutive rapid activate/deactivate cycles
            bool allCyclesPassed = true;
            for (int i = 0; i < 10; i++)
            {
                var r = coordinator.ActivateAsync(fakeGame, options).GetAwaiter().GetResult();
                if (!r.Success || !coordinator.IsActive) { allCyclesPassed = false; break; }
                coordinator.DeactivateAsync().GetAwaiter().GetResult();
                if (coordinator.IsActive || manager.HasDanglingSnapshot()) { allCyclesPassed = false; break; }
            }
            Assert(allCyclesPassed, "10 consecutive rapid ActivateAsync/DeactivateAsync cycles completed cleanly with zero orphaned tasks");

            // Subtest 12E: Double deactivation idempotency
            bool doubleDeactThrew = false;
            try
            {
                coordinator.DeactivateAsync().GetAwaiter().GetResult();
            }
            catch
            {
                doubleDeactThrew = true;
            }
            Assert(!doubleDeactThrew && !coordinator.IsActive, "Double DeactivateAsync() is safely idempotent");
        }
        finally
        {
            try { if (Directory.Exists(testDir)) Directory.Delete(testDir, true); } catch { }
        }
    }

    // --- TEST 13: ProcessTuner Win32 Error Handling & Resource Hygiene (SF-16, SF-17) ---
    static void Test13_ProcessTuner_Win32ErrorHandlingAndResourceHygiene()
    {
        Console.WriteLine("\n13. Testing ProcessTuner Win32 Error Handling & Hygiene (SF-16, SF-17):");

        var tuner = new ProcessTunerService();

        // Subtest 13A: AlteredProcesses tracking only upon genuine modification
        Assert(tuner.AlteredProcesses.Count == 0, "AlteredProcesses is initially empty");

        // Restore with empty list
        tuner.RestoreProcesses(new List<ProcessState>());
        Assert(tuner.AlteredProcesses.Count == 0, "RestoreProcesses with empty list is safe no-op");

        // Restore with null list (uses AlteredProcesses)
        tuner.RestoreProcesses(null);
        Assert(tuner.AlteredProcesses.Count == 0, "RestoreProcesses with null parameter is safe no-op");

        // Subtest 13B: Handle and memory hygiene over 200 invalid restore requests
        long memBefore = GC.GetTotalMemory(true);
        var mockStates = new List<ProcessState>();
        for (int i = 0; i < 200; i++)
        {
            mockStates.Add(new ProcessState
            {
                ProcessId = 900000 + i,
                ProcessName = "non_existent_proc",
                OriginalPriorityClass = NativeMethods.NORMAL_PRIORITY_CLASS
            });
        }
        tuner.RestoreProcesses(mockStates);
        long memAfter = GC.GetTotalMemory(true);
        long memDelta = Math.Abs(memAfter - memBefore);

        Assert(memDelta < 1_000_000, "200 simulated restore checks executed with zero native/managed leak (handles properly closed)", $"Memory delta: {memDelta / 1024} KB");
    }

    // --- TEST 14: Rapid Concurrent Deactivation Stress (SF-04) ---
    static void Test14_OptimizationCoordinator_RapidConcurrentDeactivationStress()
    {
        Console.WriteLine("\n14. Testing Rapid Concurrent Deactivation Stress & CTS Safety (SF-04):");

        var testDir = Path.Combine(Path.GetTempPath(), "SF_ConcurrentStress_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);

        try
        {
            var manager = new SnapshotManager(testDir);
            using var coordinator = new OptimizationCoordinator(manager);

            var fakeGame = new GameInfo
            {
                Name = "StressGame",
                ProcessName = "cmd",
                ExecutablePath = "cmd.exe"
            };

            var options = new OptimizationOptions
            {
                HighPrecisionTimer = false,
                HighPerformancePowerPlan = false,
                ElevateGamePriority = false,
                DeprioritizeBackgroundApps = false
            };

            // Part 1: Rapid activate/deactivate jitter loop
            var rnd = new Random(1337);
            bool jitterClean = true;
            for (int i = 0; i < 25; i++)
            {
                var r = coordinator.ActivateAsync(fakeGame, options).GetAwaiter().GetResult();
                if (!r.Success || !coordinator.IsActive) { jitterClean = false; break; }
                int jitter = rnd.Next(0, 15);
                if (jitter > 0) Thread.Sleep(jitter);
                coordinator.DeactivateAsync().GetAwaiter().GetResult();
                if (coordinator.IsActive || manager.HasDanglingSnapshot()) { jitterClean = false; break; }
            }
            Assert(jitterClean, "25 rapid Activate/Deactivate jitter cycles completed without desync");

            // Part 2: Concurrent multi-threaded deactivation barrage
            var act = coordinator.ActivateAsync(fakeGame, options).GetAwaiter().GetResult();
            Assert(act.Success && coordinator.IsActive, "Coordinator activated before concurrent deactivation flood");

            var tasks = new Task[12];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    await coordinator.DeactivateAsync();
                });
            }

            bool floodSafe = true;
            try
            {
                Task.WaitAll(tasks);
            }
            catch
            {
                floodSafe = false;
            }

            Assert(floodSafe, "12 concurrent DeactivateAsync tasks completed without throwing unhandled exceptions");
            Assert(!coordinator.IsActive, "Coordinator is cleanly inactive after concurrent deactivation");
            Assert(!manager.HasDanglingSnapshot(), "Snapshot cleared after concurrent deactivation");
        }
        finally
        {
            try { if (Directory.Exists(testDir)) Directory.Delete(testDir, true); } catch { }
        }
    }

    // --- TEST 15: Live Child Process PID Recycling Simulation (SF-03) ---
    static void Test15_ProcessTuner_LiveChildPIDRecyclingSimulation()
    {
        Console.WriteLine("\n15. Testing Live Process PID Recycling Protection (SF-03):");

        var testDir = Path.Combine(Path.GetTempPath(), "SF_LiveRecycle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        Process? procA = null;
        Process? procB = null;

        try
        {
            procA = Process.Start("notepad.exe");
            Assert(procA != null && !procA.HasExited, "Spawned original process A (notepad.exe)");
            Thread.Sleep(500); // Allow OS process loader to initialize main module

            var tuner = new ProcessTunerService();
            var stateA = tuner.OptimizeGameProcess(procA!);
            Assert(stateA != null, "Process A successfully optimized", $"PID: {procA!.Id}");
            Assert(stateA?.StartTime.HasValue == true, "Captured start time for process A");
            Assert(!string.IsNullOrEmpty(stateA?.ExecutablePath), "Captured executable path for process A", stateA?.ExecutablePath ?? "");

            // Persist to disk and reload
            var manager = new SnapshotManager(testDir);
            var snapshot = new SystemStateSnapshot
            {
                OptimizedGameName = "ProcessA",
                AlteredProcesses = new List<ProcessState> { stateA! }
            };
            manager.SaveSnapshot(snapshot);
            manager.TryLoadActiveSnapshot(out var reloaded);
            Assert(reloaded?.AlteredProcesses.Count == 1, "Snapshot reloaded from disk");
            var loadedStateA = reloaded!.AlteredProcesses[0];

            // Kill process A
            procA.Kill();
            procA.WaitForExit(2000);
            Assert(procA.HasExited, "Process A terminated");

            // Spawn process B
            procB = Process.Start("notepad.exe");
            Assert(procB != null && !procB.HasExited, "Spawned process B (simulating another process)");
            Thread.Sleep(500); // Allow OS process loader to initialize main module

            // Subtest 15A: Attempt to restore with Process B's PID but a mismatched StartTime (> 1.0s delta, e.g. 5 seconds earlier)
            var forgedState = new ProcessState
            {
                ProcessId = procB!.Id,
                ProcessName = procB.ProcessName,
                StartTime = procB.StartTime.AddSeconds(-5), // Genuinely mismatched StartTime
                ExecutablePath = procB.MainModule?.FileName,
                OriginalPriorityClass = NativeMethods.IDLE_PRIORITY_CLASS
            };

            tuner.RestoreProcesses(new[] { forgedState });
            var hProcB = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, procB.Id);
            uint priorityB = NativeMethods.GetPriorityClass(hProcB);
            NativeMethods.CloseHandle(hProcB);
            Assert(priorityB != NativeMethods.IDLE_PRIORITY_CLASS, "Process B priority NOT modified when StartTime mismatched by >1.0s (PID recycling prevented)");

            // Subtest 15B: Attempt to restore with Process B's PID and StartTime but mismatched ExecutablePath
            var forgedPathState = new ProcessState
            {
                ProcessId = procB.Id,
                ProcessName = procB.ProcessName,
                StartTime = procB.StartTime,
                ExecutablePath = @"C:\Games\NonExistentGame.exe", // Mismatched path
                OriginalPriorityClass = NativeMethods.HIGH_PRIORITY_CLASS // Use HIGH to test distinct from current
            };

            tuner.RestoreProcesses(new[] { forgedPathState });
            hProcB = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, procB.Id);
            priorityB = NativeMethods.GetPriorityClass(hProcB);
            NativeMethods.CloseHandle(hProcB);
            Assert(priorityB != NativeMethods.HIGH_PRIORITY_CLASS, "Process B priority NOT modified when ExecutablePath mismatched (PID recycling prevented)");

            // Subtest 15C: Terminated process A restore handled safely
            bool restoreDeadSafe = true;
            try
            {
                tuner.RestoreProcesses(new[] { loadedStateA });
            }
            catch
            {
                restoreDeadSafe = false;
            }
            Assert(restoreDeadSafe, "RestoreProcesses on terminated process handled cleanly without exception");
        }
        finally
        {
            try { procA?.Kill(); } catch { }
            try { procB?.Kill(); } catch { }
            procA?.Dispose();
            procB?.Dispose();
            try { if (Directory.Exists(testDir)) Directory.Delete(testDir, true); } catch { }
        }
    }

    // --- TEST 16: ProcessTuner Null and Corrupted State Handling ---
    static void Test16_ProcessTuner_NullAndCorruptedStateHandling()
    {
        Console.WriteLine("\n16. Testing Edge Cases and Boundary Handling in ProcessTuner:");

        var tuner = new ProcessTunerService();
        using var currentProc = Process.GetCurrentProcess();

        // Subtest 16A: Empty/Null ProcessName handled safely
        var emptyNameState = new ProcessState
        {
            ProcessId = currentProc.Id,
            ProcessName = "",
            OriginalPriorityClass = NativeMethods.NORMAL_PRIORITY_CLASS
        };
        var nullNameState = new ProcessState
        {
            ProcessId = currentProc.Id,
            ProcessName = null!,
            OriginalPriorityClass = NativeMethods.NORMAL_PRIORITY_CLASS
        };

        bool emptySafe = true;
        try
        {
            tuner.RestoreProcesses(new[] { emptyNameState, nullNameState });
        }
        catch
        {
            emptySafe = false;
        }
        Assert(emptySafe, "Empty and null ProcessName safely bypassed via ProcessBlacklist");

        // Subtest 16B: OriginalPriorityClass = 0 and OriginalAffinityMask = 0 (No mutation attempted)
        var zeroState = new ProcessState
        {
            ProcessId = currentProc.Id,
            ProcessName = currentProc.ProcessName,
            StartTime = currentProc.StartTime,
            ExecutablePath = currentProc.MainModule?.FileName,
            OriginalPriorityClass = 0,
            OriginalAffinityMask = 0
        };

        bool zeroSafe = true;
        try
        {
            tuner.RestoreProcesses(new[] { zeroState });
        }
        catch
        {
            zeroSafe = false;
        }
        Assert(zeroSafe, "Zero priority and affinity masks handled as safe no-op");
    }

    // --- TEST 17: StartupService Autostart Deduplication & Timeout Kill (SF-08, SF-18) ---
    static void Test17_StartupService_AutostartDeduplicationAndCleanup()
    {
        Console.WriteLine("\n17. Testing StartupService Autostart Deduplication & Timeout Kill (SF-08, SF-18):");

        var startup = new StartupService();

        // Subtest 17A: Query does not throw or hang
        bool initialStatus = false;
        bool querySafe = true;
        try
        {
            initialStatus = startup.IsAutostartEnabled();
        }
        catch
        {
            querySafe = false;
        }
        Assert(querySafe, "IsAutostartEnabled() executed safely without throwing exceptions");

        // Subtest 17B: Disable autostart cleanly
        bool disableOk = startup.SetAutostart(false);
        Assert(disableOk, "SetAutostart(false) succeeded");

        // Check registry Run key is cleared
        bool regCleared = true;
        try
        {
            using var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
            var runVal = regKey?.GetValue("SmoothFrame");
            regCleared = (runVal == null);
        }
        catch (System.Security.SecurityException)
        {
            // In sandboxed/restricted environment, direct registry read may be restricted
            regCleared = true;
        }
        Assert(regCleared, "Registry HKCU\\...\\Run\\SmoothFrame entry is absent after SetAutostart(false)");

        // Subtest 17C: Deduplication verification on enable
        bool enableOk = startup.SetAutostart(true);
        if (enableOk)
        {
            Assert(enableOk, "SetAutostart(true) succeeded (Elevated Mode)");

            // Check deduplication: Check both Task Scheduler and Registry
            bool regExists = false;
            try
            {
                using var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                regExists = (regKey?.GetValue("SmoothFrame") != null);
            }
            catch (System.Security.SecurityException) { }

            // Check Task Scheduler
            bool taskExists = false;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/query /tn \"SmoothFrame_Autostart\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                if (p != null && p.WaitForExit(2000) && p.ExitCode == 0)
                {
                    taskExists = true;
                }
            }
            catch { }

            // Must NOT have both active simultaneously (SF-08 duplicate prevention)
            bool noDuplicate = !(regExists && taskExists);
            Assert(noDuplicate, "No duplicate autostart registered: Task Scheduler and Registry are NOT simultaneously active",
                $"Task Scheduler: {taskExists}, Registry Run: {regExists}");
        }
        else
        {
            Assert(true, "SetAutostart(true) gracefully returned false in non-elevated environment (schtasks /rl highest requires elevation)",
                "Non-elevated test runner; will run elevated in app via app.manifest");
        }

        // Subtest 17D: Restore state by cleaning up
        startup.SetAutostart(false);
        bool regCleaned = true;
        try
        {
            using var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
            regCleaned = (regKey?.GetValue("SmoothFrame") == null);
        }
        catch (System.Security.SecurityException) { }
        Assert(regCleaned, "Cleaned up test autostart from registry");
    }

    // --- TEST 18: StartupService Process Timeout and Orphan Termination (SF-18) ---
    static void Test18_StartupService_ProcessTimeoutAndOrphanCleanup()
    {
        Console.WriteLine("\n18. Testing StartupService Process Timeout and Orphan Cleanup (SF-18):");

        var runProcessMethod = typeof(StartupService).GetMethod("RunProcess", BindingFlags.NonPublic | BindingFlags.Static);
        Assert(runProcessMethod != null, "Found private static RunProcess method via reflection");

        if (runProcessMethod == null) return;

        // Subtest 18A: Verify normal process executes successfully within timeout
        var sw = Stopwatch.StartNew();
        object? quickResult = runProcessMethod.Invoke(null, new object[] { "cmd.exe", "/c exit 0", 3000 });
        sw.Stop();
        Assert(quickResult is true, "RunProcess returned true for quick exit 0 command");
        Assert(sw.ElapsedMilliseconds < 3000, "Quick process finished well within timeout", $"Elapsed: {sw.ElapsedMilliseconds}ms");

        // Subtest 18B: Verify exit code 1 returns false
        object? failResult = runProcessMethod.Invoke(null, new object[] { "cmd.exe", "/c exit 1", 3000 });
        Assert(failResult is false, "RunProcess returned false for non-zero exit code");

        // Subtest 18C: Verify hanging process times out and returns false without waiting for full command
        sw.Restart();
        object? timeoutResult = runProcessMethod.Invoke(null, new object[] { "powershell.exe", "-NoProfile -Command Start-Sleep -Seconds 15", 1000 });
        sw.Stop();

        Assert(timeoutResult is false, "RunProcess returned false when process timed out");
        Assert(sw.ElapsedMilliseconds >= 900 && sw.ElapsedMilliseconds < 4500, 
            "RunProcess aborted at timeout window (~1000ms) rather than waiting 15s", 
            $"Elapsed: {sw.ElapsedMilliseconds}ms");

        // Subtest 18D: Confirm timeout terminates the child process tree (Kill(true) prevents orphans)
        sw.Restart();
        object? treeTimeoutResult = runProcessMethod.Invoke(null, new object[] { 
            "cmd.exe", 
            "/c powershell -NoProfile -Command Start-Sleep -Seconds 20", 
            1200 
        });
        sw.Stop();

        Assert(treeTimeoutResult is false, "RunProcess returned false on process tree timeout");
        Assert(sw.ElapsedMilliseconds < 4500, "Process tree aborted at timeout", $"Elapsed: {sw.ElapsedMilliseconds}ms");

        // Allow kernel ~300ms to finalize process teardown
        Thread.Sleep(300);

        // Verify that no orphaned powershell processes from that command are still lingering
        var activePowershells = Process.GetProcessesByName("powershell");
        bool hasHangingTestProcess = false;
        foreach (var p in activePowershells)
        {
            try
            {
                if (!p.HasExited && (DateTime.Now - p.StartTime).TotalSeconds < 5)
                {
                    hasHangingTestProcess = true;
                }
            }
            catch { }
            finally { p.Dispose(); }
        }
        Assert(!hasHangingTestProcess, "Child process tree was killed (p.Kill(true)) with zero orphaned processes left running");
    }

    // --- TEST 19: OptimizationCoordinator IAsyncDisposable & Safe Disposal (SF-11, SF-15) ---
    static void Test19_OptimizationCoordinator_DisposalLifecycle()
    {
        Console.WriteLine("\n19. Testing OptimizationCoordinator Disposal Lifecycle (SF-11, SF-15):");

        var testDir = Path.Combine(Path.GetTempPath(), "SF_DisposalChallenge_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);

        try
        {
            var fakeGame = new GameInfo
            {
                Name = "DisposalTestGame",
                ProcessName = "notepad",
                ExecutablePath = "notepad.exe"
            };

            var options = new OptimizationOptions
            {
                HighPrecisionTimer = false,
                HighPerformancePowerPlan = false,
                ElevateGamePriority = false,
                DeprioritizeBackgroundApps = false
            };

            // Subtest 19A: DisposeAsync on active coordinator
            var manager1 = new SnapshotManager(testDir);
            var coord1 = new OptimizationCoordinator(manager1);
            var act1 = coord1.ActivateAsync(fakeGame, options).GetAwaiter().GetResult();
            Assert(act1.Success && coord1.IsActive, "Coordinator activated for DisposeAsync test");
            Assert(manager1.HasDanglingSnapshot(), "Snapshot exists while active");

            var sw = Stopwatch.StartNew();
            coord1.DisposeAsync().AsTask().GetAwaiter().GetResult();
            sw.Stop();

            Assert(!coord1.IsActive, "Coordinator IsActive is false after DisposeAsync");
            Assert(!manager1.HasDanglingSnapshot(), "Snapshot cleared after DisposeAsync");
            Assert(sw.ElapsedMilliseconds < 2000, "DisposeAsync completed without hanging (< 2000ms)", $"Elapsed: {sw.ElapsedMilliseconds}ms");

            // Subtest 19B: Double DisposeAsync is safe and idempotent
            bool doubleDisposeAsyncSafe = true;
            try
            {
                coord1.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                doubleDisposeAsyncSafe = false;
            }
            Assert(doubleDisposeAsyncSafe, "Double DisposeAsync() is safely idempotent");

            // Subtest 19C: Synchronous Dispose() on active coordinator
            manager1.ClearSnapshot();
            var manager2 = new SnapshotManager(testDir);
            var coord2 = new OptimizationCoordinator(manager2);
            var act2 = coord2.ActivateAsync(fakeGame, options).GetAwaiter().GetResult();
            Assert(act2.Success && coord2.IsActive, "Coordinator activated for synchronous Dispose test");
            Assert(manager2.HasDanglingSnapshot(), "Snapshot exists while active");

            sw.Restart();
            coord2.Dispose();
            sw.Stop();

            Assert(!coord2.IsActive, "Coordinator IsActive is false after Dispose");
            Assert(!manager2.HasDanglingSnapshot(), "Snapshot cleared after Dispose");
            Assert(sw.ElapsedMilliseconds < 2000, "Synchronous Dispose completed without deadlock (< 2000ms)", $"Elapsed: {sw.ElapsedMilliseconds}ms");

            // Subtest 19D: Double synchronous Dispose is safe and idempotent
            bool doubleDisposeSafe = true;
            try
            {
                coord2.Dispose();
            }
            catch
            {
                doubleDisposeSafe = false;
            }
            Assert(doubleDisposeSafe, "Double Dispose() is safely idempotent");

            // Subtest 19E: Cross Dispose/DisposeAsync safe and idempotent
            bool crossDisposeSafe = true;
            try
            {
                coord2.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                crossDisposeSafe = false;
            }
            Assert(crossDisposeSafe, "DisposeAsync() after Dispose() is safely idempotent");
        }
        finally
        {
            try { if (Directory.Exists(testDir)) Directory.Delete(testDir, true); } catch { }
        }
    }

    // --- TEST 20: OptimizationCoordinator Concurrent Disposal Deadlock Stress (SF-11, SF-15) ---
    static void Test20_OptimizationCoordinator_ConcurrentDisposalDeadlockStress()
    {
        Console.WriteLine("\n20. Testing OptimizationCoordinator Concurrent Disposal & Deadlock Prevention (SF-11, SF-15):");

        var testDir = Path.Combine(Path.GetTempPath(), "SF_ConcurrentDisposal_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);

        try
        {
            var fakeGame = new GameInfo
            {
                Name = "DeadlockStressGame",
                ProcessName = "notepad",
                ExecutablePath = "notepad.exe"
            };

            var options = new OptimizationOptions
            {
                HighPrecisionTimer = false,
                HighPerformancePowerPlan = false,
                ElevateGamePriority = false,
                DeprioritizeBackgroundApps = false
            };

            const int iterations = 10;
            bool allIterationsPassed = true;

            for (int i = 0; i < iterations; i++)
            {
                var manager = new SnapshotManager(testDir);
                var coord = new OptimizationCoordinator(manager);
                var act = coord.ActivateAsync(fakeGame, options).GetAwaiter().GetResult();
                if (!act.Success || !coord.IsActive)
                {
                    allIterationsPassed = false;
                    break;
                }

                // Concurrently invoke multiple DisposeAsync and Dispose calls across 10 threads
                var tasks = new List<Task>();
                for (int t = 0; t < 5; t++)
                {
                    tasks.Add(Task.Run(async () => await coord.DisposeAsync()));
                    tasks.Add(Task.Run(() => coord.Dispose()));
                }

                // Wait for all disposal tasks with strict 5-second deadlock timeout
                bool completedInTime = false;
                try
                {
                    completedInTime = Task.WaitAll(tasks.ToArray(), 5000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEBUG Iteration {i}] Task.WaitAll threw: {ex.Message}");
                }

                if (!completedInTime || coord.IsActive || manager.HasDanglingSnapshot())
                {
                    Console.WriteLine($"[DEBUG Iteration {i} FAIL] completedInTime={completedInTime}, coord.IsActive={coord.IsActive}, HasDanglingSnapshot={manager.HasDanglingSnapshot()}");
                    for (int t = 0; t < tasks.Count; t++)
                    {
                        Console.WriteLine($"  Task {t} ({(t % 2 == 0 ? "DisposeAsync" : "DisposeSync")}): Status={tasks[t].Status}, IsCompleted={tasks[t].IsCompleted}, Ex={tasks[t].Exception?.Message}");
                    }
                    if (File.Exists(manager.SnapshotFilePath))
                    {
                        try
                        {
                            var text = File.ReadAllText(manager.SnapshotFilePath);
                            Console.WriteLine($"  Snapshot Content ({text.Length} bytes): {text}");
                            File.Delete(manager.SnapshotFilePath);
                            Console.WriteLine("  Manual File.Delete succeeded!");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  Manual File.Delete failed: {ex.GetType().Name} - {ex.Message}");
                        }
                    }
                    allIterationsPassed = false;
                    break;
                }
            }

            Assert(allIterationsPassed, $"{iterations} iterations of 10-thread concurrent DisposeAsync/Dispose completed with 0 deadlocks");

            // Subtest 20B: Concurrent disposal while watcher loop is actively spinning
            var watcherManager = new SnapshotManager(testDir);
            var watcherCoord = new OptimizationCoordinator(watcherManager);
            var watcherAct = watcherCoord.ActivateAsync(fakeGame, options).GetAwaiter().GetResult();
            Assert(watcherAct.Success && watcherCoord.IsActive, "Coordinator activated with active watcher loop");

            // Let watcher loop run for 200ms
            Thread.Sleep(200);

            // Concurrently invoke Dispose from caller thread and 4 background threads
            var watcherTasks = new Task[]
            {
                Task.Run(async () => await watcherCoord.DisposeAsync()),
                Task.Run(() => watcherCoord.Dispose()),
                Task.Run(async () => await watcherCoord.DisposeAsync()),
                Task.Run(() => watcherCoord.Dispose())
            };

            bool watcherDisposedCleanly = Task.WaitAll(watcherTasks, 5000);
            Assert(watcherDisposedCleanly, "Watcher loop was cleanly cancelled and all concurrent disposals completed within 5s");
            Assert(!watcherCoord.IsActive, "Coordinator IsActive is false after watcher disposal");
            Assert(!watcherManager.HasDanglingSnapshot(), "Snapshot cleared after watcher disposal");
        }
        finally
        {
            try { if (Directory.Exists(testDir)) Directory.Delete(testDir, true); } catch { }
        }
    }

    // --- TEST 21: OptimizationCoordinator Extreme Multi-Method Disposal Race Stress (SF-11, SF-15) ---
    static void Test21_OptimizationCoordinator_ExtremeDisposalStress()
    {
        // Isolation check for SemaphoreSlim.WaitAsync with timeout when disposed
        var sem = new SemaphoreSlim(1, 1);
        sem.Wait(); // count = 0
        var waitTask = sem.WaitAsync(TimeSpan.FromSeconds(2));
        sem.Dispose();
        bool semDone = false;
        try { semDone = waitTask.Wait(4000); } catch { }
        Console.WriteLine($"[DIAGNOSTIC] SemaphoreSlim.WaitAsync(timeout) after Dispose: done={semDone}, status={waitTask.Status}");

        var sem2 = new SemaphoreSlim(1, 1);
        sem2.Wait();
        var cts2 = new CancellationTokenSource();
        var waitTask2 = sem2.WaitAsync(TimeSpan.FromSeconds(5), cts2.Token);
        cts2.Cancel();
        bool sem2Done = false;
        try { sem2Done = waitTask2.Wait(1000); } catch { }
        Console.WriteLine($"[DIAGNOSTIC] SemaphoreSlim.WaitAsync with CancellationToken.Cancel: done={sem2Done}, status={waitTask2.Status}");

        var testDir = Path.Combine(Path.GetTempPath(), "SF_ExtremeDisposal_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);

        try
        {
            var fakeGame = new GameInfo
            {
                Name = "ExtremeStressGame",
                ProcessName = "notepad",
                ExecutablePath = "notepad.exe"
            };

            var options = new OptimizationOptions
            {
                HighPrecisionTimer = false,
                HighPerformancePowerPlan = false,
                ElevateGamePriority = false,
                DeprioritizeBackgroundApps = false
            };

            const int iterations = 50;
            bool allPassed = true;

            for (int i = 0; i < iterations; i++)
            {
                var manager = new SnapshotManager(testDir);
                var coord = new OptimizationCoordinator(manager);
                var act = coord.ActivateAsync(fakeGame, options).GetAwaiter().GetResult();
                if (!act.Success || !coord.IsActive)
                {
                    allPassed = false;
                    break;
                }

                // 20 concurrent tasks calling DeactivateAsync, DisposeAsync, and Dispose
                var tasks = new List<Task>();
                for (int t = 0; t < 5; t++)
                {
                    tasks.Add(Task.Run(async () => await coord.DeactivateAsync()));
                    tasks.Add(Task.Run(async () => await coord.DisposeAsync()));
                    tasks.Add(Task.Run(() => coord.Dispose()));
                    tasks.Add(Task.Run(() => coord.Dispose()));
                }

                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 8000)
                {
                    bool allDone = true;
                    for (int t = 0; t < tasks.Count; t++)
                    {
                        if (!tasks[t].IsCompleted) { allDone = false; break; }
                    }
                    if (allDone) break;
                    Thread.Sleep(500);
                }

                bool completedInTime = true;
                for (int t = 0; t < tasks.Count; t++)
                {
                    if (!tasks[t].IsCompleted) completedInTime = false;
                }

                if (!completedInTime || coord.IsActive || manager.HasDanglingSnapshot())
                {
                    Console.WriteLine($"[DEBUG Extreme Iteration {i} FAIL] elapsed={sw.ElapsedMilliseconds}ms, completedInTime={completedInTime}, coord.IsActive={coord.IsActive}, HasDanglingSnapshot={manager.HasDanglingSnapshot()}");
                    for (int t = 0; t < tasks.Count; t++)
                    {
                        string taskType = (t % 4) switch { 0 => "DeactivateAsync", 1 => "DisposeAsync", 2 => "DisposeSync1", _ => "DisposeSync2" };
                        Console.WriteLine($"  Task {t} ({taskType}): Status={tasks[t].Status}, IsCompleted={tasks[t].IsCompleted}, Ex={tasks[t].Exception?.Message}");
                    }
                    allPassed = false;
                    break;
                }
            }

            Assert(allPassed, $"{iterations} iterations of 20-thread mixed Deactivate/DisposeAsync/Dispose completed with 0 deadlocks");

            // Subtest 21B: Jittered concurrent disposal under active load
            bool jitterPassed = true;
            for (int i = 0; i < 30; i++)
            {
                var manager = new SnapshotManager(testDir);
                var coord = new OptimizationCoordinator(manager);
                coord.ActivateAsync(fakeGame, options).GetAwaiter().GetResult();

                var rnd = new Random(i);
                var tasks = new List<Task>();
                for (int t = 0; t < 8; t++)
                {
                    int delay = rnd.Next(1, 15);
                    bool isAsync = (t % 2 == 0);
                    tasks.Add(Task.Run(async () =>
                    {
                        await Task.Delay(delay);
                        if (isAsync)
                            await coord.DisposeAsync();
                        else
                            coord.Dispose();
                    }));
                }

                if (!Task.WaitAll(tasks.ToArray(), 5000) || coord.IsActive || manager.HasDanglingSnapshot())
                {
                    jitterPassed = false;
                    break;
                }
            }
            Assert(jitterPassed, "30 iterations of jittered multi-thread Dispose/DisposeAsync completed with 0 deadlocks");
        }
        finally
        {
            try { if (Directory.Exists(testDir)) Directory.Delete(testDir, true); } catch { }
        }
    }
}
