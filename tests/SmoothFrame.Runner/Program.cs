using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmoothFrame.Core.Models;
using SmoothFrame.Core.Native;
using SmoothFrame.Core.Safety;
using SmoothFrame.Core.Services;

namespace SmoothFrame.Runner;

internal class Program
{
    private static int _passedCount = 0;
    private static int _failedCount = 0;

    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=================================================");
        Console.WriteLine("      SmoothFrame Safety & Core Test Runner      ");
        Console.WriteLine("=================================================\n");
        Console.ResetColor();

        if (args != null && args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("SmoothFrame Test Runner");
            Console.WriteLine("Usage: dotnet run --project tests/SmoothFrame.Runner [-- [options]]\n");
            Console.WriteLine("Options:");
            Console.WriteLine("  --simulate-failure, -s, /simulate-failure, --fail    Simulate artificial test failure and exit with code 1");
            Console.WriteLine("  --help, -h                                           Show this help message");
            Environment.ExitCode = 0;
            return 0;
        }

        bool simulateFailure = args != null && args.Any(a =>
            string.Equals(a, "--simulate-failure", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "-s", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "/simulate-failure", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--fail", StringComparison.OrdinalIgnoreCase));

        if (simulateFailure)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[SIMULATION] Simulating artificial test failure (--simulate-failure)...");
            Console.ResetColor();

            Assert(false, "Simulated Failure Assertion (SF-14)", "Intentional failure triggered via --simulate-failure switch");
        }
        else
        {
            string runnerSessionDir = Path.Combine(Path.GetTempPath(), "SmoothFrame_Runner_Session_" + Guid.NewGuid().ToString("N"));
            try
            {
                TestProcessBlacklist();
                TestProcessTunerAndRecyclingSafety();
                TestTimerResolution();
                TestPowerPlanService();
                TestSnapshotManager(Path.Combine(runnerSessionDir, "Snapshot"));
                TestGameDiscovery();
                TestOptimizationCoordinator(Path.Combine(runnerSessionDir, "Coordinator"));
                TestSnapshotIsolationGuard();
            }
            catch (Exception ex)
            {
                _failedCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[UNHANDLED TEST EXCEPTION] {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
            }
            finally
            {
                try { if (Directory.Exists(runnerSessionDir)) Directory.Delete(runnerSessionDir, true); } catch { }
            }
        }

        Console.WriteLine("\n-------------------------------------------------");
        if (_failedCount == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[ALL TESTS PASSED] {_passedCount} passed, 0 failed.");
            Console.ResetColor();
            Environment.ExitCode = 0;
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[TEST FAILURES OCCURRED] {_passedCount} passed, {_failedCount} failed.");
            Console.ResetColor();
            Environment.ExitCode = 1;
            return 1;
        }
    }

    static void Assert(bool condition, string testName, string? extraInfo = null)
    {
        if (condition)
        {
            _passedCount++;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("  [PASS] ");
            Console.ResetColor();
            Console.WriteLine(testName);
            if (!string.IsNullOrEmpty(extraInfo))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"         └─ {extraInfo}");
                Console.ResetColor();
            }
        }
        else
        {
            _failedCount++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("  [FAIL] ");
            Console.ResetColor();
            Console.WriteLine(testName);
            if (!string.IsNullOrEmpty(extraInfo))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"         └─ {extraInfo}");
                Console.ResetColor();
            }
        }
    }

    static void TestProcessBlacklist()
    {
        Console.WriteLine("1. Testing Process Blacklist & Critical Protection (SF-07):");

        Assert(ProcessBlacklist.IsProtected("csrss.exe"), "csrss.exe is protected");
        Assert(ProcessBlacklist.IsProtected("dwm.exe"), "dwm.exe is protected");
        Assert(ProcessBlacklist.IsProtected("audiodg"), "audiodg (Windows Audio) is protected");
        Assert(ProcessBlacklist.IsProtected("explorer.exe"), "explorer.exe is protected");
        Assert(ProcessBlacklist.IsProtected("SmoothFrame.UI"), "SmoothFrame itself is protected");
        Assert(ProcessBlacklist.IsProtected("SmoothFrame.Runner"), "SmoothFrame.Runner itself is protected");
        Assert(ProcessBlacklist.IsProtected(""), "Empty process name is guarded (returns true)");
        Assert(ProcessBlacklist.IsProtected(null), "Null process name is guarded (returns true)");

        Assert(!ProcessBlacklist.IsProtected("cs2.exe"), "cs2.exe is NOT protected (optimizable)");
        Assert(!ProcessBlacklist.IsProtected("dota2.exe"), "dota2.exe is NOT protected (optimizable)");
        Assert(!ProcessBlacklist.IsProtected("chrome.exe"), "chrome.exe is NOT protected (can be deprioritized)");
    }

    static void TestProcessTunerAndRecyclingSafety()
    {
        Console.WriteLine("\n2. Testing Process Tuner & PID Recycling Protection (SF-03, SF-07, SF-16):");

        var tuner = new ProcessTunerService();
        var currentProc = Process.GetCurrentProcess();

        // 1. Unconditional Blacklist enforcement on OptimizeGameProcess
        var protectedResult = tuner.OptimizeGameProcess(currentProc);
        Assert(protectedResult == null, "OptimizeGameProcess refuses to alter blacklisted process (SmoothFrame.Runner)");

        // 2. PID Recycling Prevention: Non-existent PID
        var nonExistentState = new ProcessState
        {
            ProcessId = 999999,
            ProcessName = "ghost_process",
            OriginalPriorityClass = NativeMethods.IDLE_PRIORITY_CLASS
        };
        tuner.AlteredProcesses.Clear();
        tuner.AlteredProcesses.Add(nonExistentState);
        tuner.RestoreProcesses();
        Assert(tuner.AlteredProcesses.Count == 0, "RestoreProcesses safely handled non-existent PID and cleared altered list");

        Process? testProc = null;
        try
        {
            testProc = Process.Start("notepad.exe");
            Assert(testProc != null && !testProc.HasExited, "Spawned un-blacklisted test target process (notepad.exe)");
            Thread.Sleep(500); // Allow OS process loader to initialize main module

            // 3. PID Recycling Prevention: Name mismatch
            var nameMismatchState = new ProcessState
            {
                ProcessId = testProc!.Id,
                ProcessName = "completely_different_name.exe",
                StartTime = testProc.StartTime,
                ExecutablePath = testProc.MainModule?.FileName,
                OriginalPriorityClass = NativeMethods.IDLE_PRIORITY_CLASS
            };
            tuner.RestoreProcesses(new[] { nameMismatchState });
            testProc.Refresh();
            Assert(testProc.PriorityClass != ProcessPriorityClass.Idle, "RestoreProcesses detected name mismatch and aborted modification");

            // 4. PID Recycling Prevention: StartTime mismatch
            var timeMismatchState = new ProcessState
            {
                ProcessId = testProc.Id,
                ProcessName = testProc.ProcessName,
                StartTime = testProc.StartTime.AddHours(-5), // 5 hours in the past
                ExecutablePath = testProc.MainModule?.FileName,
                OriginalPriorityClass = NativeMethods.IDLE_PRIORITY_CLASS
            };
            tuner.RestoreProcesses(new[] { timeMismatchState });
            testProc.Refresh();
            Assert(testProc.PriorityClass != ProcessPriorityClass.Idle, "RestoreProcesses detected StartTime mismatch (PID recycled) and skipped");

            // 5. PID Recycling Prevention: ExecutablePath mismatch
            var pathMismatchState = new ProcessState
            {
                ProcessId = testProc.Id,
                ProcessName = testProc.ProcessName,
                StartTime = testProc.StartTime,
                ExecutablePath = @"C:\FakeDirectory\FakeApp.exe",
                OriginalPriorityClass = NativeMethods.IDLE_PRIORITY_CLASS
            };
            tuner.RestoreProcesses(new[] { pathMismatchState });
            testProc.Refresh();
            Assert(testProc.PriorityClass != ProcessPriorityClass.Idle, "RestoreProcesses detected ExecutablePath mismatch and skipped");

            // 6. StartTime UTC vs Local Normalization (verifies ToUniversalTime logic)
            var utcMatchingState = new ProcessState
            {
                ProcessId = testProc.Id,
                ProcessName = testProc.ProcessName,
                StartTime = testProc.StartTime.ToUniversalTime(), // DateTimeKind.Utc of the same instant
                ExecutablePath = testProc.MainModule?.FileName,
                OriginalPriorityClass = NativeMethods.BELOW_NORMAL_PRIORITY_CLASS
            };
            tuner.RestoreProcesses(new[] { utcMatchingState });
            testProc.Refresh();
            Assert(testProc.PriorityClass == ProcessPriorityClass.BelowNormal, "RestoreProcesses normalized UTC and Local StartTime correctly and restored priority");

            // Restore back to Normal priority
            var restoreNormalState = new ProcessState
            {
                ProcessId = testProc.Id,
                ProcessName = testProc.ProcessName,
                StartTime = testProc.StartTime,
                ExecutablePath = testProc.MainModule?.FileName,
                OriginalPriorityClass = NativeMethods.NORMAL_PRIORITY_CLASS
            };
            tuner.RestoreProcesses(new[] { restoreNormalState });
            testProc.Refresh();
            Assert(testProc.PriorityClass == ProcessPriorityClass.Normal, "Process priority restored back to Normal");

            // 7. Unconditional Blacklist enforcement in RestoreProcesses
            var blacklistedRestoreState = new ProcessState
            {
                ProcessId = testProc.Id,
                ProcessName = "dwm.exe",
                OriginalPriorityClass = NativeMethods.IDLE_PRIORITY_CLASS
            };
            tuner.RestoreProcesses(new[] { blacklistedRestoreState });
            testProc.Refresh();
            Assert(testProc.PriorityClass == ProcessPriorityClass.Normal, "RestoreProcesses unconditionally blocked restore of blacklisted process name");
        }
        finally
        {
            if (testProc != null)
            {
                try
                {
                    if (!testProc.HasExited)
                    {
                        testProc.Kill();
                        testProc.WaitForExit(2000);
                    }
                }
                catch { }
                testProc.Dispose();
            }
        }
    }

    static void TestTimerResolution()
    {
        Console.WriteLine("\n3. Testing System Timer Resolution Service:");

        using var timer = new TimerResolutionService();
        var initialMs = timer.CurrentResolutionMs;

        var success = timer.EnableHighPrecision();
        var alteredMs = timer.CurrentResolutionMs;

        Assert(success && alteredMs <= 1.0, "High precision timer enabled (<= 1.0ms)", $"Initial: {initialMs:F2}ms -> High Precision: {alteredMs:F3}ms");

        timer.Revert();
        Assert(timer.CurrentResolutionMs >= 0.9, "Timer safely reverted to original", $"Reverted: {timer.CurrentResolutionMs:F2}ms");
    }

    static void TestPowerPlanService()
    {
        Console.WriteLine("\n4. Testing Power Scheme Management & Rollback:");

        using var power = new PowerPlanService();
        var initialGuid = power.CurrentPlan;

        var success = power.SwitchToHighPerformance();
        if (success)
        {
            Assert(power.CurrentPlan != initialGuid && power.CurrentPlan != Guid.Empty, "Switched to High/Ultimate Performance scheme (Elevated Mode)", $"Active Scheme GUID: {power.CurrentPlan}");
            power.RestoreOriginalPlan();
            Assert(power.CurrentPlan == initialGuid, "Power scheme safely restored to original GUID", $"Restored GUID: {power.CurrentPlan}");
        }
        else
        {
            Assert(power.LastOperationRequiresAdmin, "Gracefully handled non-elevated user mode (Requires Administrator for Power API)", "Skipped in non-admin test runner; will run elevated in app via app.manifest");
            power.RestoreOriginalPlan();
            Assert(power.CurrentPlan == initialGuid, "Original power scheme preserved intact", $"Current GUID: {power.CurrentPlan}");
        }
    }

    static void TestSnapshotManager(string? isolatedDir = null)
    {
        Console.WriteLine("\n5. Testing Atomic Snapshot & Crash Recovery Storage (SF-12):");

        var testDir = isolatedDir ?? Path.Combine(Path.GetTempPath(), "SmoothFrame_Runner_Snapshot_" + Guid.NewGuid().ToString("N"));
        try
        {
            var manager = new SnapshotManager(testDir);
            Assert(manager.StorageDirectory == testDir, "SnapshotManager initialized with isolated directory", testDir);
            Assert(manager.SnapshotFilePath == Path.Combine(testDir, "active_snapshot.json"), "SnapshotFilePath points within isolated directory");

            var testSnapshot = new SystemStateSnapshot
            {
                OptimizedGameName = "UnitTestGame",
                OriginalPowerPlanGuid = Guid.NewGuid(),
                HasPowerPlanChanged = true,
                HasTimerResolutionChanged = true
            };

            manager.SaveSnapshot(testSnapshot);
            var hasDangling = manager.HasDanglingSnapshot();
            Assert(hasDangling, "Snapshot written to isolated disk storage");

            var loaded = manager.LoadActiveSnapshot();
            Assert(loaded != null && loaded.OptimizedGameName == "UnitTestGame", "Snapshot deserialized successfully with matching game name");

            manager.ClearSnapshot();
            Assert(!manager.HasDanglingSnapshot(), "Snapshot cleared successfully from isolated storage (Clean cleanup)");
        }
        finally
        {
            try { if (Directory.Exists(testDir)) Directory.Delete(testDir, true); } catch { }
        }
    }

    static void TestGameDiscovery()
    {
        Console.WriteLine("\n6. Testing Game Discovery (Steam, Epic, Custom):");

        var testDir = Path.Combine(Path.GetTempPath(), "SmoothFrame_Runner_Games_" + Guid.NewGuid().ToString("N"));
        try
        {
            var discovery = new GameDiscoveryService(testDir);
            var games = discovery.GetAllGames();

            Assert(games != null, "Game scanning executed without exceptions", $"Detected {games?.Count ?? 0} total installed games");

            var fakePath = Path.Combine(AppContext.BaseDirectory, "SmoothFrame.Runner.exe");
            discovery.AddCustomGame(fakePath, "SmoothFrame Test Runner");
            var customGames = discovery.GetCustomGames();
            var found = customGames.Find(g => g.ExecutablePath.Equals(fakePath, StringComparison.OrdinalIgnoreCase));

            Assert(found != null, "Added custom game executable to database");

            if (found != null)
            {
                discovery.RemoveCustomGame(found.Id);
                var updated = discovery.GetCustomGames();
                Assert(updated.Find(g => g.Id == found.Id) == null, "Removed custom game cleanly from database");
            }
        }
        finally
        {
            try { if (Directory.Exists(testDir)) Directory.Delete(testDir, true); } catch { }
        }
    }

    static void TestOptimizationCoordinator(string? isolatedDir = null)
    {
        Console.WriteLine("\n7. Testing End-to-End Optimization Coordinator & Watcher Lifecycle (SF-04, SF-12):");

        var testDir = isolatedDir ?? Path.Combine(Path.GetTempPath(), "SmoothFrame_Runner_Coord_" + Guid.NewGuid().ToString("N"));
        try
        {
            var snapshotManager = new SnapshotManager(testDir);
            using var coordinator = new OptimizationCoordinator(snapshotManager);
            var fakeGame = new GameInfo
            {
                Name = "SmoothFrame Test Game",
                ProcessName = "notepad", // safe standard process
                ExecutablePath = "notepad.exe"
            };

            var options = new OptimizationOptions
            {
                HighPrecisionTimer = true,
                HighPerformancePowerPlan = false, // test timer & state rollback
                ElevateGamePriority = true, // exercise watcher task
                DeprioritizeBackgroundApps = false
            };

            var result = coordinator.ActivateAsync(fakeGame, options).GetAwaiter().GetResult();
            Assert(result.Success && coordinator.IsActive, "Coordinator activated optimization profile and launched watcher task", $"Message: {result.Message}");

            // DeactivateAsync cancels CTS, awaits watcher task, and reverts system state
            coordinator.DeactivateAsync().GetAwaiter().GetResult();
            Assert(!coordinator.IsActive, "Coordinator cleanly deactivated and executed complete rollback");

            // Verify that subsequent activation and deactivation cycles succeed cleanly without orphaned tasks
            var secondResult = coordinator.ActivateAsync(fakeGame, options).GetAwaiter().GetResult();
            Assert(secondResult.Success && coordinator.IsActive, "Coordinator cleanly re-activated in second cycle");
            coordinator.DeactivateAsync().GetAwaiter().GetResult();
            Assert(!coordinator.IsActive, "Coordinator cleanly deactivated in second cycle");

            // 3. Test concurrent DeactivateAsync re-entrancy / race prevention
            var thirdResult = coordinator.ActivateAsync(fakeGame, options).GetAwaiter().GetResult();
            Assert(thirdResult.Success && coordinator.IsActive, "Coordinator activated for concurrent deactivation test");

            bool concurrentDeactivationPassed = true;
            try
            {
                Task.WhenAll(coordinator.DeactivateAsync(), coordinator.DeactivateAsync()).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                concurrentDeactivationPassed = false;
                Console.WriteLine($"Concurrent DeactivateAsync failed: {ex}");
            }
            Assert(concurrentDeactivationPassed && !coordinator.IsActive, "Concurrent DeactivateAsync executed safely without race conditions or exceptions");

            // 4. Test redundant DeactivateAsync on already inactive coordinator
            bool idempotentDeactivationPassed = true;
            try
            {
                coordinator.DeactivateAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                idempotentDeactivationPassed = false;
                Console.WriteLine($"Idempotent DeactivateAsync failed: {ex}");
            }
            Assert(idempotentDeactivationPassed && !coordinator.IsActive, "Redundant DeactivateAsync on already inactive coordinator returns safely");
        }
        finally
        {
            try { if (Directory.Exists(testDir)) Directory.Delete(testDir, true); } catch { }
        }
    }

    static void TestSnapshotIsolationGuard()
    {
        Console.WriteLine("\n8. Testing Snapshot Storage Isolation & User Data Guard (SF-12):");

        string realAppData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmoothFrame"
        );
        string realSnapshotPath = Path.Combine(realAppData, "active_snapshot.json");

        var tempDir = Path.Combine(Path.GetTempPath(), "SmoothFrame_Guard_" + Guid.NewGuid().ToString("N"));
        try
        {
            var isolatedManager = new SnapshotManager(tempDir);
            Assert(isolatedManager.StorageDirectory == tempDir, "Isolated SnapshotManager targets temp directory");
            Assert(isolatedManager.SnapshotFilePath == Path.Combine(tempDir, "active_snapshot.json"), "Isolated file path is within temp directory");
            Assert(isolatedManager.StorageDirectory != realAppData, "Isolated directory is strictly separate from default AppData");

            bool realExistedBefore = File.Exists(realSnapshotPath);
            DateTime realMtimeBefore = realExistedBefore ? File.GetLastWriteTimeUtc(realSnapshotPath) : DateTime.MinValue;

            var testSnapshot = new SystemStateSnapshot
            {
                OptimizedGameName = "IsolationCanaryGame",
                OriginalPowerPlanGuid = Guid.NewGuid(),
                HasPowerPlanChanged = true
            };

            isolatedManager.SaveSnapshot(testSnapshot);
            Assert(File.Exists(isolatedManager.SnapshotFilePath), "Snapshot saved into isolated temp path");

            if (!realExistedBefore)
            {
                Assert(!File.Exists(realSnapshotPath), "Production active_snapshot.json was NOT created by isolated SaveSnapshot");
            }
            else
            {
                Assert(File.Exists(realSnapshotPath), "Pre-existing production active_snapshot.json preserved");
                Assert(File.GetLastWriteTimeUtc(realSnapshotPath) == realMtimeBefore, "Production active_snapshot.json timestamp untouched");
            }

            isolatedManager.ClearSnapshot();
            Assert(!File.Exists(isolatedManager.SnapshotFilePath), "Snapshot removed from isolated temp path");

            if (!realExistedBefore)
            {
                Assert(!File.Exists(realSnapshotPath), "Production active_snapshot.json remains absent after ClearSnapshot");
            }
            else
            {
                Assert(File.Exists(realSnapshotPath), "Pre-existing production active_snapshot.json still preserved after ClearSnapshot");
            }
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }
}
