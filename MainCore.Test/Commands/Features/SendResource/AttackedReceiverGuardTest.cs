namespace MainCore.Test.Commands.Features.SendResource
{
    // CLAUDE.md rule 27 (2026-11-26): a task that ships resources to ANOTHER village must not pick a receiver that is
    // under attack. Live: Balance refilled a village 27 s after Defend had emptied it, 7-11 min before the attack landed.
    // This reads the sources: every task file that sends through SendResourceCommand has to mention the guard
    // (AttackedReceiverGuard, or the IsUnderAttack flag in its receiver query). A new shipping task fails the build
    // until it does the same.
    public class AttackedReceiverGuardTest
    {
        private static string? FindTasksDirectory()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "MainCore");
                if (File.Exists(Path.Combine(candidate, "MainCore.csproj"))) return Path.Combine(candidate, "Tasks");
                dir = dir.Parent;
            }
            return null;
        }

        [Fact]
        public void EveryShippingTask_ChecksTheReceiversAttackFlag()
        {
            var tasks = FindTasksDirectory();
            if (tasks is null || !Directory.Exists(tasks)) return; // sources are not next to the test binaries

            var offenders = new List<string>();
            foreach (var file in Directory.EnumerateFiles(tasks, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                if (!text.Contains("SendResourceCommand.Handler")) continue;
                if (text.Contains("AttackedReceiverGuard") || text.Contains("IsUnderAttack")) continue;
                offenders.Add(Path.GetFileName(file));
            }

            offenders.ShouldBeEmpty("these tasks ship resources but never look at the receiver's attack flag (CLAUDE.md rule 27): " + string.Join(", ", offenders));
        }
    }
}
