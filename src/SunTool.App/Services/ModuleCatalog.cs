using System.Reflection;
using SunTool.Core.Logging;
using SunTool.Core.Modules;

namespace SunTool.App.Services;

internal static class ModuleCatalog
{
    public static IReadOnlyList<IToolModule> Discover(IEnumerable<Assembly> assemblies, IAppLogger log)
    {
        var modules = new List<IToolModule>();
        foreach (var assembly in assemblies.Distinct())
        {
            foreach (var type in GetLoadableTypes(assembly, log))
            {
                if (!typeof(IToolModule).IsAssignableFrom(type)
                    || type is not { IsClass: true, IsAbstract: false }
                    || type.GetConstructor(Type.EmptyTypes) is null)
                {
                    continue;
                }

                try
                {
                    var module = (IToolModule)Activator.CreateInstance(type)!;
                    modules.Add(module);
                    log.Info($"모듈 로드: {module.DisplayName} ({module.Id}) ← {assembly.GetName().Name}");
                }
                catch (Exception ex)
                {
                    log.Error($"모듈 생성 실패: {type.FullName}", ex);
                }
            }
        }

        var duplicates = modules.GroupBy(m => m.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        foreach (var id in duplicates)
        {
            log.Warn($"모듈 ID가 중복됩니다: {id}");
        }

        return modules.OrderBy(m => m.Order).ThenBy(m => m.DisplayName).ToList();
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly, IAppLogger log)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            log.Warn($"{assembly.GetName().Name} 일부 타입을 불러오지 못했습니다.", ex);
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
