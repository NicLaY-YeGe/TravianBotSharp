using System.Reflection;
public static class Runner
{
    public static int Main()
    {
        int pass = 0, fail = 0;
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes().Where(t => t.IsClass && t.IsPublic && t.Namespace?.StartsWith("MainCore.Test") == true))
        {
            foreach (var m in type.GetMethods().Where(m => m.GetCustomAttribute<FactAttribute>() != null))
            {
                var cases = m.GetCustomAttributes<InlineDataAttribute>().Select(a => a.Data).ToList();
                if (cases.Count == 0) cases.Add(Array.Empty<object?>());
                foreach (var data in cases)
                {
                    var name = $"{type.Name}.{m.Name}({string.Join(", ", data.Select(d => d?.ToString()))})";
                    try
                    {
                        var inst = Activator.CreateInstance(type);
                        var r = m.Invoke(inst, data);
                        pass++; Console.WriteLine("PASS " + name);
                    }
                    catch (TargetInvocationException ex) { fail++; Console.WriteLine("FAIL " + name + " -> " + ex.InnerException?.GetType().Name + ": " + ex.InnerException?.Message); }
                    catch (Exception ex) { fail++; Console.WriteLine("FAIL " + name + " -> " + ex.GetType().Name + ": " + ex.Message); }
                }
            }
        }
        Console.WriteLine($"\n{pass} passed, {fail} failed");
        return fail == 0 ? 0 : 1;
    }
}
