using System.Runtime.CompilerServices;
using ReactiveUI;

namespace MainCore.Test
{
    // 2026-10-29 (CI, Test step): "TypeInitializationException: ReactiveUI.RxApp ... ArgumentException: Destination array
    // was not long enough". RxApp has a static constructor that is not safe when several test classes touch it for the first
    // time on different threads at once, and this project runs test classes in parallel. Touching RxApp here, once, on the
    // loading thread, before any test class runs, removes the race. Nothing else is configured: the tests keep ReactiveUI's defaults.
    internal static class TestAssemblyInit
    {
        [ModuleInitializer]
        internal static void Init()
        {
            _ = RxApp.TaskpoolScheduler;
            _ = RxApp.MainThreadScheduler;
        }
    }
}
