using System.Reflection;
using DotProlog.Packages;

if (args is ["--version"])
{
    Console.WriteLine(
        "propack "
            + Assembly
                .GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                .InformationalVersion.Split('+')[0]
    );
    return 0;
}
return PackageCommands.Execute(args, Console.Out, Console.Error);
